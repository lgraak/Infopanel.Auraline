using System.Text.Json;
using Auraline.Host.Diagnostics;
using Auraline.Host.RenderSessions;

namespace Auraline.Host.Tests;

public sealed class StallObservabilityTests
{
    [Fact]
    public void MonotonicTimingMathSeparatesLatenessAndPublicationIntervals()
    {
        var clock = new FakeClock();

        Assert.Equal(0, RenderSessionManager.CalculateSchedulerLateness(clock, 100, 90));
        Assert.Equal(75, RenderSessionManager.CalculateSchedulerLateness(clock, 100, 175));
        Assert.Equal(225, RenderSessionManager.CalculatePublicationInterval(clock, 400, 625));
        Assert.Equal(133, clock.Add(100, TimeSpan.FromMilliseconds(33)));
    }

    [Fact]
    public void ThresholdCountersUseStrictBoundaries()
    {
        var counts = new StallThresholdCounts(0, 0, 0, 0);
        foreach (var value in new[] { 50d, 50.1, 100d, 100.1, 250d, 250.1, 500d, 500.1 })
            counts = StallObservability.IncrementThresholds(counts, value);

        Assert.Equal(new StallThresholdCounts(7, 5, 3, 1), counts);
    }

    [Fact]
    public void SignificantEventRetentionIsBoundedAndKeepsNewestEvents()
    {
        var clock = new FakeClock();
        var observability = new StallObservability(clock, new UnavailableGcMetrics(), capacity: 3);

        for (var index = 0; index < 100; index++)
        {
            clock.Advance(1);
            observability.Record("publication_interval", sessionId: "session", durationMs: index, sequence: (ulong)index);
        }

        var snapshot = observability.GetSnapshot();
        Assert.Equal(3, snapshot.EventCapacity);
        Assert.Equal([97UL, 98UL, 99UL], snapshot.SignificantEvents.Select(item => item.Sequence!.Value));
    }

    [Fact]
    public void WaveformLifecycleCapturesStreamAndStopReason()
    {
        var clock = new FakeClock();
        var observability = new StallObservability(clock, new UnavailableGcMetrics(), capacity: 4);

        observability.RecordWaveformStarted("stream-1", "opaque-source");
        clock.Advance(10);
        observability.RecordWaveformStopped("stream-1", "source_reconfigured");

        var snapshot = observability.GetSnapshot();
        Assert.Equal("waveform_stream_stopped", snapshot.LatestWaveformLifecycleEvent!.Category);
        Assert.Equal("stream-1", snapshot.LatestWaveformLifecycleEvent.StreamId);
        Assert.Equal("source_reconfigured", snapshot.LatestWaveformLifecycleEvent.Reason);
    }

    [Fact]
    public void RuntimeMetricsRemainSerializableWhenPauseEvidenceIsUnavailable()
    {
        var observability = new StallObservability(new FakeClock(), new UnavailableGcMetrics());

        var snapshot = observability.GetSnapshot();
        var json = JsonSerializer.Serialize(snapshot);

        Assert.Null(snapshot.RuntimeGc.TotalPauseDurationMs);
        Assert.Contains("\"total_pause_duration_ms\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"significant_events\":[]", json, StringComparison.Ordinal);
    }

    private sealed class FakeClock : IObservabilityClock
    {
        public long Timestamp { get; private set; }
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch.AddMilliseconds(Timestamp);
        public double ToMilliseconds(long timestamp) => timestamp;
        public double ElapsedMilliseconds(long startTimestamp, long endTimestamp) => endTimestamp - startTimestamp;
        public long Add(long timestamp, TimeSpan duration) => timestamp + (long)duration.TotalMilliseconds;
        public void Advance(long milliseconds) => Timestamp += milliseconds;
    }

    private sealed class UnavailableGcMetrics : IRuntimeGcMetricsProvider
    {
        public RuntimeGcMetrics Capture() => new(1, 2, 3, 4, 0, 0, null, null, null, null);
    }
}
