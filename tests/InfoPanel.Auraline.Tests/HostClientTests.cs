using System.Net;
using System.Text;
using System.Text.Json;
using Auraline.Contracts;
using InfoPanel.Auraline.Core;

namespace InfoPanel.Auraline.Tests;

public sealed class HostClientTests
{
    [Fact]
    public async Task ProfilesAttachHeartbeatAndDetachUseVersionedContracts()
    {
        var requests = new List<(HttpMethod Method, string Path, string? Body)>();
        var handler = new StubHandler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return request.RequestUri.AbsolutePath switch
            {
                "/api/v1/profiles" => Json(new AuralineProfileCatalog(
                    ContractVersion.Current,
                    "1.0.0-m4",
                    [new(AuralineProfiles.DefaultProfileId, "Default Waveform", true, "waveform", "available")])),
                "/api/v1/render-sessions/attach" => Json(Attachment("session-1", "lease-1", 320, 120), HttpStatusCode.Created),
                var path when path.EndsWith("/heartbeat", StringComparison.Ordinal) =>
                    Json(new ConsumerLease("lease-1", "session-1", DateTimeOffset.UtcNow.AddSeconds(25))),
                _ when request.Method == HttpMethod.Delete => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AuralinePlugin.DefaultEndpoint) };
        using var client = new AuralineHostClient(http);

        var profiles = await client.GetProfilesAsync(CancellationToken.None);
        var attachment = await client.AttachAsync(AuralineProfiles.DefaultProfileId, 320, 120, 30, CancellationToken.None);
        var lease = await client.HeartbeatAsync("session-1", "lease-1", CancellationToken.None);
        await client.DetachAsync("session-1", "lease-1", CancellationToken.None);

        Assert.Equal("1.0.0-m4", profiles.HostVersion);
        Assert.Equal("session-1", attachment.Session.SessionId);
        Assert.Equal("lease-1", lease!.LeaseId);
        using var attachJson = JsonDocument.Parse(requests.Single(item => item.Path.EndsWith("/attach", StringComparison.Ordinal)).Body!);
        Assert.Equal(ContractVersion.Current.Major, attachJson.RootElement.GetProperty("contract_major").GetInt32());
        Assert.Contains(requests, item => item.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task UnavailableInvalidAndIncompatibleResponsesFailClearly()
    {
        using var unavailableHttp = new HttpClient(new StubHandler(
            (Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException("offline"))))
        {
            BaseAddress = new Uri(AuralinePlugin.DefaultEndpoint)
        };
        using var unavailable = new AuralineHostClient(unavailableHttp);
        await Assert.ThrowsAsync<HttpRequestException>(() => unavailable.GetProfilesAsync(CancellationToken.None));

        using var invalidHttp = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }))
        {
            BaseAddress = new Uri(AuralinePlugin.DefaultEndpoint)
        };
        using var invalid = new AuralineHostClient(invalidHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => invalid.GetProfilesAsync(CancellationToken.None));

        using var incompatibleHttp = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UpgradeRequired)
            {
                Content = new StringContent("{\"error\":\"unsupported major\"}", Encoding.UTF8, "application/json")
            }))
        {
            BaseAddress = new Uri(AuralinePlugin.DefaultEndpoint)
        };
        using var incompatible = new AuralineHostClient(incompatibleHttp);
        var error = await Assert.ThrowsAsync<AuralineHostException>(() =>
            incompatible.AttachAsync("profile", 320, 120, 30, CancellationToken.None));
        Assert.Equal(HttpStatusCode.UpgradeRequired, error.StatusCode);
        Assert.Equal("unsupported major", error.Message);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

    internal static RenderSessionAttachment Attachment(string sessionId, string leaseId, int width, int height, int fps = 30) =>
        new(
            new RenderSessionDescriptor(
                ContractVersion.Current,
                sessionId,
                AuralineProfiles.DefaultProfileId,
                width,
                height,
                fps,
                new FrameTransportDescriptor(
                    "windows-shared-memory",
                    ContractVersion.Current,
                    sessionId,
                    128L + width * height * 8L,
                    128,
                    2,
                    "rgba8888-premul")),
            new ConsumerLease(leaseId, sessionId, DateTimeOffset.UtcNow.AddSeconds(25)));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
