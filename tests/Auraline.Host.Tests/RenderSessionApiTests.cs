using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraline.Contracts;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraline.Host.Tests;

public sealed class RenderSessionApiTests
{
    [Fact]
    public async Task ProfileCatalogExposesStableDefaultProfileAndContract()
    {
        await using var fixture = await ApiFixture.StartAsync(cap: 2);

        var catalog = await fixture.Client.GetFromJsonAsync<AuralineProfileCatalog>("/api/v1/profiles");

        Assert.NotNull(catalog);
        Assert.Equal(ContractVersion.Current, catalog.ContractVersion);
        Assert.Equal("1.0.0-m5", catalog.HostVersion);
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal(AuralineProfiles.DefaultProfileId, profile.ProfileId);
        Assert.Equal("Default Waveform", profile.FriendlyName);
        Assert.True(profile.IsDefault);
        Assert.Equal("waveform", profile.VisualizationType);
    }

    [Fact]
    public async Task AttachHeartbeatDiagnosticsAndDetachUseVersionedLoopbackApi()
    {
        await using var fixture = await ApiFixture.StartAsync(cap: 2);
        var attach = await fixture.Client.PostAsJsonAsync("/api/v1/render-sessions/attach", Request(320, 120));
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);
        var first = await attach.Content.ReadFromJsonAsync<RenderSessionAttachment>();
        Assert.NotNull(first);

        var sharedResponse = await fixture.Client.PostAsJsonAsync("/api/v1/render-sessions/attach", Request(320, 120));
        var shared = await sharedResponse.Content.ReadFromJsonAsync<RenderSessionAttachment>();
        Assert.Equal(first.Session.SessionId, shared!.Session.SessionId);
        Assert.NotEqual(first.Lease.LeaseId, shared.Lease.LeaseId);

        var heartbeat = await fixture.Client.PostAsync(
            $"/api/v1/render-sessions/{first.Session.SessionId}/leases/{first.Lease.LeaseId}/heartbeat", null);
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        var diagnostics = await fixture.Client.GetFromJsonAsync<RenderSessionDiagnostics>("/api/v1/render-sessions");
        Assert.Equal(1, diagnostics!.ActiveSessionCount);
        Assert.Equal(2, diagnostics.TotalConsumerLeases);

        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.DeleteAsync(
            $"/api/v1/render-sessions/{first.Session.SessionId}/leases/{first.Lease.LeaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.DeleteAsync(
            $"/api/v1/render-sessions/{first.Session.SessionId}/leases/{first.Lease.LeaseId}")).StatusCode);
    }

    [Fact]
    public async Task ApiSurfacesValidationCompatibilityAndCapacityFailuresClearly()
    {
        await using var fixture = await ApiFixture.StartAsync(cap: 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PostAsJsonAsync(
            "/api/v1/render-sessions/attach", Request(8, 120))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client.PostAsJsonAsync(
            "/api/v1/render-sessions/attach", Request(320, 120) with { ProfileId = "missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await fixture.Client.PostAsJsonAsync(
            "/api/v1/render-sessions/attach", Request(320, 120) with { ContractMajor = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync(
            "/api/v1/render-sessions/attach", Request(320, 120))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PostAsJsonAsync(
            "/api/v1/render-sessions/attach", Request(640, 240))).StatusCode);
    }

    private static AttachRenderSessionRequest Request(int width, int height) =>
        new(ContractVersion.Current.Major, ContractVersion.Current.Minor, AuralineProfiles.DefaultProfileId, width, height, 30);

    private sealed class ApiFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly RenderSessionManager _manager;
        private readonly ProviderManager _providers;
        private readonly string _root;

        private ApiFixture(WebApplication app, RenderSessionManager manager, ProviderManager providers, string root, HttpClient client)
        {
            _app = app;
            _manager = manager;
            _providers = providers;
            _root = root;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ApiFixture> StartAsync(int cap)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var root = Path.Combine(Path.GetTempPath(), "AuralineApiTests", Guid.NewGuid().ToString("N"));
            var paths = AuralinePaths.FromRoot(root);
            var configuration = new ConfigurationStore(paths);
            await configuration.LoadAsync();
            var products = new ProductConfigurationStore(paths);
            await products.LoadAsync();
            var providers = new ProviderManager(configuration, new OfflineConnector(), new BlockingDelay(), NullLogger<ProviderManager>.Instance, products);
            var manager = new RenderSessionManager(
                new FakeTransportFactory(),
                new FakeWaveformSource(),
                new WaveformRenderer(),
                new Clock(),
                new RenderSessionOptions(cap, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1)),
                NullLogger<RenderSessionManager>.Instance,
                products);
            builder.Services.AddSingleton(manager);
            builder.Services.AddSingleton(products);
            builder.Services.AddSingleton(providers);
            var app = builder.Build();
            app.MapRenderSessionEndpoints();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var address = addresses!.Addresses.Single();
            return new ApiFixture(app, manager, providers, root, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _manager.DisposeAsync();
            _providers.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class OfflineConnector : IProviderConnector
    {
        public Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken) =>
            Task.FromException<ProviderConnectionResult>(new HttpRequestException("offline"));
    }

    private sealed class BlockingDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class Clock : IRenderSessionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }

    private sealed class FakeWaveformSource : IWaveformRenderStateSource
    {
        public WaveformRenderSnapshot CaptureRenderState() => new(
            new WaveformProcessedFrame("test", 1, 1, 1, [0.5f, -0.5f], [[0.5f, -0.5f]]),
            WaveformVisualizationState.Active);
    }

    private sealed class FakeTransportFactory : IAuralineFrameTransportFactory
    {
        public IAuralineFrameTransport Create(int width, int height, int targetFps) => new FakeTransport(width, height);
        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) => throw new NotSupportedException();

        private sealed class FakeTransport(int width, int height) : IAuralineFrameTransport
        {
            public FrameTransportDescriptor Descriptor { get; } = new("fake", ContractVersion.Current, Guid.NewGuid().ToString("N"), 128L + width * height * 8L, 128, 2, "rgba8888-premul");
            public void Publish(FramePublication frame) { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
