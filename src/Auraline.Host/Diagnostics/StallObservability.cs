using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Auraline.Host.Diagnostics;

public interface IObservabilityClock
{
    long Timestamp { get; }
    DateTimeOffset UtcNow { get; }
    double ToMilliseconds(long timestamp);
    double ElapsedMilliseconds(long startTimestamp, long endTimestamp);
    long Add(long timestamp, TimeSpan duration);
}

public sealed class SystemObservabilityClock : IObservabilityClock
{
    public long Timestamp => Stopwatch.GetTimestamp();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public double ToMilliseconds(long timestamp) => timestamp * 1000d / Stopwatch.Frequency;
    public double ElapsedMilliseconds(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) * 1000d / Stopwatch.Frequency;
    public long Add(long timestamp, TimeSpan duration) => timestamp + (long)(duration.TotalSeconds * Stopwatch.Frequency);
}

public readonly record struct StallThresholdCounts(
    [property: JsonPropertyName("over_50_ms")] long Over50Ms,
    [property: JsonPropertyName("over_100_ms")] long Over100Ms,
    [property: JsonPropertyName("over_250_ms")] long Over250Ms,
    [property: JsonPropertyName("over_500_ms")] long Over500Ms);

public sealed record StallEvent(
    [property: JsonPropertyName("monotonic_timestamp_ms")] double MonotonicTimestampMs,
    [property: JsonPropertyName("wall_clock_timestamp_utc")] DateTimeOffset WallClockTimestampUtc,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("stream_id")] string? StreamId,
    [property: JsonPropertyName("duration_ms")] double? DurationMs,
    [property: JsonPropertyName("sequence")] ulong? Sequence,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record RuntimeGcMetrics(
    [property: JsonPropertyName("gen0_collections")] int Gen0Collections,
    [property: JsonPropertyName("gen1_collections")] int Gen1Collections,
    [property: JsonPropertyName("gen2_collections")] int Gen2Collections,
    [property: JsonPropertyName("managed_memory_bytes")] long ManagedMemoryBytes,
    [property: JsonPropertyName("heap_size_bytes")] long HeapSizeBytes,
    [property: JsonPropertyName("total_committed_bytes")] long TotalCommittedBytes,
    [property: JsonPropertyName("total_pause_duration_ms")] double? TotalPauseDurationMs,
    [property: JsonPropertyName("latest_gc_index")] long? LatestGcIndex,
    [property: JsonPropertyName("latest_gc_generation")] int? LatestGcGeneration,
    [property: JsonPropertyName("latest_gc_pause_duration_ms")] double? LatestGcPauseDurationMs);

public sealed record StallObservabilitySnapshot(
    [property: JsonPropertyName("event_capacity")] int EventCapacity,
    [property: JsonPropertyName("runtime_gc")] RuntimeGcMetrics RuntimeGc,
    [property: JsonPropertyName("latest_waveform_lifecycle_event")] StallEvent? LatestWaveformLifecycleEvent,
    [property: JsonPropertyName("significant_events")] IReadOnlyList<StallEvent> SignificantEvents);

public interface IRuntimeGcMetricsProvider
{
    RuntimeGcMetrics Capture();
}

public sealed class SystemRuntimeGcMetricsProvider : IRuntimeGcMetricsProvider
{
    public RuntimeGcMetrics Capture()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            var latestPause = info.PauseDurations.Length == 0
                ? null
                : (double?)info.PauseDurations.ToArray().Sum(item => item.TotalMilliseconds);
            return new(
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalMemory(false), info.HeapSizeBytes, info.TotalCommittedBytes,
                GC.GetTotalPauseDuration().TotalMilliseconds,
                info.Index > 0 ? info.Index : null,
                info.Index > 0 ? info.Generation : null,
                latestPause);
        }
        catch (PlatformNotSupportedException)
        {
            return new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalMemory(false), 0, 0, null, null, null, null);
        }
    }
}

public sealed class StallObservability
{
    public const int DefaultEventCapacity = 32;
    public const double SignificantTimingThresholdMs = 50;

    private readonly object _gate = new();
    private readonly Queue<StallEvent> _events;
    private readonly IObservabilityClock _clock;
    private readonly int _capacity;
    private readonly IRuntimeGcMetricsProvider _runtimeGc;
    private StallEvent? _latestWaveformLifecycleEvent;

    public StallObservability(IObservabilityClock clock, IRuntimeGcMetricsProvider? runtimeGc = null,
        int capacity = DefaultEventCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _clock = clock;
        _capacity = capacity;
        _events = new Queue<StallEvent>(capacity);
        _runtimeGc = runtimeGc ?? new SystemRuntimeGcMetricsProvider();
    }

    public IObservabilityClock Clock => _clock;

    public void Record(string category, string? sessionId = null, string? streamId = null,
        double? durationMs = null, ulong? sequence = null, string? reason = null)
    {
        var timestamp = _clock.Timestamp;
        var item = new StallEvent(_clock.ToMilliseconds(timestamp), _clock.UtcNow, category,
            sessionId, streamId, durationMs, sequence, BoundReason(reason));
        lock (_gate)
        {
            if (category.StartsWith("waveform_", StringComparison.Ordinal))
                _latestWaveformLifecycleEvent = item;
            if (_events.Count == _capacity) _events.Dequeue();
            _events.Enqueue(item);
        }
    }

    public void RecordWaveformStarted(string streamId, string sourceId) =>
        Record("waveform_stream_started", streamId: streamId, reason: sourceId);

    public void RecordWaveformStopped(string? streamId, string reason) =>
        Record("waveform_stream_stopped", streamId: streamId, reason: reason);

    public StallObservabilitySnapshot GetSnapshot()
    {
        StallEvent[] events;
        StallEvent? latestWaveform;
        lock (_gate)
        {
            events = _events.ToArray();
            latestWaveform = _latestWaveformLifecycleEvent;
        }
        return new(_capacity, _runtimeGc.Capture(), latestWaveform, events);
    }

    public static StallThresholdCounts IncrementThresholds(StallThresholdCounts counts, double durationMs) => new(
        counts.Over50Ms + (durationMs > 50 ? 1 : 0),
        counts.Over100Ms + (durationMs > 100 ? 1 : 0),
        counts.Over250Ms + (durationMs > 250 ? 1 : 0),
        counts.Over500Ms + (durationMs > 500 ? 1 : 0));

    private static string? BoundReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var value = reason.ReplaceLineEndings(" ").Trim();
        return value.Length <= 240 ? value : value[..237] + "...";
    }
}
