using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Auraline.Contracts;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;
using Auraline.Host.Web;

namespace Auraline.Host.Diagnostics;

public enum DiagnosticErrorCategory
{
    HostUnavailable, ProviderUnavailable, ProtocolIncompatible, SourceUnavailable, SourceUnresolved,
    ProfileInvalid, TransportIncompatible, SharedMemorySessionFailure, InfoPanelConsumerIncompatible,
    ConfigurationMalformed, InternalError
}

public enum SelfTestStageStatus { Pass, Fail, Skipped }

public sealed record SelfTestStage(string Name, SelfTestStageStatus Status, string Reason, long DurationMs);
public sealed record SelfTestResult(DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, string OverallResult, IReadOnlyList<SelfTestStage> Stages)
{
    public long DurationMs => Math.Max(0, (long)(EndedAtUtc - StartedAtUtc).TotalMilliseconds);
}

public sealed record DiagnosticsSnapshot(
    string HostVersion,
    string ReleaseChannel,
    ContractVersion ContractVersion,
    int ResonanceSignalProtocolVersion,
    string OperatingSystem,
    string Runtime,
    string Architecture,
    string LogLevel,
    HealthContract Health,
    IReadOnlyList<ProviderStatus> Providers,
    IReadOnlyList<SourceGroupStatus> SourceGroups,
    IReadOnlyList<ProfileDefinition> Profiles,
    SelfTestResult? LatestSelfTest,
    string? LatestMeaningfulError,
    StallObservabilitySnapshot StallObservability,
    string ExternalReleaseGate);

public sealed record DiagnosticsExport(string FileName, byte[] Content);

public sealed class DiagnosticsRedactor
{
    private readonly (Regex Pattern, string Replacement)[] _rules;

    public DiagnosticsRedactor()
    {
        var user = Environment.UserName;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var machine = Environment.MachineName;
        var rules = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(profile)) rules.Add((Regex.Escape(profile), "[USER_PROFILE]"));
        if (!string.IsNullOrWhiteSpace(user)) rules.Add(($@"(?<![A-Za-z0-9]){Regex.Escape(user)}(?![A-Za-z0-9])", "[USERNAME]"));
        if (!string.IsNullOrWhiteSpace(machine)) rules.Add(($@"(?<![A-Za-z0-9]){Regex.Escape(machine)}(?![A-Za-z0-9])", "[HOSTNAME]"));
        rules.Add((@"(?i)(?:[A-Z]:\\Users\\)[^\\\s]+", "[USER_PROFILE]"));
        rules.Add((@"(?i)(authorization|api[_-]?key|token|secret|password)(\s*[:=]\s*)([^\s,;]+)", "$1$2[REDACTED]"));
        _rules = rules.Select(rule => (new Regex(rule.Item1, RegexOptions.CultureInvariant), rule.Item2)).ToArray();
    }

    public string Redact(string value)
    {
        foreach (var rule in _rules) value = rule.Pattern.Replace(value, rule.Replacement);
        return value;
    }
}

public sealed class DiagnosticsService(
    HostStatusService status,
    ProviderManager providers,
    ProductConfigurationStore products,
    ConfigurationStore configuration,
    IProviderConnector providerConnector,
    IAuralineFrameTransportFactory transportFactory,
    IWaveformSelfTester waveformSelfTester,
    WaveformRenderer renderer,
    AuralinePaths paths,
    DiagnosticLogLevel logLevel,
    DiagnosticsRedactor redactor,
    StallObservability stallObservability)
{
    public const string ExternalReleaseGate = "Public beta distribution requires an InfoPanel build containing the generic plugin image consumer-dimension capability used by InfoPanel.Auraline.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } };
    private readonly SemaphoreSlim _selfTestGate = new(1, 1);
    private SelfTestResult? _latestSelfTest;

    public DiagnosticsSnapshot GetSnapshot()
    {
        var health = status.GetHealth();
        var providerStates = providers.GetStatuses();
        var groups = products.GetGroups().Select(group => products.ResolveGroup(group.Id, providerStates)).ToArray();
        var error = health.ConfigurationError
            ?? health.Waveform?.LastError
            ?? providerStates.Select(item => item.LastError).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        return new(HostStatusService.Version, "beta", ContractVersion.Current, 1,
            RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier, logLevel.Current, health, providerStates, groups,
            products.GetProfiles(), _latestSelfTest, error, stallObservability.GetSnapshot(), ExternalReleaseGate);
    }

    public async Task<SelfTestResult> RunSelfTestAsync(CancellationToken cancellationToken)
    {
        await _selfTestGate.WaitAsync(cancellationToken);
        try
        {
            var started = DateTimeOffset.UtcNow;
            var stages = new List<SelfTestStage>();
            Run("Host configuration", () => _ = configuration.Current, stages);
            Run("Provider configuration", () =>
            {
                if (providers.GetStatuses().Count == 0) throw new InvalidDataException("No provider is configured.");
            }, stages);

            var provider = providers.GetStatuses().FirstOrDefault(item => item.Enabled);
            if (provider is null)
            {
                Skip("Provider contact", "No provider is enabled.", stages);
                Skip("Source discovery", "Provider contact is unavailable.", stages);
                Skip("Logical source resolution", "Provider contact is unavailable.", stages);
                Skip("Waveform session and decode", "Provider contact is unavailable.", stages);
            }
            else
            {
                ProviderConnectionResult? discovery = null;
                var configured = configuration.Current.Providers.Single(item => item.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase));
                var contactTimer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    discovery = await providerConnector.ConnectAndDiscoverAsync(configured, cancellationToken);
                    Pass("Provider contact", $"{provider.FriendlyName} responded with protocol-compatible status.", stages, contactTimer.ElapsedMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stages.Add(new("Provider contact", SelfTestStageStatus.Skipped, $"Environmental unavailability: {Categorize(ex)}", contactTimer.ElapsedMilliseconds));
                }
                if (discovery is null)
                {
                    Skip("Source discovery", "Provider contact is unavailable.", stages);
                    Skip("Logical source resolution", "Provider contact is unavailable.", stages);
                    Skip("Waveform session and decode", "Provider contact is unavailable.", stages);
                }
                else
                {
                    if (discovery.Sources.Count == 0) Skip("Source discovery", "Provider returned no sources.", stages);
                    else Pass("Source discovery", $"Discovered {discovery.Sources.Count} source(s).", stages);
                    var resolved = discovery.Sources.FirstOrDefault(item => item.DefaultPlayback && item.Availability.Equals("available", StringComparison.OrdinalIgnoreCase));
                    if (resolved is null)
                    {
                        Skip("Logical source resolution", "Default Playback is not currently available.", stages);
                        Skip("Waveform session and decode", "Default Playback is unavailable.", stages);
                    }
                    else
                    {
                        Pass("Logical source resolution", $"Resolved {resolved.DisplayName ?? resolved.SourceId}.", stages);
                        var waveformTimer = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var evidence = await waveformSelfTester.OpenAndDecodeAsync(configured.Endpoint, cancellationToken);
                            Pass("Waveform session and decode", $"Opened isolated stream {evidence.StreamId} and decoded sequence {evidence.Sequence}.", stages, waveformTimer.ElapsedMilliseconds);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            stages.Add(new("Waveform session and decode", SelfTestStageStatus.Fail, Categorize(ex), waveformTimer.ElapsedMilliseconds));
                        }
                    }
                }
            }

            WaveformRenderedFrame rendered = null!;
            Run("Renderer", () => rendered = renderer.Render(
                new WaveformProcessedFrame("self-test", 1, 1, 0, [0f, .5f, -.5f, 0f], [[0f, .5f, -.5f, 0f]]),
                WaveformVisualizationState.Active, 64, 32, 1, DateTimeOffset.UtcNow, 30, 1), stages);
            IAuralineFrameTransport? transport = null;
            IAuralineFrameReader? reader = null;
            try
            {
                Run("Temporary render session", () => transport = transportFactory.Create(64, 32, 30), stages);
                Run("Windows frame transport allocation", () => reader = transportFactory.Open(transport!.Descriptor), stages);
                Run("Frame publish and readback", () =>
                {
                    transport!.Publish(new FramePublication(rendered.Width, rendered.Height, rendered.Stride, rendered.PixelFormat,
                        rendered.Premultiplied, rendered.Sequence, rendered.TimestampTicks, rendered.TargetFps, rendered.Pixels));
                    if (!reader!.TryReadLatest(out var read) || read is null || read.Sequence != rendered.Sequence || !read.Pixels.AsSpan().SequenceEqual(rendered.Pixels))
                        throw new InvalidDataException("Temporary transport did not return the published frame.");
                }, stages);
            }
            finally
            {
                reader?.Dispose();
                if (transport is not null) await transport.DisposeAsync();
            }
            Pass("Temporary resource cleanup", "Isolated transport resources were released; active consumers were not used.", stages);
            var ended = DateTimeOffset.UtcNow;
            var overall = stages.Any(item => item.Status == SelfTestStageStatus.Fail) ? "Fail"
                : stages.Any(item => item.Status == SelfTestStageStatus.Skipped) ? "Partial" : "Pass";
            return _latestSelfTest = new(started, ended, overall, stages);
        }
        finally { _selfTestGate.Release(); }
    }

    public string CreateMarkdownSummary()
    {
        var s = GetSnapshot();
        var waveformHealth = s.Health.Waveform;
        var sessions = s.Health.RenderSessions;
        var provider = s.Providers.FirstOrDefault(item => item.Enabled);
        var lines = new[]
        {
            "# Auraline diagnostics", "",
            $"- Auraline: {s.HostVersion} ({s.ReleaseChannel})",
            $"- Host/plugin contract: {s.ContractVersion}",
            $"- OS/runtime: {s.OperatingSystem}; {s.Runtime}; {s.Architecture}",
            $"- Provider: {(provider is null ? "none enabled" : $"{provider.FriendlyName} — {provider.State}")}",
            $"- Resonance Signal protocol: {s.ResonanceSignalProtocolVersion}",
            $"- Waveform: {waveformHealth?.VisualState ?? "unavailable"}; stream {waveformHealth?.StreamId ?? "—"}",
            $"- Default profile: {s.Health.ProductConfiguration?.DefaultProfileId ?? "—"}",
            $"- Render sessions/leases: {sessions?.ActiveSessionCount ?? 0}/{sessions?.TotalConsumerLeases ?? 0}",
            $"- Reconnects: provider {s.Providers.Sum(item => item.ReconnectCount)}; waveform {waveformHealth?.ReconnectAttempts ?? 0}",
            $"- Runtime GC: gen0 {s.StallObservability.RuntimeGc.Gen0Collections}; gen1 {s.StallObservability.RuntimeGc.Gen1Collections}; gen2 {s.StallObservability.RuntimeGc.Gen2Collections}; total pause {s.StallObservability.RuntimeGc.TotalPauseDurationMs?.ToString("F3") ?? "unavailable"} ms",
            $"- Significant timing events retained: {s.StallObservability.SignificantEvents.Count}/{s.StallObservability.EventCapacity}",
            $"- Latest self-test: {s.LatestSelfTest?.OverallResult ?? "not run"}",
            $"- Latest meaningful error: {s.LatestMeaningfulError ?? "none"}",
            "", "No audio samples, waveform samples, or rendered frame pixels are included. Obvious local identifiers are redacted."
        };
        return redactor.Redact(string.Join("\n", lines));
    }

    public DiagnosticsExport CreateExport(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.Now;
        var snapshot = GetSnapshot();
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddText(archive, "diagnostics-summary.md", CreateMarkdownSummary());
            AddJson(archive, "build.json", new { snapshot.HostVersion, snapshot.ReleaseChannel, snapshot.ContractVersion, snapshot.ResonanceSignalProtocolVersion, snapshot.OperatingSystem, snapshot.Runtime, snapshot.Architecture });
            AddJson(archive, "providers.json", snapshot.Providers);
            AddJson(archive, "sources.json", snapshot.Providers.SelectMany(item => item.Sources));
            AddJson(archive, "source-groups.json", snapshot.SourceGroups);
            AddJson(archive, "profiles.json", snapshot.Profiles.Select(item => new { item.SchemaVersion, item.Id, item.FriendlyName, item.SourceGroupId, item.Revision, item.VisualizationType }));
            AddJson(archive, "render-sessions.json", snapshot.Health.RenderSessions);
            AddJson(archive, "stall-observability.json", snapshot.StallObservability);
            AddJson(archive, "self-test.json", snapshot.LatestSelfTest);
            AddJson(archive, "configuration.json", new { HostSchemaVersion = 1, ProductSchemaVersion = ProductCatalogDocument.CurrentSchemaVersion, snapshot.Health.ProductConfiguration });
            AddText(archive, "privacy.txt", "No audio samples, waveform samples, or rendered frame pixels are included. Obvious user, profile-path, hostname, and secret-like values are redacted by default. Logs may contain technical endpoint, provider, source, and profile names.");
            foreach (var file in Directory.Exists(paths.LogsDirectory)
                         ? Directory.EnumerateFiles(paths.LogsDirectory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(7)
                         : [])
                AddText(archive, $"logs/{Path.GetFileName(file)}", ReadBounded(file));
        }
        return new($"auraline-diagnostics-{timestamp:yyyyMMdd-HHmmss}.zip", memory.ToArray());
    }

    private string ReadBounded(string file)
    {
        const int maxBytes = 10 * 1024 * 1024;
        using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > maxBytes) stream.Seek(-maxBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return redactor.Redact(reader.ReadToEnd());
    }

    private void AddJson(ZipArchive archive, string name, object? value) => AddText(archive, name, JsonSerializer.Serialize(value, JsonOptions));
    private void AddText(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(redactor.Redact(value));
    }

    private static void Run(string name, Action action, List<SelfTestStage> stages)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try { action(); Pass(name, "Completed successfully.", stages, started.ElapsedMilliseconds); }
        catch (Exception ex) { stages.Add(new(name, SelfTestStageStatus.Fail, Categorize(ex), started.ElapsedMilliseconds)); }
    }
    private static void Pass(string name, string reason, List<SelfTestStage> stages, long duration = 0) => stages.Add(new(name, SelfTestStageStatus.Pass, reason, duration));
    private static void Skip(string name, string reason, List<SelfTestStage> stages) => stages.Add(new(name, SelfTestStageStatus.Skipped, reason, 0));
    public static string Categorize(Exception ex) => ex switch
    {
        ProviderCompatibilityException => $"{DiagnosticErrorCategory.ProtocolIncompatible}: {ex.Message}",
        HttpRequestException => $"{DiagnosticErrorCategory.ProviderUnavailable}: {ex.Message}",
        TimeoutException => $"{DiagnosticErrorCategory.SourceUnavailable}: {ex.Message}",
        IOException => $"{DiagnosticErrorCategory.SourceUnavailable}: {ex.Message}",
        InvalidDataException => $"{DiagnosticErrorCategory.ConfigurationMalformed}: {ex.Message}",
        NotSupportedException => $"{DiagnosticErrorCategory.TransportIncompatible}: {ex.Message}",
        _ => $"{DiagnosticErrorCategory.InternalError}: {ex.Message}"
    };
}
