using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using Auraline.Contracts;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraline.Host.Tests;

public sealed class ConfigurationApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task GroupAndProfileCrudExposeDefaultsDuplicatesAndDependencyConflicts()
    {
        await using var fixture = await Fixture.StartAsync();
        var groupResponse = await fixture.Client.PostAsJsonAsync("/api/v1/source-groups", new CreateSourceGroupRequest(
            "Desk", [new SourceReference { ProviderId = HostConfiguration.DefaultProviderId, LogicalIntent = ProductDefaults.DefaultLogicalSourceIntent }]));
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = await groupResponse.Content.ReadFromJsonAsync<SourceGroupDefinition>();
        Assert.NotNull(group);

        var profileResponse = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", new CreateProfileRequest("Purple", group.Id));
        Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileDefinition>(JsonOptions);
        Assert.NotNull(profile);

        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.PostAsync($"/api/v1/profiles/{profile.Id}/set-default", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.DeleteAsync($"/api/v1/profiles/{profile.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.DeleteAsync($"/api/v1/source-groups/{group.Id}")).StatusCode);

        var duplicate = await fixture.Client.PostAsync($"/api/v1/profiles/{profile.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var duplicateProfile = await duplicate.Content.ReadFromJsonAsync<ProfileDefinition>(JsonOptions);
        Assert.NotEqual(profile.Id, duplicateProfile!.Id);
    }

    [Fact]
    public async Task WorkingCopyPreviewUsesRendererWithoutMutatingSavedProfile()
    {
        await using var fixture = await Fixture.StartAsync();
        var saved = fixture.Products.GetProfile(ProductDefaults.DefaultProfileId);
        var working = saved with { Waveform = saved.Waveform with { Color = "#FF0000", ScaleMode = WaveformScaleMode.Fixed, FixedScale = 2 } };

        var response = await fixture.Client.PostAsJsonAsync("/api/v1/profile-preview/render.png", working);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 100);
        Assert.Equal(saved, fixture.Products.GetProfile(saved.Id));
    }

    [Fact]
    public async Task ProviderCrudKeepsStableIdsAndBlocksReferencedProviderDeletion()
    {
        await using var fixture = await Fixture.StartAsync();
        var provider = new ProviderConfiguration { Id = "secondary", FriendlyName = "Secondary", Endpoint = "http://127.0.0.1:48482", Enabled = false };

        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/providers", provider)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync("/api/v1/providers/secondary", provider with { FriendlyName = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.DeleteAsync("/api/v1/providers/secondary")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.DeleteAsync($"/api/v1/providers/{HostConfiguration.DefaultProviderId}")).StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly WebApplication _app;
        private readonly RenderSessionManager _sessions;
        private readonly ProviderManager _providers;

        private Fixture(string root, WebApplication app, HttpClient client, ProductConfigurationStore products, RenderSessionManager sessions, ProviderManager providers)
        {
            _root = root; _app = app; Client = client; Products = products; _sessions = sessions; _providers = providers;
        }

        public HttpClient Client { get; }
        public ProductConfigurationStore Products { get; }

        public static async Task<Fixture> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "AuralineConfigurationApiTests", Guid.NewGuid().ToString("N"));
            var paths = AuralinePaths.FromRoot(root);
            var configuration = new ConfigurationStore(paths);
            await configuration.LoadAsync();
            var products = new ProductConfigurationStore(paths);
            await products.LoadAsync();
            var waveform = new FakeWaveformSource();
            var renderer = new WaveformRenderer();
            var providers = new ProviderManager(configuration, new OfflineConnector(), new BlockingDelay(), NullLogger<ProviderManager>.Instance, products);
            var sessions = new RenderSessionManager(new FakeTransportFactory(), waveform, renderer, new Clock(), RenderSessionOptions.Default, NullLogger<RenderSessionManager>.Instance, products);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower)));
            builder.Services.AddSingleton(products);
            builder.Services.AddSingleton(providers);
            builder.Services.AddSingleton(sessions);
            builder.Services.AddSingleton<IWaveformRenderStateSource>(waveform);
            builder.Services.AddSingleton(renderer);
            var app = builder.Build();
            app.MapConfigurationEndpoints();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new Fixture(root, app, new HttpClient { BaseAddress = new Uri(address) }, products, sessions, providers);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _sessions.DisposeAsync();
            _providers.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private sealed class FakeWaveformSource : IWaveformRenderStateSource
    {
        public WaveformRenderSnapshot CaptureRenderState() => new(new WaveformProcessedFrame("test", 1, 1, 1, [0.4f, -0.4f], [[0.4f, -0.4f]]), WaveformVisualizationState.Active);
    }

    private sealed class Clock : IRenderSessionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }

    private sealed class OfflineConnector : IProviderConnector
    {
        public Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken) => Task.FromException<ProviderConnectionResult>(new HttpRequestException("offline"));
    }

    private sealed class BlockingDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakeTransportFactory : IAuralineFrameTransportFactory
    {
        public IAuralineFrameTransport Create(int width, int height, int targetFps) => new FakeTransport();
        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) => throw new NotSupportedException();
        private sealed class FakeTransport : IAuralineFrameTransport
        {
            public FrameTransportDescriptor Descriptor { get; } = new("fake", ContractVersion.Current, Guid.NewGuid().ToString("N"), 1024, 128, 2, "rgba8888-premul");
            public void Publish(FramePublication frame) { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
