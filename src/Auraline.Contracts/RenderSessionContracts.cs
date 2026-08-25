using System.Text.Json.Serialization;

namespace Auraline.Contracts;

public static class AuralineProfiles
{
    public const string DefaultProfileId = "default-profile";
}

public sealed record AuralineProfileSummary(
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("friendly_name")] string FriendlyName,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("visualization_type")] string VisualizationType,
    [property: JsonPropertyName("status")] string Status);

public sealed record AuralineProfileCatalog(
    [property: JsonPropertyName("contract_version")] ContractVersion ContractVersion,
    [property: JsonPropertyName("host_version")] string HostVersion,
    [property: JsonPropertyName("profiles")] IReadOnlyList<AuralineProfileSummary> Profiles);

public readonly record struct RenderSessionKey(
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height);

public enum RenderSessionState
{
    Active,
    Grace,
    Stopped
}

public sealed record FrameTransportDescriptor(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("layout_version")] ContractVersion LayoutVersion,
    [property: JsonPropertyName("resource_name")] string ResourceName,
    [property: JsonPropertyName("allocation_size")] long AllocationSize,
    [property: JsonPropertyName("header_size")] int HeaderSize,
    [property: JsonPropertyName("slot_count")] int SlotCount,
    [property: JsonPropertyName("pixel_format")] string PixelFormat);

public sealed record RenderSessionDescriptor(
    [property: JsonPropertyName("contract_version")] ContractVersion ContractVersion,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("target_fps")] int TargetFps,
    [property: JsonPropertyName("transport")] FrameTransportDescriptor Transport);

public sealed record ConsumerLease(
    [property: JsonPropertyName("lease_id")] string LeaseId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc);

public sealed record RenderSessionAttachment(
    [property: JsonPropertyName("session")] RenderSessionDescriptor Session,
    [property: JsonPropertyName("lease")] ConsumerLease Lease);

public sealed record FramePublication(
    int Width,
    int Height,
    int Stride,
    string PixelFormat,
    bool Premultiplied,
    ulong Sequence,
    long TimestampUtcTicks,
    int TargetFps,
    ReadOnlyMemory<byte> Pixels);

public sealed record FrameReadResult(
    int Width,
    int Height,
    int Stride,
    string PixelFormat,
    bool Premultiplied,
    ulong Sequence,
    long TimestampUtcTicks,
    int TargetFps,
    byte[] Pixels);

public interface IAuralineFrameTransport : IAsyncDisposable
{
    FrameTransportDescriptor Descriptor { get; }

    void Publish(FramePublication frame);
}

public interface IAuralineFrameReader : IDisposable
{
    FrameTransportDescriptor Descriptor { get; }

    bool TryReadLatest(out FrameReadResult? frame);
}

public interface IAuralineFrameTransportFactory
{
    IAuralineFrameTransport Create(int width, int height, int targetFps);

    IAuralineFrameReader Open(FrameTransportDescriptor descriptor);
}
