using Auraline.Contracts;

namespace InfoPanel.Auraline.Core;

internal enum PluginConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Incompatible,
    Unavailable
}

internal sealed record ImageConsumerDemand(
    string ImageId,
    string ConsumerId,
    int Width,
    int Height);

internal sealed record OutputDiagnostics(
    string ImageId,
    string? SessionId,
    int? Width,
    int? Height,
    int TargetFps,
    ulong LatestSequence,
    DateTimeOffset? LatestFrameUtc);

internal sealed record PluginRuntimeDiagnostics(
    string PluginVersion,
    string HostEndpoint,
    string? HostVersion,
    string SelectedProfileId,
    string? SelectedProfileName,
    PluginConnectionState State,
    long ReconnectCount,
    string? LastError,
    IReadOnlyList<OutputDiagnostics> Outputs);

internal interface IPluginFrameSink
{
    string ImageId { get; }

    int Width { get; }

    int Height { get; }

    void Publish(FrameReadResult frame);

    void PublishUnavailable(string message);
}

internal interface IPluginFrameReaderFactory
{
    IAuralineFrameReader Open(FrameTransportDescriptor descriptor);
}

internal interface IPluginRuntimeClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemPluginRuntimeClock : IPluginRuntimeClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
