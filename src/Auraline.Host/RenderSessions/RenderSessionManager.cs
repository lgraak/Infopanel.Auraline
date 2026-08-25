using System.Diagnostics;
using System.Text.Json.Serialization;
using Auraline.Contracts;
using Auraline.Host.Waveform;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auraline.Host.RenderSessions;

public sealed record RenderSessionOptions(
    int SessionCap,
    TimeSpan LeaseTimeout,
    TimeSpan TeardownGrace,
    TimeSpan MaintenanceInterval)
{
    public static RenderSessionOptions Default { get; } = new(
        32,
        TimeSpan.FromSeconds(25),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(1));
}

public interface IRenderSessionClock
{
    DateTimeOffset UtcNow { get; }

    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemRenderSessionClock : IRenderSessionClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed record RenderSessionDiagnostic(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("target_fps")] int TargetFps,
    [property: JsonPropertyName("actual_fps")] double ActualFps,
    [property: JsonPropertyName("rendered_frames")] long RenderedFrames,
    [property: JsonPropertyName("published_sequence")] ulong PublishedSequence,
    [property: JsonPropertyName("latest_render_duration_ms")] double? LatestRenderDurationMs,
    [property: JsonPropertyName("average_render_duration_ms")] double? AverageRenderDurationMs,
    [property: JsonPropertyName("allocation_size")] long AllocationSize,
    [property: JsonPropertyName("consumer_count")] int ConsumerCount,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("grace_expires_at_utc")] DateTimeOffset? GraceExpiresAtUtc);

public sealed record RenderSessionDiagnostics(
    [property: JsonPropertyName("active_session_count")] int ActiveSessionCount,
    [property: JsonPropertyName("total_consumer_leases")] int TotalConsumerLeases,
    [property: JsonPropertyName("session_cap")] int SessionCap,
    [property: JsonPropertyName("session_creation_count")] long SessionCreationCount,
    [property: JsonPropertyName("teardown_count")] long TeardownCount,
    [property: JsonPropertyName("eviction_count")] long EvictionCount,
    [property: JsonPropertyName("rejected_session_count")] long RejectedSessionCount,
    [property: JsonPropertyName("sessions")] IReadOnlyList<RenderSessionDiagnostic> Sessions);

public sealed class RenderSessionCapacityException(string message) : Exception(message);

public sealed class RenderSessionManager : IHostedService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionLookupKey, SessionRuntime> _sessions = [];
    private readonly IAuralineFrameTransportFactory _transportFactory;
    private readonly IWaveformRenderStateSource _waveform;
    private readonly WaveformRenderer _renderer;
    private readonly IRenderSessionClock _clock;
    private readonly RenderSessionOptions _options;
    private readonly ILogger<RenderSessionManager> _logger;
    private CancellationTokenSource? _maintenanceCancellation;
    private Task? _maintenanceTask;
    private long _creationCount;
    private long _teardownCount;
    private long _evictionCount;
    private long _rejectedCount;
    private bool _disposed;

    public RenderSessionManager(
        IAuralineFrameTransportFactory transportFactory,
        IWaveformRenderStateSource waveform,
        WaveformRenderer renderer,
        IRenderSessionClock clock,
        RenderSessionOptions options,
        ILogger<RenderSessionManager> logger)
    {
        if (options.SessionCap <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        _transportFactory = transportFactory;
        _waveform = waveform;
        _renderer = renderer;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public RenderSessionAttachment Attach(string profileId, int width, int height, int targetFps, ContractVersion consumerVersion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRequest(profileId, width, height, targetFps, consumerVersion);
        var now = _clock.UtcNow;
        var lookupKey = new SessionLookupKey(new RenderSessionKey(profileId, width, height), targetFps);
        SessionRuntime session;
        SessionRuntime? evicted = null;

        lock (_gate)
        {
            ExpireLeasesLocked(now);
            if (!_sessions.TryGetValue(lookupKey, out session!))
            {
                if (_sessions.Count >= _options.SessionCap)
                {
                    var candidate = _sessions.Values
                        .Where(item => item.Leases.Count == 0)
                        .OrderBy(item => item.LastAccessUtc)
                        .ThenBy(item => item.SessionId, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (candidate is null)
                    {
                        _rejectedCount++;
                        throw new RenderSessionCapacityException("Render-session capacity is fully referenced; no active session can be evicted.");
                    }

                    _sessions.Remove(candidate.LookupKey);
                    candidate.State = RenderSessionState.Stopped;
                    evicted = candidate;
                    _evictionCount++;
                    _teardownCount++;
                }

                evicted?.StopAsync().GetAwaiter().GetResult();
                var transport = _transportFactory.Create(width, height, targetFps);
                session = new SessionRuntime(lookupKey, transport, _waveform, _renderer, _clock, _logger, now);
                _sessions.Add(lookupKey, session);
                _creationCount++;
            }

            var leaseId = Guid.NewGuid().ToString("N");
            var expiresAt = now + _options.LeaseTimeout;
            session.Leases.Add(leaseId, expiresAt);
            session.LastAccessUtc = now;
            session.GraceExpiresAtUtc = null;
            session.State = RenderSessionState.Active;
            var attachment = new RenderSessionAttachment(session.Descriptor, new ConsumerLease(leaseId, session.SessionId, expiresAt));
            session.Start();
            return attachment;
        }
    }

    public ConsumerLease? Heartbeat(string sessionId, string leaseId)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            ExpireLeasesLocked(now);
            var session = FindSessionLocked(sessionId);
            if (session is null || !session.Leases.ContainsKey(leaseId)) return null;
            var expiresAt = now + _options.LeaseTimeout;
            session.Leases[leaseId] = expiresAt;
            session.LastAccessUtc = now;
            return new ConsumerLease(leaseId, sessionId, expiresAt);
        }
    }

    public bool Detach(string sessionId, string leaseId)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            ExpireLeasesLocked(now);
            var session = FindSessionLocked(sessionId);
            if (session is null || !session.Leases.Remove(leaseId)) return false;
            session.LastAccessUtc = now;
            BeginGraceIfIdleLocked(session, now);
            return true;
        }
    }

    public RenderSessionDiagnostics GetDiagnostics()
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            ExpireLeasesLocked(now);
            var sessions = _sessions.Values
                .OrderBy(item => item.SessionId, StringComparer.Ordinal)
                .Select(item => item.GetDiagnostic(now))
                .ToArray();
            return new RenderSessionDiagnostics(
                sessions.Length,
                sessions.Sum(item => item.ConsumerCount),
                _options.SessionCap,
                _creationCount,
                _teardownCount,
                _evictionCount,
                _rejectedCount,
                sessions);
        }
    }

    public RenderSessionDiagnostic? GetDiagnostic(string sessionId) =>
        GetDiagnostics().Sessions.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_maintenanceTask is not null) return Task.CompletedTask;
        _maintenanceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _maintenanceTask = Task.Run(() => MaintainAsync(_maintenanceCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _maintenanceCancellation?.Cancel();
        if (_maintenanceTask is not null)
        {
            try { await _maintenanceTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            _maintenanceTask = null;
        }

        SessionRuntime[] sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
            foreach (var session in sessions) session.State = RenderSessionState.Stopped;
            _teardownCount += sessions.Length;
        }
        await Task.WhenAll(sessions.Select(item => item.StopAsync())).ConfigureAwait(false);
    }

    public async Task SweepAsync()
    {
        SessionRuntime[] expired;
        var now = _clock.UtcNow;
        lock (_gate)
        {
            ExpireLeasesLocked(now);
            expired = _sessions.Values
                .Where(item => item.Leases.Count == 0 && item.GraceExpiresAtUtc <= now)
                .ToArray();
            foreach (var session in expired)
            {
                _sessions.Remove(session.LookupKey);
                session.State = RenderSessionState.Stopped;
            }
            _teardownCount += expired.Length;
        }
        await Task.WhenAll(expired.Select(item => item.StopAsync())).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _maintenanceCancellation?.Dispose();
    }

    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _clock.Delay(_options.MaintenanceInterval, cancellationToken).ConfigureAwait(false);
                await SweepAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ValidateRequest(string profileId, int width, int height, int targetFps, ContractVersion consumerVersion)
    {
        if (!ContractVersion.Current.IsCompatibleWith(consumerVersion))
            throw new NotSupportedException($"Unsupported render-session contract major version {consumerVersion.Major}.");
        if (!string.Equals(profileId, AuralineProfiles.DefaultProfileId, StringComparison.Ordinal))
            throw new KeyNotFoundException($"Unknown profile '{profileId}'.");
        WaveformRenderer.ValidateDimensions(width, height);
        if (targetFps is not (30 or 60))
            throw new ArgumentOutOfRangeException(nameof(targetFps), "Target FPS must be 30 or 60.");
    }

    private void ExpireLeasesLocked(DateTimeOffset now)
    {
        foreach (var session in _sessions.Values)
        {
            var expired = session.Leases.Where(item => item.Value <= now).Select(item => item.Key).ToArray();
            foreach (var leaseId in expired) session.Leases.Remove(leaseId);
            if (expired.Length > 0)
            {
                session.LastAccessUtc = now;
                BeginGraceIfIdleLocked(session, now);
            }
        }
    }

    private void BeginGraceIfIdleLocked(SessionRuntime session, DateTimeOffset now)
    {
        if (session.Leases.Count != 0 || session.GraceExpiresAtUtc is not null) return;
        session.State = RenderSessionState.Grace;
        session.GraceExpiresAtUtc = now + _options.TeardownGrace;
    }

    private SessionRuntime? FindSessionLocked(string sessionId) =>
        _sessions.Values.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));

    internal static DateTimeOffset CalculateNextDeadline(DateTimeOffset previousDeadline, TimeSpan interval, DateTimeOffset now)
    {
        var scheduled = previousDeadline + interval;
        return scheduled <= now ? now + interval : scheduled;
    }

    private readonly record struct SessionLookupKey(RenderSessionKey SemanticKey, int TargetFps);

    private sealed class SessionRuntime
    {
        private readonly object _metricsGate = new();
        private readonly IAuralineFrameTransport _transport;
        private readonly IWaveformRenderStateSource _waveform;
        private readonly WaveformRenderer _renderer;
        private readonly IRenderSessionClock _clock;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellation = new();
        private Task? _renderTask;
        private long _renderedFrames;
        private ulong _sequence;
        private double? _latestDurationMs;
        private double? _averageDurationMs;

        public SessionRuntime(
            SessionLookupKey lookupKey,
            IAuralineFrameTransport transport,
            IWaveformRenderStateSource waveform,
            WaveformRenderer renderer,
            IRenderSessionClock clock,
            ILogger logger,
            DateTimeOffset createdAtUtc)
        {
            LookupKey = lookupKey;
            _transport = transport;
            _waveform = waveform;
            _renderer = renderer;
            _clock = clock;
            _logger = logger;
            CreatedAtUtc = createdAtUtc;
            LastAccessUtc = createdAtUtc;
            SessionId = Guid.NewGuid().ToString("N");
            State = RenderSessionState.Active;
            Descriptor = new RenderSessionDescriptor(
                ContractVersion.Current,
                SessionId,
                lookupKey.SemanticKey.ProfileId,
                lookupKey.SemanticKey.Width,
                lookupKey.SemanticKey.Height,
                lookupKey.TargetFps,
                transport.Descriptor);
        }

        public SessionLookupKey LookupKey { get; }
        public string SessionId { get; }
        public RenderSessionDescriptor Descriptor { get; }
        public Dictionary<string, DateTimeOffset> Leases { get; } = [];
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset LastAccessUtc { get; set; }
        public DateTimeOffset? GraceExpiresAtUtc { get; set; }
        public RenderSessionState State { get; set; }

        public void Start() => _renderTask ??= Task.Run(() => RenderAsync(_cancellation.Token), CancellationToken.None);

        public async Task StopAsync()
        {
            _cancellation.Cancel();
            if (_renderTask is not null)
            {
                try { await _renderTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            }
            await _transport.DisposeAsync().ConfigureAwait(false);
            _cancellation.Dispose();
        }

        public RenderSessionDiagnostic GetDiagnostic(DateTimeOffset now)
        {
            lock (_metricsGate)
            {
                var elapsed = Math.Max(0.001, (now - CreatedAtUtc).TotalSeconds);
                return new RenderSessionDiagnostic(
                    SessionId,
                    LookupKey.SemanticKey.ProfileId,
                    LookupKey.SemanticKey.Width,
                    LookupKey.SemanticKey.Height,
                    LookupKey.TargetFps,
                    _renderedFrames / elapsed,
                    _renderedFrames,
                    _sequence,
                    _latestDurationMs,
                    _averageDurationMs,
                    _transport.Descriptor.AllocationSize,
                    Leases.Count,
                    State.ToString(),
                    GraceExpiresAtUtc);
            }
        }

        private async Task RenderAsync(CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(1d / LookupKey.TargetFps);
            var nextDeadline = _clock.UtcNow;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var snapshot = _waveform.CaptureRenderState();
                    var sequence = checked(_sequence + 1);
                    var timestamp = _clock.UtcNow;
                    var rendered = _renderer.Render(
                        snapshot.ProcessedFrame,
                        snapshot.VisualState,
                        LookupKey.SemanticKey.Width,
                        LookupKey.SemanticKey.Height,
                        sequence,
                        timestamp,
                        LookupKey.TargetFps,
                        unchecked((int)sequence));
                    _transport.Publish(new FramePublication(
                        rendered.Width,
                        rendered.Height,
                        rendered.Stride,
                        rendered.PixelFormat,
                        rendered.Premultiplied,
                        rendered.Sequence,
                        rendered.TimestampTicks,
                        rendered.TargetFps,
                        rendered.Pixels));
                    stopwatch.Stop();
                    lock (_metricsGate)
                    {
                        _sequence = sequence;
                        _renderedFrames++;
                        _latestDurationMs = stopwatch.Elapsed.TotalMilliseconds;
                        _averageDurationMs = _averageDurationMs is null
                            ? _latestDurationMs
                            : _averageDurationMs.Value * 0.8 + _latestDurationMs.Value * 0.2;
                    }

                    var now = _clock.UtcNow;
                    nextDeadline = CalculateNextDeadline(nextDeadline, interval, now);
                    await _clock.Delay(nextDeadline - now, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Render session {SessionId} stopped after a render/publication failure", SessionId);
            }
        }
    }
}
