using Auraline.Contracts;
using InfoPanel.Auraline.Adapters;
using InfoPanel.Auraline.Core;

namespace InfoPanel.Auraline.Tests;

public sealed class ConfigurationAndPortabilityTests
{
    [Fact]
    public void PluginLifecycleAndDefaultsMatchCurrentInfoPanelContract()
    {
        var plugin = new AuralinePlugin();

        Assert.Equal(TimeSpan.FromMilliseconds(1000d / (30 * 2d)), plugin.UpdateInterval);
        Assert.Equal([AuralinePlugin.PrimaryImageId, AuralinePlugin.SecondaryImageId],
            plugin.ImageDescriptors.Select(descriptor => descriptor.Id).ToArray());
        Assert.Equal(AuralinePlugin.DefaultEndpoint,
            plugin.ConfigProperties.Single(property => property.Key == "HostEndpoint").Value);
        Assert.Equal(AuralineProfiles.DefaultProfileId,
            ProfileChoice.ParseProfileId(plugin.ConfigProperties.Single(property => property.Key == "Profile").Value?.ToString()));

        plugin.Initialize();
        plugin.ApplyConfig("TargetFps", "60");
        Assert.Equal(TimeSpan.FromMilliseconds(1000d / (60 * 2d)), plugin.UpdateInterval);
        plugin.Close();
    }

    [Fact]
    public void ProfileChoicePersistsStableIdAcrossFriendlyRename()
    {
        var original = new global::Auraline.Contracts.AuralineProfileSummary("stable-id", "Original", false, "waveform", "available");
        var renamed = original with { FriendlyName = "Renamed" };

        Assert.Equal("stable-id", ProfileChoice.ParseProfileId(ProfileChoice.Format(original)));
        Assert.Equal("stable-id", ProfileChoice.ParseProfileId(ProfileChoice.Format(renamed)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:48481", true)]
    [InlineData("http://127.0.0.1:50000", true)]
    [InlineData("http://localhost:48481", false)]
    [InlineData("https://127.0.0.1:48481", false)]
    [InlineData("http://192.168.1.10:48481", false)]
    public void HostEndpointRemainsNumericLoopbackOnly(string endpoint, bool valid)
    {
        if (valid)
            AuralinePluginRuntime.ValidateEndpoint(new Uri(endpoint));
        else
            Assert.Throws<ArgumentException>(() => AuralinePluginRuntime.ValidateEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void ReconnectBackoffIsBoundedAndResettable()
    {
        var backoff = new ReconnectBackoff();
        Assert.Equal(
            [500d, 1000d, 2000d, 5000d, 5000d],
            Enumerable.Range(0, 5).Select(_ => backoff.Next().TotalMilliseconds).ToArray());
        backoff.Reset();
        Assert.Equal(500, backoff.Next().TotalMilliseconds);
    }

    [Fact]
    public void SharedCoreDoesNotReferenceWindowsOrInfoPanelImplementationTypes()
    {
        var root = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var core = Path.Combine(root.FullName, "src", "InfoPanel.Auraline", "Core");
        var forbidden = new[]
        {
            "System.IO.MemoryMappedFiles",
            "MemoryMappedFile",
            "System.Windows",
            "System.Windows.Forms",
            "Microsoft.Win32",
            "InfoPanel.Plugins",
            "SkiaSharp"
        };

        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WindowsAndInfoPanelAdaptersRemainSeparate()
    {
        var root = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var windows = Path.Combine(root.FullName, "src", "InfoPanel.Auraline", "Platform", "Windows");
        var adapters = Path.Combine(root.FullName, "src", "InfoPanel.Auraline", "Adapters");
        Assert.DoesNotContain("InfoPanel.Plugins", ReadAll(windows), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MemoryMappedFile", ReadAll(adapters), StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAll(string directory) => string.Join('\n',
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo start)
    {
        var current = start;
        while (current.Parent is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
