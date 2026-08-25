using System.Reflection;
using Auraline.Contracts;
using InfoPanel.Auraline.Adapters;
using InfoPanel.Auraline.Core;
using InfoPanel.Auraline.Platform.Windows;
using InfoPanel.Plugins;
using InfoPanel.Plugins.Graphics;

namespace InfoPanel.Auraline;

public sealed class AuralinePlugin : BasePlugin, IPluginConfigurable, IPluginImageProvider, IPluginImageConsumerAware
{
    internal const string PrimaryImageId = "waveform";
    internal const string SecondaryImageId = "waveform-2";
    internal const string DefaultEndpoint = "http://127.0.0.1:48481";

    private readonly object _gate = new();
    private readonly PluginText _status = new("status", "Connection State", PluginConnectionState.Disconnected.ToString());
    private readonly PluginText _hostVersion = new("host_version", "Host Contract", "-");
    private readonly PluginText _selectedProfile = new("selected_profile", "Selected Profile", AuralineProfiles.DefaultProfileId);
    private readonly PluginText _sessions = new("sessions", "Sessions", "-");
    private readonly PluginText _frame = new("frame", "Latest Frame", "-");
    private readonly PluginText _reconnects = new("reconnects", "Reconnect Count", "0");
    private readonly PluginText _lastError = new("last_error", "Last Error", "-");
    private AuralinePluginRuntime? _runtime;
    private IReadOnlyList<PluginImageConsumerDemand> _demands = [];
    private string _endpoint = DefaultEndpoint;
    private string _profileId = AuralineProfiles.DefaultProfileId;
    private int _targetFps = 30;
    private DateTimeOffset _nextDiagnosticsUpdateUtc = DateTimeOffset.MinValue;

    public AuralinePlugin()
        : base(
            "auraline-plugin",
            "Auraline",
            "Displays waveform frames rendered by the local Auraline Host.")
    {
    }

    // InfoPanel waits this interval after UpdateAsync completes. Wake at a bounded
    // 2x producer cadence so the latest-only reader does not systematically miss
    // Host frames; duplicate sequences are rejected before image publication.
    public override TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(1000d / (_targetFps * 2d));

    public IReadOnlyList<PluginImageDescriptor> ImageDescriptors { get; } =
    [
        new(PrimaryImageId, "Auraline Waveform", 320, 120),
        new(SecondaryImageId, "Auraline Waveform 2", 320, 120)
    ];

    public IReadOnlyList<PluginConfigProperty> ConfigProperties
    {
        get
        {
            lock (_gate)
            {
                var profiles = _runtime?.GetProfiles() ?? [];
                var selected = profiles.FirstOrDefault(profile =>
                    string.Equals(profile.ProfileId, _profileId, StringComparison.Ordinal));
                var options = profiles.Select(ProfileChoice.Format).ToArray();
                var value = selected is null ? _profileId : ProfileChoice.Format(selected);
                if (options.Length == 0) options = [value];
                return
                [
                    new PluginConfigProperty
                    {
                        Key = "HostEndpoint",
                        DisplayName = "Auraline Host Endpoint",
                        Description = "Numeric loopback HTTP endpoint for Auraline Host.",
                        Type = PluginConfigType.String,
                        Value = _endpoint
                    },
                    new PluginConfigProperty
                    {
                        Key = "Profile",
                        DisplayName = "Auraline Profile",
                        Description = "Friendly profile name with its stable ID retained in brackets.",
                        Type = PluginConfigType.Choice,
                        Value = value,
                        Options = options
                    },
                    new PluginConfigProperty
                    {
                        Key = "TargetFps",
                        DisplayName = "Target FPS",
                        Description = "Auraline render-session cadence.",
                        Type = PluginConfigType.Choice,
                        Value = _targetFps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Options = ["30", "60"]
                    }
                ];
            }
        }
    }

    public override void Initialize()
    {
        lock (_gate)
        {
            _runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var version = GetType().Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+', 2)[0] ?? "unknown";
            _runtime = new AuralinePluginRuntime(
                endpoint => new AuralineHostClient(endpoint),
                new WindowsSharedMemoryFrameReaderFactory(),
                new SystemPluginRuntimeClock(),
                version);
            _runtime.Configure(new Uri(_endpoint), _profileId, _targetFps);
            _runtime.SetDemands(_demands.Select(ToCoreDemand));
            _nextDiagnosticsUpdateUtc = DateTimeOffset.MinValue;
        }
    }

    public override void Load(List<IPluginContainer> containers)
    {
        var container = new PluginContainer("Auraline");
        container.Entries.AddRange([_status, _hostVersion, _selectedProfile, _sessions, _frame, _reconnects, _lastError]);
        containers.Add(container);
    }

    public void OnImageBuffersReady(IReadOnlyDictionary<string, IPluginImageWriter> writers)
    {
        var sinks = ImageDescriptors.Select(descriptor =>
        {
            if (!writers.TryGetValue(descriptor.Id, out var writer))
                throw new InvalidOperationException($"InfoPanel did not provide image writer '{descriptor.Id}'.");
            return (IPluginFrameSink)new InfoPanelFrameSink(descriptor.Id, writer);
        }).ToArray();
        lock (_gate) _runtime?.SetSinks(sinks);
    }

    public void OnImageConsumerDemandsChanged(IReadOnlyList<PluginImageConsumerDemand> demands)
    {
        var snapshot = demands.ToArray();
        lock (_gate)
        {
            _demands = snapshot;
            _runtime?.SetDemands(snapshot.Select(ToCoreDemand));
        }
    }

    public void ApplyConfig(string key, object? value)
    {
        lock (_gate)
        {
            switch (key)
            {
                case "HostEndpoint":
                    var endpoint = new Uri(value?.ToString()?.Trim() ?? DefaultEndpoint, UriKind.Absolute);
                    AuralinePluginRuntime.ValidateEndpoint(endpoint);
                    _endpoint = endpoint.ToString().TrimEnd('/');
                    break;
                case "Profile":
                    _profileId = ProfileChoice.ParseProfileId(value?.ToString());
                    break;
                case "TargetFps":
                    if (!int.TryParse(value?.ToString(), out var fps) || fps is not (30 or 60))
                        throw new ArgumentException("Target FPS must be 30 or 60.", nameof(value));
                    _targetFps = fps;
                    break;
                default:
                    return;
            }
            _runtime?.Configure(new Uri(_endpoint), _profileId, _targetFps);
        }
    }

    public override void Update() => throw new NotSupportedException("Auraline uses UpdateAsync.");

    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        AuralinePluginRuntime? runtime;
        lock (_gate) runtime = _runtime;
        if (runtime is null) return;
        await runtime.TickAsync(cancellationToken).ConfigureAwait(false);
        if (DateTimeOffset.UtcNow < _nextDiagnosticsUpdateUtc) return;
        UpdateDiagnostics(runtime.GetDiagnostics());
        _nextDiagnosticsUpdateUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
    }

    public override void Close()
    {
        AuralinePluginRuntime? runtime;
        lock (_gate)
        {
            runtime = _runtime;
            _runtime = null;
        }
        runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _status.Value = PluginConnectionState.Disconnected.ToString();
    }

    private void UpdateDiagnostics(PluginRuntimeDiagnostics diagnostics)
    {
        _status.Value = diagnostics.State.ToString();
        _hostVersion.Value = diagnostics.HostVersion is null
            ? $"plugin {diagnostics.PluginVersion}; host -"
            : $"plugin {diagnostics.PluginVersion}; host {diagnostics.HostVersion}; contract {ContractVersion.Current}";
        _selectedProfile.Value = diagnostics.SelectedProfileName is null
            ? diagnostics.SelectedProfileId
            : $"{diagnostics.SelectedProfileName} [{diagnostics.SelectedProfileId}]";
        _sessions.Value = string.Join("; ", diagnostics.Outputs.Select(output =>
            output.SessionId is null
                ? $"{output.ImageId}: idle"
                : $"{output.ImageId}: {output.SessionId} {output.Width}x{output.Height}@{output.TargetFps}"));
        var latest = diagnostics.Outputs.OrderByDescending(output => output.LatestFrameUtc).FirstOrDefault();
        _frame.Value = latest?.LatestFrameUtc is null
            ? "-"
            : $"{latest.ImageId}: sequence {latest.LatestSequence}; age {Math.Max(0, (DateTimeOffset.UtcNow - latest.LatestFrameUtc.Value).TotalMilliseconds):F0} ms";
        _reconnects.Value = diagnostics.ReconnectCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lastError.Value = diagnostics.LastError ?? "-";
    }

    private static ImageConsumerDemand ToCoreDemand(PluginImageConsumerDemand demand) =>
        new(demand.ImageId, demand.ConsumerId, demand.Width, demand.Height);
}
