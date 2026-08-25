using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraline.Contracts;

namespace InfoPanel.Auraline.Core;

internal interface IAuralineHostClient : IDisposable
{
    Task<AuralineProfileCatalog> GetProfilesAsync(CancellationToken cancellationToken);

    Task<RenderSessionAttachment> AttachAsync(
        string profileId,
        int width,
        int height,
        int targetFps,
        CancellationToken cancellationToken);

    Task<ConsumerLease?> HeartbeatAsync(
        string sessionId,
        string leaseId,
        CancellationToken cancellationToken);

    Task DetachAsync(string sessionId, string leaseId, CancellationToken cancellationToken);
}

internal sealed class AuralineHostClient : IAuralineHostClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public AuralineHostClient(Uri endpoint)
        : this(new HttpClient
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromSeconds(3)
        }, true)
    {
    }

    internal AuralineHostClient(HttpClient client, bool ownsClient = false)
    {
        _client = client;
        _ownsClient = ownsClient;
    }

    public async Task<AuralineProfileCatalog> GetProfilesAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync("/api/v1/profiles", cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<AuralineProfileCatalog>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RenderSessionAttachment> AttachAsync(
        string profileId,
        int width,
        int height,
        int targetFps,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/render-sessions/attach", new
        {
            contract_major = ContractVersion.Current.Major,
            contract_minor = ContractVersion.Current.Minor,
            profile_id = profileId,
            width,
            height,
            target_fps = targetFps
        }, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredAsync<RenderSessionAttachment>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsumerLease?> HeartbeatAsync(
        string sessionId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(
            $"/api/v1/render-sessions/{Uri.EscapeDataString(sessionId)}/leases/{Uri.EscapeDataString(leaseId)}/heartbeat",
            null,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadRequiredAsync<ConsumerLease>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DetachAsync(string sessionId, string leaseId, CancellationToken cancellationToken)
    {
        using var response = await _client.DeleteAsync(
            $"/api/v1/render-sessions/{Uri.EscapeDataString(sessionId)}/leases/{Uri.EscapeDataString(leaseId)}",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidDataException("Auraline Host returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Auraline Host returned an invalid response.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)) message = error.GetString();
        }
        catch (JsonException)
        {
        }

        throw new AuralineHostException(response.StatusCode,
            string.IsNullOrWhiteSpace(message)
                ? $"Auraline Host request failed with HTTP {(int)response.StatusCode}."
                : message);
    }
}

internal sealed class AuralineHostException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
