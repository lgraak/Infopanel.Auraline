using System.Diagnostics;
using System.Text.Json.Serialization;
using Auraline.Contracts;
using Auraline.Host.Configuration;
using Auraline.Host.Diagnostics;
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
    [property: JsonPropertyName("profile_revision")] long ProfileRevision,
    [property: JsonPropertyName("hot_apply_count")] long HotApplyCount,
    [property: JsonPropertyName("actual_fps")] double ActualFps,
    [property: JsonPropertyName("rendered_frames")] long RenderedFrames,
    [property: JsonPropertyName("published_sequence")] ulong PublishedSequence,
    [property: JsonPropertyName("latest_render_duration_ms")] double? LatestRenderDurationMs,
    [property: JsonPropertyName("average_render_duration_ms")] double? AverageRenderDurationMs,
    [property: JsonPropertyName("target_frame_interval_ms")] double TargetFrameIntervalMs,
    [property: JsonPropertyName("scheduled_deadline_monotonic_ms")] double? ScheduledDeadlineMonotonicMs,
    [property: JsonPropertyName("latest_render_start_monotonic_ms")] double? LatestRenderStartMonotonicMs,
    [property: JsonPropertyName("latest_scheduler_lateness_ms")] double? LatestSchedulerLatenessMs,
    [property: JsonPropertyName("maximum_scheduler_lateness_ms")] double MaximumSchedulerLatenessMs,
    [property: JsonPropertyName("scheduler_lateness_counts")] StallThresholdCounts SchedulerLatenessCounts,
    [property: JsonPropertyName("latest_publication_monotonic_ms")] double? LatestPublicationMonotonicMs,
    [property: JsonPropertyName("latest_publication_interval_ms")] double? LatestPublicationIntervalMs,
    [property: JsonPropertyName("maximum_publication_interval_ms")] double MaximumPublicationIntervalMs,
    [property: JsonPropertyName("maximum_publication_interval_sequence")] ulong? MaximumPublicationIntervalSequence,
    [property: JsonPropertyName("publication_interval_counts")] StallThresholdCounts PublicationIntervalCounts,
    [property: JsonPropertyName("latest_renderer_duration_ms")] double? LatestRendererDurationMs,
    [property: JsonPropertyName("latest_transport_publication_duration_ms")] double? LatestTransportPublicationDurationMs,
    [property: JsonPropertyName("latest_render_to_publish_duration_ms")] double? LatestRenderToPublishDurationMs,
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
    [property: JsonPropertyName("hot_apply_count")] long HotApplyCount,
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
    private readonly IProfileCatalog? _profiles;
    private readonly StallObservability _observability;
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
        ILogger<RenderSessionManager> logger,
        IProfileCatalog? profiles = null,
        StallObservability? observability = null)
    {
        if (options.SessionCap <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        _transportFactory = transportFactory;
        _waveform = waveform;
        _renderer = renderer;
        _clock = clock;
        _options = options;
        _logger = logger;
        _profiles = profiles;
        _observability = observability ?? new StallObservability(new SystemObservabilityClock());
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
                session = new SessionRuntime(lookupKey, transport, _waveform, _renderer, _clock, _logger, _profiles, _observability, now);
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
                sessions.Sum(item => item.HotApplyCount),
                sessions);
        }
    }

    public RenderSessionDiagnostic? GetDiagnostic(string sessionId) =>
        GetDiagnostics().Sessions.FirstOrDefault(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));

    public bool IsProfileInUse(string profileId)
    {
        lock (_gate)
        {
            ExpireLeasesLocked(_clock.UtcNow);
            return _sessions.Values.Any(item =>
                item.Leases.Count > 0 && item.LookupKey.SemanticKey.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        }
    }

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
        if (_profiles is null)
        {
            if (!string.Equals(profileId, AuralineProfiles.DefaultProfileId, StringComparison.Ordinal))
                throw new KeyNotFoundException($"Unknown profile '{profileId}'.");
        }
        else
        {
            var profile = _profiles.GetProfile(profileId);
            if (_profiles is ProductConfigurationStore products && !products.IsRuntimeSupported(profile))
                throw new RenderSessionCapacityException("The selected source group is preserved but multi-source, cross-provider, and explicit-source rendering are not implemented in M5.");
        }
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

    internal static double CalculateSchedulerLateness(IObservabilityClock clock, long deadlineTimestamp, long actualTimestamp) =>
        Math.Max(0, clock.ElapsedMilliseconds(deadlineTimestamp, actualTimestamp));

    internal static double CalculatePublicationInterval(IObservabilityClock clock, long previousTimestamp, long currentTimestamp) =>
        Math.Max(0, clock.ElapsedMilliseconds(previousTimestamp, currentTimestamp));

    private readonly record struct SessionLookupKey(RenderSessionKey SemanticKey, int TargetFps);

    private sealed class SessionRuntime
    {
        private readonly object _metricsGate = new();
        private readonly IAuralineFrameTransport _transport;
        private readonly IWaveformRenderStateSource _waveform;
        private readonly WaveformRenderer _renderer;
        private readonly IRenderSessionClock _clock;
        private readonly ILogger _logger;
        private readonly IProfileCatalog? _profiles;
        private readonly StallObservability _observability;
        private readonly CancellationTokenSource _cancellation = new();
        private Task? _renderTask;
        private long _renderedFrames;
        private ulong _sequence;
        private double? _latestDurationMs;
        private double? _averageDurationMs;
        private long _profileRevision = 1;
        private long _hotApplyCount;
        private readonly double _targetFrameIntervalMs;
        private long? _scheduledDeadlineTimestamp;
        private long? _latestRenderStartTimestamp;
        private double? _latestSchedulerLatenessMs;
        private double _maximumSchedulerLatenessMs;
        private StallThresholdCounts _schedulerLatenessCounts = new(0, 0, 0, 0);
        private long? _latestPublicationTimestamp;
        private double? _latestPublicationIntervalMs;
        private double _maximumPublicationIntervalMs;
        private ulong? _maximumPublicationIntervalSequence;
        private StallThresholdCounts _publicationIntervalCounts = new(0, 0, 0, 0);
        private double? _latestRendererDurationMs;
        private double? _latestTransportPublicationDurationMs;
        private double? _latestRenderToPublishDurationMs;

        public SessionRuntime(
            SessionLookupKey lookupKey,
            IAuralineFrameTransport transport,
            IWaveformRenderStateSource waveform,
            WaveformRenderer renderer,
            IRenderSessionClock clock,
            ILogger logger,
            IProfileCatalog? profiles,
            StallObservability observability,
            DateTimeOffset createdAtUtc)
        {
            LookupKey = lookupKey;
            _transport = transport;
            _waveform = waveform;
            _renderer = renderer;
            _clock = clock;
            _logger = logger;
            _profiles = profiles;
            _observability = observability;
            _targetFrameIntervalMs = 1000d / lookupKey.TargetFps;
            if (_profiles is not null) _profileRevision = _profiles.GetProfile(lookupKey.SemanticKey.ProfileId).Revision;
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
        public long HotApplyCount { get { lock (_metricsGate) return _hotApplyCount; } }

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
                    _profileRevision,
                    _hotApplyCount,
                    _renderedFrames / elapsed,
                    _renderedFrames,
                    _sequence,
                    _latestDurationMs,
                    _averageDurationMs,
                    _targetFrameIntervalMs,
                    _scheduledDeadlineTimestamp is { } deadline ? _observability.Clock.ToMilliseconds(deadline) : null,
                    _latestRenderStartTimestamp is { } renderStart ? _observability.Clock.ToMilliseconds(renderStart) : null,
                    _latestSchedulerLatenessMs,
                    _maximumSchedulerLatenessMs,
                    _schedulerLatenessCounts,
                    _latestPublicationTimestamp is { } publication ? _observability.Clock.ToMilliseconds(publication) : null,
                    _latestPublicationIntervalMs,
                    _maximumPublicationIntervalMs,
                    _maximumPublicationIntervalSequence,
                    _publicationIntervalCounts,
                    _latestRendererDurationMs,
                    _latestTransportPublicationDurationMs,
                    _latestRenderToPublishDurationMs,
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
            var observabilityClock = _observability.Clock;
            var scheduledDeadlineTimestamp = observabilityClock.Timestamp;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    var renderStartTimestamp = observabilityClock.Timestamp;
                    var schedulerLatenessMs = CalculateSchedulerLateness(observabilityClock, scheduledDeadlineTimestamp, renderStartTimestamp);
                    var snapshot = _waveform.CaptureRenderState();
                    var profile = _profiles?.GetProfile(LookupKey.SemanticKey.ProfileId);
                    if (profile is not null && profile.Revision != _profileRevision)
                    {
                        lock (_metricsGate)
                        {
                            if (profile.Revision != _profileRevision)
                            {
                                _profileRevision = profile.Revision;
                                _hotApplyCount++;
                            }
                        }
                    }
                    var sequence = checked(_sequence + 1);
                    var timestamp = _clock.UtcNow;
                    var rendererStartTimestamp = observabilityClock.Timestamp;
                    var rendered = _renderer.Render(
                        snapshot.ProcessedFrame,
                        snapshot.VisualState,
                        LookupKey.SemanticKey.Width,
                        LookupKey.SemanticKey.Height,
                        sequence,
                        timestamp,
                        LookupKey.TargetFps,
                        unchecked((int)sequence),
                        profile?.Waveform);
                    var rendererEndTimestamp = observabilityClock.Timestamp;
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
                    var publicationTimestamp = observabilityClock.Timestamp;
                    stopwatch.Stop();
                    var rendererDurationMs = observabilityClock.ElapsedMilliseconds(rendererStartTimestamp, rendererEndTimestamp);
                    var publicationDurationMs = observabilityClock.ElapsedMilliseconds(rendererEndTimestamp, publicationTimestamp);
                    var cycleDurationMs = observabilityClock.ElapsedMilliseconds(renderStartTimestamp, publicationTimestamp);
                    double? publicationIntervalMs = null;
                    if (_latestPublicationTimestamp is { } previousPublication)
                        publicationIntervalMs = CalculatePublicationInterval(observabilityClock, previousPublication, publicationTimestamp);
                    lock (_metricsGate)
                    {
                        _sequence = sequence;
                        _renderedFrames++;
                        _latestDurationMs = stopwatch.Elapsed.TotalMilliseconds;
                        _averageDurationMs = _averageDurationMs is null
                            ? _latestDurationMs
                            : _averageDurationMs.Value * 0.8 + _latestDurationMs.Value * 0.2;
                        _scheduledDeadlineTimestamp = scheduledDeadlineTimestamp;
                        _latestRenderStartTimestamp = renderStartTimestamp;
                        _latestSchedulerLatenessMs = schedulerLatenessMs;
                        _maximumSchedulerLatenessMs = Math.Max(_maximumSchedulerLatenessMs, schedulerLatenessMs);
                        _schedulerLatenessCounts = StallObservability.IncrementThresholds(_schedulerLatenessCounts, schedulerLatenessMs);
                        _latestPublicationTimestamp = publicationTimestamp;
                        _latestPublicationIntervalMs = publicationIntervalMs;
                        if (publicationIntervalMs > _maximumPublicationIntervalMs)
                        {
                            _maximumPublicationIntervalMs = publicationIntervalMs.Value;
                            _maximumPublicationIntervalSequence = sequence;
                        }
                        if (publicationIntervalMs is { } intervalMs)
                            _publicationIntervalCounts = StallObservability.IncrementThresholds(_publicationIntervalCounts, intervalMs);
                        _latestRendererDurationMs = rendererDurationMs;
                        _latestTransportPublicationDurationMs = publicationDurationMs;
                        _latestRenderToPublishDurationMs = cycleDurationMs;
                    }
                    if (schedulerLatenessMs > StallObservability.SignificantTimingThresholdMs)
                        _observability.Record("scheduler_lateness", SessionId, durationMs: schedulerLatenessMs, sequence: sequence);
                    if (publicationIntervalMs > StallObservability.SignificantTimingThresholdMs)
                        _observability.Record("publication_interval", SessionId, durationMs: publicationIntervalMs, sequence: sequence);

                    var now = _clock.UtcNow;
                    nextDeadline = CalculateNextDeadline(nextDeadline, interval, now);
                    var delay = nextDeadline - now;
                    scheduledDeadlineTimestamp = observabilityClock.Add(observabilityClock.Timestamp, delay);
                    await _clock.Delay(delay, cancellationToken).ConfigureAwait(false);
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
