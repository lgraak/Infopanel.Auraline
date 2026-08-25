using System.Text.Json.Serialization;

namespace Auraline.Host.Waveform;

public enum WaveformRetryHint
{
    RetryNow,
    RetryLater,
    WaitForSource,
    RequestPermission,
    ChangeFormat,
    DoNotRetry,
    Unknown
}

public enum WaveformVisualizationState
{
    Active,
    Idle,
    Reconnecting,
    Unavailable,
    Degraded
}

public sealed record WaveformBinaryFrame(
    ulong Sequence,
    ulong FrameIndex,
    ulong StreamTimeNs,
    uint FrameCount,
    int ChannelCount,
    float[] Samples);

public sealed record WaveformStreamStartedEvent(
    string StreamId,
    string SourceId,
    string SourceKind,
    int SampleRateHz,
    int ChannelCount,
    string[] ChannelOrder,
    string SampleFormat,
    long WindowDurationNs);

public sealed record WaveformStreamStoppedEvent(
    string StreamId,
    string Reason);

public sealed record WaveformStreamErrorEvent(
    string Kind,
    string ScopeType,
    string? ScopeId,
    WaveformRetryHint RetryHint);

public sealed record WaveformProcessedFrame(
    string StreamId,
    ulong Sequence,
    ulong FrameIndex,
    ulong StreamTimeNs,
    float[] MonoSamples,
    float[][] ChannelSamples);

public sealed record WaveformEngineHealth(
    [property: JsonPropertyName("visual_state")] string VisualState,
    [property: JsonPropertyName("logical_source_intent")] string LogicalSourceIntent,
    [property: JsonPropertyName("provider_id")] string? ProviderId,
    [property: JsonPropertyName("stream_id")] string? StreamId,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("channel_count")] int? ChannelCount,
    [property: JsonPropertyName("sample_rate_hz")] int? SampleRateHz,
    [property: JsonPropertyName("sample_format")] string? SampleFormat,
    [property: JsonPropertyName("reconnect_attempts")] long ReconnectAttempts,
    [property: JsonPropertyName("stream_starts")] long StreamStarts,
    [property: JsonPropertyName("stream_stops")] long StreamStops,
    [property: JsonPropertyName("malformed_frames")] long MalformedFrames,
    [property: JsonPropertyName("waveform_frames")] long WaveformFrames,
    [property: JsonPropertyName("latest_frame_age_ms")] double? LatestFrameAgeMs,
    [property: JsonPropertyName("active_stream_uptime_ms")] double? ActiveStreamUptimeMs,
    [property: JsonPropertyName("last_render_duration_ms")] double? LastRenderDurationMs,
    [property: JsonPropertyName("average_render_duration_ms")] double? AverageRenderDurationMs,
    [property: JsonPropertyName("target_fps")] int TargetFps,
    [property: JsonPropertyName("retry_state")] string RetryState,
    [property: JsonPropertyName("rendered_frames")] long RenderedFrames);

public sealed record WaveformRenderedFrame(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("pixel_format")] string PixelFormat,
    [property: JsonPropertyName("stride")] int Stride,
    [property: JsonPropertyName("sequence")] ulong Sequence,
    [property: JsonPropertyName("timestamp")] long TimestampTicks,
    [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
    [property: JsonPropertyName("premultiplied")] bool Premultiplied,
    [property: JsonPropertyName("visual_state")] string VisualState,
    [property: JsonPropertyName("target_fps")] int TargetFps,
    [property: JsonPropertyName("pixels")] byte[] Pixels);

public interface IWaveformEngineStatusProvider
{
    WaveformEngineHealth GetHealth();
    WaveformRenderedFrame? GetLatestFrame();
}
