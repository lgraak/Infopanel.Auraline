using System.Text;
using Auraline.Host.Configuration;

namespace Auraline.Host.Tests;

public sealed class ConfigurationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuralineTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MissingConfigurationBootstrapsReadableDefaults()
    {
        var paths = AuralinePaths.FromRoot(_root);
        var store = new ConfigurationStore(paths);

        var result = await store.LoadAsync();

        Assert.True(result.Created);
        var provider = Assert.Single(result.Configuration.Providers);
        Assert.Equal("local-resonance-signal", provider.Id);
        Assert.Equal("Local Resonance Signal", provider.FriendlyName);
        Assert.Equal("http://127.0.0.1:48480", provider.Endpoint);
        Assert.True(provider.Enabled);
        var text = await File.ReadAllTextAsync(paths.ConfigFile);
        Assert.Contains("\"schema_version\": 1", text);
        Assert.Contains("\"first_run_completed\": false", text);
    }

    [Fact]
    public async Task ConfigurationRoundTripsValidatedUpdates()
    {
        var paths = AuralinePaths.FromRoot(_root);
        var store = new ConfigurationStore(paths);
        await store.LoadAsync();
        await store.UpdateAsync(current => current with { Host = current.Host with { Port = 49001, Theme = "dark", FirstRunCompleted = true } });

        var reloaded = new ConfigurationStore(paths);
        var result = await reloaded.LoadAsync();

        Assert.False(result.Created);
        Assert.Equal(49001, result.Configuration.Host.Port);
        Assert.Equal("dark", result.Configuration.Host.Theme);
        Assert.True(result.Configuration.Host.FirstRunCompleted);
    }

    [Fact]
    public async Task MalformedConfigurationIsPreservedAndNotOverwritten()
    {
        var paths = AuralinePaths.FromRoot(_root);
        paths.EnsureDirectories();
        var malformed = Encoding.UTF8.GetBytes("{ this is not valid json");
        await File.WriteAllBytesAsync(paths.ConfigFile, malformed);
        var store = new ConfigurationStore(paths);

        var result = await store.LoadAsync();

        Assert.False(result.CanPersist);
        Assert.NotNull(result.Error);
        Assert.Equal(malformed, await File.ReadAllBytesAsync(paths.ConfigFile));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(current => current));
    }

    [Fact]
    public void ValidationRejectsExternalProviderEndpointsAndDuplicateIds()
    {
        var configuration = HostConfiguration.CreateDefault() with
        {
            Providers =
            [
                new() { Id = "same", FriendlyName = "One", Endpoint = "http://127.0.0.1:48480" },
                new() { Id = "same", FriendlyName = "Two", Endpoint = "http://192.168.1.20:48480" }
            ]
        };

        var errors = ConfigurationValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("Duplicate provider ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("numeric loopback", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
