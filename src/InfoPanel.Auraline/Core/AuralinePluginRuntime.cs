using System.Net;
using Auraline.Contracts;

namespace InfoPanel.Auraline.Core;

internal sealed class AuralinePluginRuntime : IAsyncDisposable
{
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DisconnectGrace = TimeSpan.FromMilliseconds(1500);
    internal static readonly TimeSpan FrameStaleTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly Func<Uri, IAuralineHostClient> _clientFactory;
    private readonly IPluginFrameReaderFactory _readerFactory;
    private readonly IPluginRuntimeClock _clock;
    private readonly string _pluginVersion;
    private readonly ReconnectBackoff _connectionBackoff = new();
    private readonly Dictionary<string, OutputRuntime> _outputs = new(StringComparer.Ordinal);
    private IReadOnlyList<ImageConsumerDemand> _demands = [];
    private Uri _endpoint = new("http://127.0.0.1:48481");
    private string _selectedProfileId = AuralineProfiles.DefaultProfileId;
    private int _targetFps = 30;
    private IAuralineHostClient? _client;
    private AuralineProfileCatalog? _catalog;
    private PluginConnectionState _state = PluginConnectionState.Disconnected;
    private DateTimeOffset _nextConnectionAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _nextCatalogRefreshUtc = DateTimeOffset.MinValue;
    private long _reconnectCount;
    private string? _lastError;
    private bool _everConnected;
    private bool _resetRequested;
    private bool _disposed;

    public AuralinePluginRuntime(
        Func<Uri, IAuralineHostClient> clientFactory,
        IPluginFrameReaderFactory readerFactory,
        IPluginRuntimeClock clock,
        string pluginVersion)
    {
        _clientFactory = clientFactory;
        _readerFactory = readerFactory;
        _clock = clock;
        _pluginVersion = pluginVersion;
    }

    public void SetSinks(IEnumerable<IPluginFrameSink> sinks)
    {
        lock (_gate)
        {
            foreach (var sink in sinks)
            {
                if (_outputs.TryGetValue(sink.ImageId, out var existing))
                    existing.Sink = sink;
                else
                    _outputs[sink.ImageId] = new OutputRuntime(sink);
            }
        }
    }

    public void SetDemands(IEnumerable<ImageConsumerDemand> demands)
    {
        var snapshot = demands
            .Where(demand => !string.IsNullOrWhiteSpace(demand.ImageId) &&
                             !string.IsNullOrWhiteSpace(demand.ConsumerId) &&
                             demand.Width is >= 16 and <= 2048 &&
                             demand.Height is >= 16 and <= 2048)
            .Distinct()
            .ToArray();
        lock (_gate) _demands = snapshot;
    }

    public void Configure(Uri endpoint, string profileId, int targetFps)
    {
        ValidateEndpoint(endpoint);
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Profile ID is required.", nameof(profileId));
        if (targetFps is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(targetFps));

        lock (_gate)
        {
            if (_endpoint == endpoint &&
                string.Equals(_selectedProfileId, profileId, StringComparison.Ordinal) &&
                _targetFps == targetFps)
                return;
            _endpoint = endpoint;
            _selectedProfileId = profileId;
            _targetFps = targetFps;
            _resetRequested = true;
        }
    }

    public IReadOnlyList<AuralineProfileSummary> GetProfiles()
    {
        lock (_gate) return _catalog?.Profiles.ToArray() ?? [];
    }

    public PluginRuntimeDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            var selected = _catalog?.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, _selectedProfileId, StringComparison.Ordinal));
            var outputError = _outputs.Values
                .OrderBy(output => output.Sink.ImageId, StringComparer.Ordinal)
                .Select(output => output.LastError)
                .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error));
            return new PluginRuntimeDiagnostics(
                _pluginVersion,
                _endpoint.ToString(),
                _catalog?.HostVersion,
                _selectedProfileId,
                selected?.FriendlyName,
                _state,
                _reconnectCount,
                _lastError ?? outputError,
                _outputs.Values
                    .OrderBy(output => output.Sink.ImageId, StringComparer.Ordinal)
                    .Select(output => new OutputDiagnostics(
                        output.Sink.ImageId,
                        output.Active?.Attachment.Session.SessionId,
                        output.Active?.Attachment.Session.Width,
                        output.Active?.Attachment.Session.Height,
                        _targetFps,
                        output.LatestSequence,
                        output.LatestFrameUtc))
                    .ToArray());
        }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TakeResetRequest()) await ResetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false))
            {
                PublishUnavailableAfterGrace();
                return;
            }

            OutputRuntime[] outputs;
            ImageConsumerDemand[] demands;
            string profileId;
            int targetFps;
            lock (_gate)
            {
                outputs = _outputs.Values.ToArray();
                demands = _demands.ToArray();
                profileId = _selectedProfileId;
                targetFps = _targetFps;
            }

            foreach (var output in outputs)
            {
                var desired = SelectDemand(demands, output.Sink.ImageId);
                if (desired is null)
                {
                    await ReleaseOutputAsync(output, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await ServiceOutputAsync(output, profileId, desired, targetFps, cancellationToken).ConfigureAwait(false);
            }
            PublishUnavailableAfterGrace();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TransitionDisconnectedAsync(ex, cancellationToken).ConfigureAwait(false);
            PublishUnavailableAfterGrace();
        }
        finally
        {
            _tickGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _tickGate.WaitAsync().ConfigureAwait(false);
        try
        {
            OutputRuntime[] outputs;
            lock (_gate) outputs = _outputs.Values.ToArray();
            foreach (var output in outputs)
                await ReleaseOutputAsync(output, CancellationToken.None).ConfigureAwait(false);
            _client?.Dispose();
            _client = null;
            lock (_gate) _state = PluginConnectionState.Disconnected;
        }
        finally
        {
            _tickGate.Release();
            _tickGate.Dispose();
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        PluginConnectionState state;
        DateTimeOffset refreshAt;
        DateTimeOffset attemptAt;
        lock (_gate)
        {
            state = _state;
            refreshAt = _nextCatalogRefreshUtc;
            attemptAt = _nextConnectionAttemptUtc;
        }

        if (state == PluginConnectionState.Connected && now < refreshAt) return true;
        if (now < attemptAt && state != PluginConnectionState.Connected) return false;

        lock (_gate)
            _state = _everConnected ? PluginConnectionState.Reconnecting : PluginConnectionState.Connecting;

        try
        {
            _client ??= _clientFactory(GetEndpoint());
            var catalog = await _client.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
            if (!ContractVersion.Current.IsCompatibleWith(catalog.ContractVersion))
                throw new ContractIncompatibleException(
                    $"Unsupported Auraline Host contract major version {catalog.ContractVersion.Major}.");
            var profileId = GetSelectedProfileId();
            var selected = catalog.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
            OutputRuntime[] staleOutputs;
            lock (_gate)
            {
                _catalog = catalog;
                _nextCatalogRefreshUtc = now + CatalogRefreshInterval;
                if (selected is null)
                {
                    _state = PluginConnectionState.Unavailable;
                    _lastError = $"Selected Auraline profile '{profileId}' is unavailable.";
                    _nextConnectionAttemptUtc = now + CatalogRefreshInterval;
                    staleOutputs = _outputs.Values.ToArray();
                }
                else
                {
                    _state = PluginConnectionState.Connected;
                    _lastError = null;
                    _everConnected = true;
                    _nextConnectionAttemptUtc = DateTimeOffset.MinValue;
                    staleOutputs = [];
                }
            }
            if (selected is null)
            {
                var error = $"Selected Auraline profile '{profileId}' is unavailable.";
                foreach (var output in staleOutputs)
                {
                    output.MarkFailure(now, error);
                    await ReleaseOutputAsync(output, cancellationToken).ConfigureAwait(false);
                }
                return false;
            }
            _connectionBackoff.Reset();
            return true;
        }
        catch (ContractIncompatibleException ex)
        {
            await MarkConnectionFailureAsync(PluginConnectionState.Incompatible, ex.Message, cancellationToken)
                .ConfigureAwait(false);
            return false;
        }
        catch (AuralineHostException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            await MarkConnectionFailureAsync(
                PluginConnectionState.Incompatible,
                "Auraline Host does not expose the M4 profile catalog contract.",
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or AuralineHostException)
        {
            await MarkConnectionFailureAsync(
                _everConnected ? PluginConnectionState.Reconnecting : PluginConnectionState.Unavailable,
                SafeError(ex),
                cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task ServiceOutputAsync(
        OutputRuntime output,
        string profileId,
        ImageConsumerDemand desired,
        int targetFps,
        CancellationToken cancellationToken)
    {
        var desiredKey = new OutputKey(profileId, desired.Width, desired.Height, targetFps);
        if (output.Pending is not null && output.Pending.Key != desiredKey)
        {
            await ReleaseSessionAsync(output.Pending, cancellationToken).ConfigureAwait(false);
            output.Pending = null;
        }

        if (output.Active?.Key != desiredKey && output.Pending is null)
        {
            if (_clock.UtcNow < output.NextAttachAttemptUtc) return;
            RenderSessionAttachment? attachment = null;
            try
            {
                var client = _client ?? throw new InvalidOperationException("Host client is not connected.");
                attachment = await client.AttachAsync(
                    profileId,
                    desired.Width,
                    desired.Height,
                    targetFps,
                    cancellationToken).ConfigureAwait(false);
                ValidateAttachment(attachment, desiredKey);
                var reader = _readerFactory.Open(attachment.Session.Transport);
                var openedUtc = _clock.UtcNow;
                output.Pending = new SessionHandle(
                    desiredKey,
                    attachment,
                    reader,
                    openedUtc,
                    openedUtc + HeartbeatInterval);
                output.AttachBackoff.Reset();
            }
            catch (AuralineHostException ex) when (ex.StatusCode == HttpStatusCode.UpgradeRequired)
            {
                await DetachAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
                await MarkConnectionFailureAsync(PluginConnectionState.Incompatible, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (ContractIncompatibleException ex)
            {
                await DetachAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
                await MarkConnectionFailureAsync(PluginConnectionState.Incompatible, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (NotSupportedException ex)
            {
                await DetachAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
                await MarkConnectionFailureAsync(PluginConnectionState.Incompatible, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (AuralineHostException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await DetachAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _state = PluginConnectionState.Unavailable;
                    _lastError = ex.Message;
                }
                output.MarkFailure(_clock.UtcNow, ex.Message);
                return;
            }
            catch (Exception ex) when (ex is AuralineHostException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                await DetachAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
                output.NextAttachAttemptUtc = _clock.UtcNow + output.AttachBackoff.Next();
                output.MarkFailure(_clock.UtcNow, SafeError(ex));
                return;
            }
        }

        if (output.Pending is not null)
        {
            var pendingResult = await TryConsumeAsync(output, output.Pending, cancellationToken).ConfigureAwait(false);
            if (pendingResult == ConsumeResult.FramePublished)
            {
                var old = output.Active;
                output.Active = output.Pending;
                output.Pending = null;
                if (old is not null) await ReleaseSessionAsync(old, cancellationToken).ConfigureAwait(false);
            }
            else if (pendingResult == ConsumeResult.SessionLost)
            {
                await ReleaseSessionAsync(output.Pending, cancellationToken).ConfigureAwait(false);
                output.Pending = null;
            }
        }

        if (output.Active is not null && output.Active.Key == desiredKey)
        {
            var result = await TryConsumeAsync(output, output.Active, cancellationToken).ConfigureAwait(false);
            if (result == ConsumeResult.SessionLost)
            {
                await ReleaseSessionAsync(output.Active, cancellationToken).ConfigureAwait(false);
                output.Active = null;
                output.NextAttachAttemptUtc = _clock.UtcNow + output.AttachBackoff.Next();
            }
        }
    }

    private async Task<ConsumeResult> TryConsumeAsync(
        OutputRuntime output,
        SessionHandle session,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_clock.UtcNow >= session.NextHeartbeatUtc)
            {
                var renewed = await (_client ?? throw new InvalidOperationException()).HeartbeatAsync(
                    session.Attachment.Session.SessionId,
                    session.Attachment.Lease.LeaseId,
                    cancellationToken).ConfigureAwait(false);
                if (renewed is null)
                {
                    session.Dispose();
                    output.MarkFailure(_clock.UtcNow, "Auraline render-session lease expired.");
                    return ConsumeResult.SessionLost;
                }
                session.Attachment = session.Attachment with { Lease = renewed };
                session.NextHeartbeatUtc = _clock.UtcNow + HeartbeatInterval;
            }

            if (session.Reader.TryReadLatest(out var frame) && frame is not null)
            {
                ValidateFrame(frame, session.Key);
                output.Sink.Publish(frame);
                output.LatestSequence = frame.Sequence;
                output.LatestFrameUtc = _clock.UtcNow;
                session.LatestFrameUtc = _clock.UtcNow;
                output.FailureBeganUtc = null;
                output.UnavailablePublished = false;
                output.LastError = null;
                return ConsumeResult.FramePublished;
            }

            var sessionProgressUtc = session.LatestFrameUtc ?? session.OpenedUtc;
            if (_clock.UtcNow - sessionProgressUtc > FrameStaleTimeout)
            {
                session.Dispose();
                output.MarkFailure(_clock.UtcNow, "Auraline frame transport stopped advancing.");
                return ConsumeResult.SessionLost;
            }
            return ConsumeResult.NoFrame;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            session.Dispose();
            output.MarkFailure(_clock.UtcNow, SafeError(ex));
            return ConsumeResult.SessionLost;
        }
    }

    private async Task TransitionDisconnectedAsync(Exception exception, CancellationToken cancellationToken)
    {
        await MarkConnectionFailureAsync(
            _everConnected ? PluginConnectionState.Reconnecting : PluginConnectionState.Unavailable,
            SafeError(exception),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkConnectionFailureAsync(
        PluginConnectionState state,
        string error,
        CancellationToken cancellationToken)
    {
        OutputRuntime[] outputs;
        lock (_gate)
        {
            _state = state;
            _lastError = error;
            _reconnectCount++;
            _nextConnectionAttemptUtc = _clock.UtcNow + _connectionBackoff.Next();
            outputs = _outputs.Values.ToArray();
        }
        foreach (var output in outputs)
        {
            output.MarkFailure(_clock.UtcNow, error);
            await ReleaseOutputAsync(output, cancellationToken).ConfigureAwait(false);
        }
        _client?.Dispose();
        _client = null;
    }

    private async Task ResetConnectionAsync(CancellationToken cancellationToken)
    {
        OutputRuntime[] outputs;
        lock (_gate)
        {
            outputs = _outputs.Values.ToArray();
            _catalog = null;
            _state = PluginConnectionState.Disconnected;
            _lastError = null;
            _nextConnectionAttemptUtc = DateTimeOffset.MinValue;
            _nextCatalogRefreshUtc = DateTimeOffset.MinValue;
        }
        foreach (var output in outputs)
            await ReleaseOutputAsync(output, cancellationToken).ConfigureAwait(false);
        _client?.Dispose();
        _client = null;
        _connectionBackoff.Reset();
    }

    private async Task ReleaseOutputAsync(OutputRuntime output, CancellationToken cancellationToken)
    {
        if (output.Pending is not null)
        {
            await ReleaseSessionAsync(output.Pending, cancellationToken).ConfigureAwait(false);
            output.Pending = null;
        }
        if (output.Active is not null)
        {
            await ReleaseSessionAsync(output.Active, cancellationToken).ConfigureAwait(false);
            output.Active = null;
        }
    }

    private async Task ReleaseSessionAsync(SessionHandle session, CancellationToken cancellationToken)
    {
        session.Dispose();
        await DetachAttachmentAsync(session.Attachment, cancellationToken).ConfigureAwait(false);
    }

    private async Task DetachAttachmentAsync(
        RenderSessionAttachment? attachment,
        CancellationToken cancellationToken)
    {
        if (attachment is null) return;
        if (_client is null) return;
        try
        {
            await _client.DetachAsync(
                attachment.Session.SessionId,
                attachment.Lease.LeaseId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AuralineHostException)
        {
        }
    }

    private void PublishUnavailableAfterGrace()
    {
        var now = _clock.UtcNow;
        OutputRuntime[] outputs;
        lock (_gate) outputs = _outputs.Values.ToArray();
        foreach (var output in outputs)
        {
            if (output.UnavailablePublished || output.FailureBeganUtc is not { } began || now - began < DisconnectGrace)
                continue;
            output.Sink.PublishUnavailable(output.LastError ?? "Auraline unavailable");
            output.UnavailablePublished = true;
        }
    }

    private static ImageConsumerDemand? SelectDemand(
        IEnumerable<ImageConsumerDemand> demands,
        string imageId) =>
        demands
            .Where(demand => string.Equals(demand.ImageId, imageId, StringComparison.Ordinal))
            .OrderByDescending(demand => (long)demand.Width * demand.Height)
            .ThenByDescending(demand => demand.Width)
            .ThenByDescending(demand => demand.Height)
            .ThenBy(demand => demand.ConsumerId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static void ValidateAttachment(RenderSessionAttachment attachment, OutputKey expected)
    {
        if (!ContractVersion.Current.IsCompatibleWith(attachment.Session.ContractVersion))
            throw new ContractIncompatibleException(
                $"Unsupported render-session contract major version {attachment.Session.ContractVersion.Major}.");
        if (!string.Equals(attachment.Session.ProfileId, expected.ProfileId, StringComparison.Ordinal) ||
            attachment.Session.Width != expected.Width ||
            attachment.Session.Height != expected.Height ||
            attachment.Session.TargetFps != expected.TargetFps ||
            !string.Equals(attachment.Lease.SessionId, attachment.Session.SessionId, StringComparison.Ordinal))
            throw new InvalidDataException("Auraline Host returned a render session that did not match the request.");
    }

    private static void ValidateFrame(FrameReadResult frame, OutputKey expected)
    {
        if (frame.Width != expected.Width || frame.Height != expected.Height ||
            frame.Stride != checked(expected.Width * 4) ||
            frame.Pixels.Length != checked(frame.Stride * frame.Height) ||
            !string.Equals(frame.PixelFormat, "rgba8888-premul", StringComparison.Ordinal) ||
            !frame.Premultiplied || frame.TargetFps != expected.TargetFps)
            throw new InvalidDataException("Auraline frame geometry or pixel layout did not match the negotiated session.");
    }

    internal static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(endpoint.Host, "127.0.0.1", StringComparison.Ordinal) || endpoint.Port <= 0)
            throw new ArgumentException("Auraline Host endpoint must be numeric HTTP loopback (127.0.0.1).", nameof(endpoint));
    }

    private bool TakeResetRequest()
    {
        lock (_gate)
        {
            if (!_resetRequested) return false;
            _resetRequested = false;
            return true;
        }
    }

    private Uri GetEndpoint()
    {
        lock (_gate) return _endpoint;
    }

    private string GetSelectedProfileId()
    {
        lock (_gate) return _selectedProfileId;
    }

    private static string SafeError(Exception exception) => exception switch
    {
        TaskCanceledException => "Auraline Host request timed out.",
        HttpRequestException => "Auraline Host is unavailable.",
        _ => exception.Message
    };

    private readonly record struct OutputKey(string ProfileId, int Width, int Height, int TargetFps);

    private enum ConsumeResult
    {
        NoFrame,
        FramePublished,
        SessionLost
    }

    private sealed class OutputRuntime(IPluginFrameSink sink)
    {
        public IPluginFrameSink Sink { get; set; } = sink;
        public SessionHandle? Active { get; set; }
        public SessionHandle? Pending { get; set; }
        public ReconnectBackoff AttachBackoff { get; } = new();
        public DateTimeOffset NextAttachAttemptUtc { get; set; }
        public ulong LatestSequence { get; set; }
        public DateTimeOffset? LatestFrameUtc { get; set; }
        public DateTimeOffset? FailureBeganUtc { get; set; }
        public string? LastError { get; set; }
        public bool UnavailablePublished { get; set; }

        public void MarkFailure(DateTimeOffset now, string error)
        {
            FailureBeganUtc ??= now;
            LastError = error;
        }
    }

    private sealed class SessionHandle(
        OutputKey key,
        RenderSessionAttachment attachment,
        IAuralineFrameReader reader,
        DateTimeOffset openedUtc,
        DateTimeOffset nextHeartbeatUtc) : IDisposable
    {
        private bool _disposed;
        public OutputKey Key { get; } = key;
        public RenderSessionAttachment Attachment { get; set; } = attachment;
        public IAuralineFrameReader Reader { get; } = reader;
        public DateTimeOffset OpenedUtc { get; } = openedUtc;
        public DateTimeOffset? LatestFrameUtc { get; set; }
        public DateTimeOffset NextHeartbeatUtc { get; set; } = nextHeartbeatUtc;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reader.Dispose();
        }
    }

    private sealed class ContractIncompatibleException(string message) : Exception(message);
}
