namespace Auraline.Host.Providers;

public enum ProviderLifecycleState
{
    Disabled,
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed record ProviderSource(
    string ProviderId,
    string SourceId,
    string? DisplayName,
    string Kind,
    string Availability,
    bool DefaultPlayback,
    IReadOnlyList<string> SupportedProducts,
    int? ChannelCount = null,
    int? SampleRateHz = null);

public sealed record ProviderStatus(
    string Id,
    string FriendlyName,
    string Endpoint,
    bool Enabled,
    ProviderLifecycleState State,
    string? LastError,
    DateTimeOffset? LastConnectedAt,
    string? DiscoveryRevision,
    IReadOnlyList<ProviderSource> Sources,
    long ReconnectCount = 0,
    double? RetryDelayMs = null);

public sealed record ProviderConnectionResult(string DiscoveryRevision, IReadOnlyList<ProviderSource> Sources);

public sealed class ProviderCompatibilityException(string message) : Exception(message);
