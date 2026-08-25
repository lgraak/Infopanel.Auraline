using System.Text;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;

namespace Auraline.Host.Tests;

public sealed class ProductConfigurationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuralineProductTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FirstRunBootstrapsM4CompatiblePersistentDefaultsAndReloadsThem()
    {
        var paths = AuralinePaths.FromRoot(_root);
        var store = new ProductConfigurationStore(paths);

        var load = await store.LoadAsync();

        Assert.True(load.Created);
        Assert.Equal(ProductDefaults.DefaultProfileId, store.Catalog.DefaultProfileId);
        var profile = Assert.Single(store.GetProfiles());
        Assert.Equal("default-profile", profile.Id);
        Assert.Equal(ProductDefaults.DefaultSourceGroupId, profile.SourceGroupId);
        var member = Assert.Single(Assert.Single(store.GetGroups()).Members);
        Assert.Equal("default-playback", member.LogicalIntent);
        Assert.True(File.Exists(Path.Combine(paths.ProfilesDirectory, "default-profile.json")));

        var reloaded = new ProductConfigurationStore(paths);
        Assert.True((await reloaded.LoadAsync()).CanPersist);
        Assert.Equal(profile, reloaded.GetProfile(profile.Id));
    }

    [Fact]
    public async Task ProfileAndGroupIdentitySurviveRenameAndDuplicatesReceiveNewIds()
    {
        var store = await CreateStoreAsync();
        var group = await store.CreateGroupAsync("Speakers", [new SourceReference { ProviderId = "provider-a", SourceId = "source-a", LastKnownDisplayName = "Speakers", LastKnownKind = "playback" }]);
        var renamedGroup = await store.SaveGroupAsync(group with { FriendlyName = "Desk Speakers" });
        var profile = await store.CreateProfileAsync("Blue", group.Id);
        var renamedProfile = await store.SaveProfileAsync(profile with { FriendlyName = "Blue Line" });
        var duplicate = await store.DuplicateProfileAsync(profile.Id);

        Assert.Equal(group.Id, renamedGroup.Id);
        Assert.Equal(profile.Id, renamedProfile.Id);
        Assert.True(renamedProfile.Revision > profile.Revision);
        Assert.NotEqual(profile.Id, duplicate.Id);
        Assert.Equal("Blue Line Copy", duplicate.FriendlyName);

        await store.SetDefaultProfileAsync(profile.Id);
        await store.SetDefaultGroupAsync(group.Id);
        Assert.Equal(profile.Id, store.Catalog.DefaultProfileId);
        Assert.Equal(group.Id, store.Catalog.DefaultSourceGroupId);
    }

    [Fact]
    public async Task DependencyRulesAndFailedSavePreserveLastSavedProfile()
    {
        var store = await CreateStoreAsync();
        var group = await store.CreateGroupAsync("Referenced", [new SourceReference { ProviderId = "provider-a", SourceId = "source-a" }]);
        var profile = await store.CreateProfileAsync("Saved", group.Id);
        var before = store.GetProfile(profile.Id);

        await Assert.ThrowsAsync<ConfigurationDependencyException>(() => store.DeleteGroupAsync(group.Id));
        await Assert.ThrowsAsync<ConfigurationDependencyException>(() => store.DeleteProfileAsync(ProductDefaults.DefaultProfileId));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveProfileAsync(before with { Waveform = before.Waveform with { Color = "not-a-color" } }));

        Assert.Equal(before, store.GetProfile(profile.Id));
        Assert.Equal(before, new ProductConfigurationStore(AuralinePaths.FromRoot(_root)).TapLoad().GetProfile(profile.Id));
    }

    [Fact]
    public async Task MalformedProfileIsPreservedAndBlocksDestructiveReset()
    {
        var store = await CreateStoreAsync();
        var path = Path.Combine(AuralinePaths.FromRoot(_root).ProfilesDirectory, "default-profile.json");
        var malformed = Encoding.UTF8.GetBytes("{ invalid");
        await File.WriteAllBytesAsync(path, malformed);

        var reloaded = new ProductConfigurationStore(AuralinePaths.FromRoot(_root));
        var result = await reloaded.LoadAsync();

        Assert.False(result.CanPersist);
        Assert.Equal(malformed, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reloaded.CreateProfileAsync("Nope", ProductDefaults.DefaultSourceGroupId));
    }

    [Fact]
    public async Task MalformedSourceGroupIsPreservedAndDefaultProfileRemainsSafelyReadable()
    {
        var store = await CreateStoreAsync();
        var path = Path.Combine(AuralinePaths.FromRoot(_root).SourceGroupsDirectory, "default-source-group.json");
        var malformed = Encoding.UTF8.GetBytes("{ invalid group");
        await File.WriteAllBytesAsync(path, malformed);

        var reloaded = new ProductConfigurationStore(AuralinePaths.FromRoot(_root));
        var result = await reloaded.LoadAsync();

        Assert.False(result.CanPersist);
        Assert.Equal(malformed, await File.ReadAllBytesAsync(path));
        Assert.Equal(ProductDefaults.DefaultProfileId, reloaded.GetProfile(ProductDefaults.DefaultProfileId).Id);
    }

    [Fact]
    public async Task SourceResolutionIsConservativeAcrossOnlineOfflineAmbiguousAndLogicalIntent()
    {
        var store = await CreateStoreAsync();
        var exactGroup = await store.CreateGroupAsync("Exact", [new SourceReference { ProviderId = "p", SourceId = "s1", LastKnownDisplayName = "Speakers", LastKnownKind = "playback" }]);
        var ambiguousGroup = await store.CreateGroupAsync("Ambiguous", [new SourceReference { ProviderId = "p", SourceId = "missing", LastKnownDisplayName = "Speakers", LastKnownKind = "playback" }]);
        var logicalGroup = await store.CreateGroupAsync("Logical", [new SourceReference { ProviderId = "p", LogicalIntent = ProductDefaults.DefaultLogicalSourceIntent }]);
        var sources = new[]
        {
            new ProviderSource("p", "s1", "Speakers", "playback", "available", true, ["waveform"]),
            new ProviderSource("p", "s2", "Speakers", "playback", "available", false, ["waveform"])
        };
        var online = new[] { new ProviderStatus("p", "Provider", "http://127.0.0.1:1", true, ProviderLifecycleState.Connected, null, DateTimeOffset.UtcNow, "r1", sources) };

        Assert.Equal(SourceMemberResolution.Resolved, store.ResolveGroup(exactGroup.Id, online).Members.Single().Resolution);
        Assert.Equal(SourceMemberResolution.Ambiguous, store.ResolveGroup(ambiguousGroup.Id, online).Members.Single().Resolution);
        Assert.Equal(SourceMemberResolution.Resolved, store.ResolveGroup(logicalGroup.Id, online).Members.Single().Resolution);

        await store.RecordSourcesAsync(online);
        var offline = new[] { online[0] with { State = ProviderLifecycleState.Reconnecting, Sources = [] } };
        Assert.Equal(SourceMemberResolution.Stale, store.ResolveGroup(exactGroup.Id, offline).Members.Single().Resolution);
    }

    private async Task<ProductConfigurationStore> CreateStoreAsync()
    {
        var store = new ProductConfigurationStore(AuralinePaths.FromRoot(_root));
        await store.LoadAsync();
        return store;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

internal static class ProductConfigurationTestExtensions
{
    public static ProductConfigurationStore TapLoad(this ProductConfigurationStore store)
    {
        var result = store.LoadAsync().GetAwaiter().GetResult();
        Assert.True(result.CanPersist);
        return store;
    }
}
