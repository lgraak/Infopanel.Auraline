namespace Auraline.Host.Configuration;

public sealed record HostConfiguration
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultPort = 48481;
    public const string DefaultProviderId = "local-resonance-signal";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public HostSettings Host { get; init; } = new();
    public List<ProviderConfiguration> Providers { get; init; } = [];

    public static HostConfiguration CreateDefault() => new()
    {
        Providers =
        [
            new ProviderConfiguration
            {
                Id = DefaultProviderId,
                FriendlyName = "Local Resonance Signal",
                Endpoint = "http://127.0.0.1:48480",
                Enabled = true
            }
        ]
    };
}

public sealed record HostSettings
{
    public int Port { get; init; } = HostConfiguration.DefaultPort;
    public bool FirstRunCompleted { get; init; }
    public bool StartWithWindows { get; init; }
    public string Theme { get; init; } = "system";
}

public sealed record ProviderConfiguration
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required string Endpoint { get; init; }
    public bool Enabled { get; init; } = true;
}
