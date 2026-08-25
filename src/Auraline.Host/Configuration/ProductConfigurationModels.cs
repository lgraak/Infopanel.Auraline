using System.Text.Json.Serialization;

namespace Auraline.Host.Configuration;

public static class ProductDefaults
{
    public const string DefaultSourceGroupId = "default-source-group";
    public const string DefaultProfileId = "default-profile";
    public const string DefaultLogicalSourceIntent = "default-playback";
}

public enum SourceMemberResolution
{
    Resolved,
    Stale,
    Unresolved,
    Ambiguous
}

public sealed record SourceReference
{
    public required string ProviderId { get; init; }
    public string? SourceId { get; init; }
    public string? LogicalIntent { get; init; }
    public string? LastKnownDisplayName { get; init; }
    public string? LastKnownKind { get; init; }
    public double Gain { get; init; } = 1.0;
    public bool Active { get; init; } = true;
}

public sealed record SourceGroupDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public List<SourceReference> Members { get; init; } = [];
}

public enum WaveformScaleMode
{
    Automatic,
    Fixed
}

public sealed record WaveformProfileSettings
{
    public string Style { get; init; } = "centered-line";
    public string Color { get; init; } = "#76B9FF";
    public WaveformScaleMode ScaleMode { get; init; } = WaveformScaleMode.Automatic;
    public double FixedScale { get; init; } = 1.0;
    public bool SmoothingEnabled { get; init; } = true;
    public double SmoothingAmount { get; init; }
    public int TargetFps { get; init; } = 30;
    public string Background { get; init; } = "transparent";
}

public sealed record ProfileDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public string VisualizationType { get; init; } = "waveform";
    public required string SourceGroupId { get; init; }
    public long Revision { get; init; } = 1;
    public WaveformProfileSettings Waveform { get; init; } = new();
}

public sealed record LastKnownSource
{
    public required string ProviderId { get; init; }
    public required string SourceId { get; init; }
    public string? DisplayName { get; init; }
    public required string Kind { get; init; }
    public required string Availability { get; init; }
    public bool DefaultPlayback { get; init; }
    public List<string> SupportedProducts { get; init; } = [];
    public int? ChannelCount { get; init; }
    public int? SampleRateHz { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ProductCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string DefaultSourceGroupId { get; init; } = ProductDefaults.DefaultSourceGroupId;
    public string DefaultProfileId { get; init; } = ProductDefaults.DefaultProfileId;
}

public sealed record SourceCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset? RefreshedAtUtc { get; init; }
    public List<LastKnownSource> Sources { get; init; } = [];
}

public sealed record SourceMemberStatus(SourceReference Member, SourceMemberResolution Resolution, LastKnownSource? Source, string Reason);

public sealed record SourceGroupStatus(SourceGroupDefinition Group, IReadOnlyList<SourceMemberStatus> Members)
{
    [JsonIgnore]
    public int UsableMemberCount => Members.Count(item => item.Resolution is SourceMemberResolution.Resolved or SourceMemberResolution.Stale);

    [JsonIgnore]
    public string Availability => UsableMemberCount == 0 ? "unavailable" : Members.Any(item => item.Resolution != SourceMemberResolution.Resolved) ? "degraded" : "available";
}

public interface IProfileCatalog
{
    IReadOnlyList<ProfileDefinition> GetProfiles();
    ProfileDefinition GetProfile(string profileId);
}
