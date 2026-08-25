using System.Net;
using System.Text;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;

namespace Auraline.Host.Tests;

public sealed class ResonanceSignalClientTests
{
    [Fact]
    public async Task UsesStatusAndDiscoveryEndpointsWithoutWaveformProbe()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/status" => Json("{\"protocol_version\":1,\"status\":\"ready\",\"listener_scope\":\"loopback\",\"active_stream_sessions\":0}"),
            "/v1/sources" => Json("{\"protocol_version\":1,\"revision\":\"opaque-revision\",\"sources\":[{\"source_id\":\"opaque-source\",\"display_name\":\"Speakers\",\"kind\":\"playback\",\"availability\":\"available\",\"default_playback\":true,\"supported_products\":[\"waveform\"]}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = new ResonanceSignalClient(new StubFactory(handler));

        var result = await client.ConnectAndDiscoverAsync(HostConfiguration.CreateDefault().Providers.Single(), default);

        Assert.Equal(["/v1/status", "/v1/sources"], handler.Paths);
        var source = Assert.Single(result.Sources);
        Assert.Equal("opaque-source", source.SourceId);
        Assert.Null(source.ChannelCount);
        Assert.Null(source.SampleRateHz);
    }

    [Fact]
    public async Task UnsupportedProtocolIsReportedAsCompatibilityFailure()
    {
        var handler = new StubHandler(_ => Json("{\"protocol_version\":2,\"status\":\"ready\"}"));
        var client = new ResonanceSignalClient(new StubFactory(handler));

        var error = await Assert.ThrowsAsync<ProviderCompatibilityException>(() =>
            client.ConnectAndDiscoverAsync(HostConfiguration.CreateDefault().Providers.Single(), default));

        Assert.Contains("protocol version 2", error.Message);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(responder(request));
        }
    }
}
