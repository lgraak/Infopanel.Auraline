using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Auraline.Host.Configuration;

namespace Auraline.Host.Providers;

public interface IProviderConnector
{
    Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken);
}

public sealed class ResonanceSignalClient(IHttpClientFactory httpClientFactory) : IProviderConnector
{
    public async Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("resonance-signal");
        client.BaseAddress = new Uri(provider.Endpoint.TrimEnd('/') + "/", UriKind.Absolute);

        using var statusResponse = await client.GetAsync("v1/status", cancellationToken);
        statusResponse.EnsureSuccessStatusCode();
        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: cancellationToken)
                     ?? throw new InvalidDataException("Provider returned an empty status response.");
        EnsureProtocol(status.ProtocolVersion);
        if (!string.Equals(status.Status, "ready", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException($"Provider reported status '{status.Status}'.");

        using var discoveryResponse = await client.GetAsync("v1/sources", cancellationToken);
        discoveryResponse.EnsureSuccessStatusCode();
        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<SourcesResponse>(cancellationToken: cancellationToken)
                        ?? throw new InvalidDataException("Provider returned an empty discovery response.");
        EnsureProtocol(discovery.ProtocolVersion);
        if (string.IsNullOrWhiteSpace(discovery.Revision)) throw new InvalidDataException("Provider discovery revision was empty.");

        var sources = discovery.Sources.Select(source => new ProviderSource(
            provider.Id,
            source.SourceId,
            source.DisplayName,
            source.Kind,
            source.Availability,
            source.DefaultPlayback,
            source.SupportedProducts)).ToArray();
        return new(discovery.Revision, sources);
    }

    private static void EnsureProtocol(int version)
    {
        if (version != 1) throw new ProviderCompatibilityException($"Unsupported Resonance Signal protocol version {version}; expected version 1.");
    }

    private sealed record StatusResponse(
        [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
        [property: JsonPropertyName("status")] string Status);

    private sealed record SourcesResponse(
        [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("sources")] SourceResponse[] Sources);

    private sealed record SourceResponse(
        [property: JsonPropertyName("source_id")] string SourceId,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("availability")] string Availability,
        [property: JsonPropertyName("default_playback")] bool DefaultPlayback,
        [property: JsonPropertyName("supported_products")] string[] SupportedProducts);
}
