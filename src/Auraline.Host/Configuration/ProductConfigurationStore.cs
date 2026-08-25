using System.Text.Json;
using Auraline.Host.Providers;

namespace Auraline.Host.Configuration;

public sealed record ProductConfigurationLoadResult(bool Created, string? Error, bool CanPersist);

public sealed class ProductConfigurationStore : IProfileCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly AuralinePaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotGate = new();
    private ProductCatalogDocument _catalog = new();
    private SourceCatalogDocument _sources = new();
    private Dictionary<string, SourceGroupDefinition> _groups = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProfileDefinition> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ProductConfigurationStore(AuralinePaths paths) => _paths = paths;

    public string? LoadError { get; private set; }
    public bool CanPersist { get; private set; } = true;
    public long SaveFailureCount { get; private set; }
    public long ValidationFailureCount { get; private set; }

    public ProductCatalogDocument Catalog { get { lock (_snapshotGate) return _catalog; } }
    public SourceCatalogDocument SourceCatalog { get { lock (_snapshotGate) return _sources with { Sources = [.. _sources.Sources] }; } }

    public async Task<ProductConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var created = !File.Exists(_paths.CatalogFile);
        try
        {
            var catalog = created ? new ProductCatalogDocument() : await ReadRequiredAsync<ProductCatalogDocument>(_paths.CatalogFile, cancellationToken);
            if (catalog.SchemaVersion != ProductCatalogDocument.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported product catalog schema version {catalog.SchemaVersion}.");

            var sources = File.Exists(_paths.SourceCatalogFile)
                ? await ReadRequiredAsync<SourceCatalogDocument>(_paths.SourceCatalogFile, cancellationToken)
                : new SourceCatalogDocument();
            if (sources.SchemaVersion != SourceCatalogDocument.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported source catalog schema version {sources.SchemaVersion}.");

            var groups = await ReadDirectoryAsync<SourceGroupDefinition>(_paths.SourceGroupsDirectory, cancellationToken);
            var profiles = await ReadDirectoryAsync<ProfileDefinition>(_paths.ProfilesDirectory, cancellationToken);
            Bootstrap(groups, profiles);
            ValidateSnapshot(catalog, groups, profiles);

            if (created || !File.Exists(_paths.SourceCatalogFile) || Directory.GetFiles(_paths.SourceGroupsDirectory, "*.json").Length == 0 || Directory.GetFiles(_paths.ProfilesDirectory, "*.json").Length == 0)
            {
                await WriteAtomicAsync(_paths.CatalogFile, catalog, cancellationToken);
                await WriteAtomicAsync(_paths.SourceCatalogFile, sources, cancellationToken);
                foreach (var group in groups.Values) await WriteAtomicAsync(GroupPath(group.Id), group, cancellationToken);
                foreach (var profile in profiles.Values) await WriteAtomicAsync(ProfilePath(profile.Id), profile, cancellationToken);
            }

            lock (_snapshotGate)
            {
                _catalog = catalog;
                _sources = sources;
                _groups = groups;
                _profiles = profiles;
            }
            LoadError = null;
            CanPersist = true;
            return new(created, null, true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            LoadError = $"Product configuration was preserved but could not be loaded: {ex.Message}";
            CanPersist = false;
            var safeGroups = new Dictionary<string, SourceGroupDefinition>(StringComparer.OrdinalIgnoreCase);
            var safeProfiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
            Bootstrap(safeGroups, safeProfiles);
            lock (_snapshotGate)
            {
                _catalog = new ProductCatalogDocument();
                _sources = new SourceCatalogDocument();
                _groups = safeGroups;
                _profiles = safeProfiles;
            }
            return new(false, LoadError, false);
        }
    }

    public IReadOnlyList<SourceGroupDefinition> GetGroups()
    {
        lock (_snapshotGate) return _groups.Values.OrderBy(item => item.FriendlyName).ToArray();
    }

    public SourceGroupDefinition GetGroup(string groupId)
    {
        lock (_snapshotGate) return _groups.TryGetValue(groupId, out var group)
            ? group : throw new KeyNotFoundException($"Source group '{groupId}' was not found.");
    }

    public IReadOnlyList<ProfileDefinition> GetProfiles()
    {
        lock (_snapshotGate) return _profiles.Values.OrderBy(item => item.FriendlyName).ToArray();
    }

    public ProfileDefinition GetProfile(string profileId)
    {
        lock (_snapshotGate) return _profiles.TryGetValue(profileId, out var profile)
            ? profile : throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
    }

    public bool IsRuntimeSupported(ProfileDefinition profile)
    {
        var group = GetGroup(profile.SourceGroupId);
        return group.Members.Count == 1 &&
               group.Members[0].Active &&
               group.Members[0].ProviderId.Equals(HostConfiguration.DefaultProviderId, StringComparison.OrdinalIgnoreCase) &&
               group.Members[0].LogicalIntent == ProductDefaults.DefaultLogicalSourceIntent;
    }

    public async Task<SourceGroupDefinition> CreateGroupAsync(string friendlyName, IReadOnlyList<SourceReference> members, CancellationToken cancellationToken = default)
    {
        var group = new SourceGroupDefinition { Id = NewId("group"), FriendlyName = friendlyName.Trim(), Members = [.. members] };
        return await SaveGroupAsync(group, isCreate: true, cancellationToken);
    }

    public async Task<SourceGroupDefinition> SaveGroupAsync(SourceGroupDefinition group, bool isCreate = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            var errors = ProductConfigurationValidator.ValidateGroup(group);
            if (errors.Count > 0) { ValidationFailureCount++; throw new InvalidDataException(string.Join(" ", errors)); }
            lock (_snapshotGate)
            {
                if (isCreate && _groups.ContainsKey(group.Id)) throw new InvalidOperationException($"Source group '{group.Id}' already exists.");
                if (!isCreate && !_groups.ContainsKey(group.Id)) throw new KeyNotFoundException($"Source group '{group.Id}' was not found.");
            }
            await WriteAtomicSafeAsync(GroupPath(group.Id), group, cancellationToken);
            lock (_snapshotGate) _groups[group.Id] = group;
            return group;
        }
        finally { _gate.Release(); }
    }

    public async Task<SourceGroupDefinition> DuplicateGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var source = GetGroup(groupId);
        return await CreateGroupAsync($"{source.FriendlyName} Copy", source.Members, cancellationToken);
    }

    public async Task DeleteGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            lock (_snapshotGate)
            {
                if (!_groups.ContainsKey(groupId)) throw new KeyNotFoundException($"Source group '{groupId}' was not found.");
                var dependencies = _profiles.Values.Where(item => item.SourceGroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)).Select(item => item.FriendlyName).ToArray();
                if (dependencies.Length > 0) throw new ConfigurationDependencyException($"Source group '{groupId}' is used by profile(s): {string.Join(", ", dependencies)}.");
                if (_catalog.DefaultSourceGroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase)) throw new ConfigurationDependencyException("The default source group cannot be deleted until another group is made default.");
            }
            File.Delete(GroupPath(groupId));
            lock (_snapshotGate) _groups.Remove(groupId);
        }
        finally { _gate.Release(); }
    }

    public async Task<ProfileDefinition> CreateProfileAsync(string friendlyName, string sourceGroupId, CancellationToken cancellationToken = default)
    {
        var profile = new ProfileDefinition { Id = NewId("profile"), FriendlyName = friendlyName.Trim(), SourceGroupId = sourceGroupId };
        return await SaveProfileAsync(profile, isCreate: true, cancellationToken);
    }

    public async Task<ProfileDefinition> SaveProfileAsync(ProfileDefinition profile, bool isCreate = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            ProfileDefinition saved;
            lock (_snapshotGate)
            {
                if (isCreate && _profiles.ContainsKey(profile.Id)) throw new InvalidOperationException($"Profile '{profile.Id}' already exists.");
                if (!isCreate && !_profiles.ContainsKey(profile.Id)) throw new KeyNotFoundException($"Profile '{profile.Id}' was not found.");
                var revision = isCreate ? 1 : checked(_profiles[profile.Id].Revision + 1);
                saved = profile with { Revision = revision };
                var errors = ProductConfigurationValidator.ValidateProfile(saved, _groups.Keys.ToArray());
                if (errors.Count > 0) { ValidationFailureCount++; throw new InvalidDataException(string.Join(" ", errors)); }
            }
            await WriteAtomicSafeAsync(ProfilePath(saved.Id), saved, cancellationToken);
            lock (_snapshotGate) _profiles[saved.Id] = saved;
            return saved;
        }
        finally { _gate.Release(); }
    }

    public async Task<ProfileDefinition> DuplicateProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var source = GetProfile(profileId);
        return await SaveProfileAsync(source with { Id = NewId("profile"), FriendlyName = $"{source.FriendlyName} Copy", Revision = 1 }, true, cancellationToken);
    }

    public async Task SetDefaultProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        _ = GetProfile(profileId);
        await UpdateCatalogAsync(_catalog with { DefaultProfileId = profileId }, cancellationToken);
    }

    public async Task SetDefaultGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        _ = GetGroup(groupId);
        await UpdateCatalogAsync(_catalog with { DefaultSourceGroupId = groupId }, cancellationToken);
    }

    public async Task DeleteProfileAsync(string profileId, Func<string, bool>? isInUse = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            lock (_snapshotGate)
            {
                if (!_profiles.ContainsKey(profileId)) throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
                if (_catalog.DefaultProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)) throw new ConfigurationDependencyException("The default profile cannot be deleted. Promote another profile first.");
                if (isInUse?.Invoke(profileId) == true) throw new ConfigurationDependencyException("The profile is used by an active render session.");
            }
            File.Delete(ProfilePath(profileId));
            lock (_snapshotGate) _profiles.Remove(profileId);
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<string> GetProviderDependencies(string providerId)
    {
        lock (_snapshotGate)
        {
            return _groups.Values
                .Where(group => group.Members.Any(member => member.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
                .Select(group => $"Source group: {group.FriendlyName} [{group.Id}]")
                .ToArray();
        }
    }

    public SourceGroupStatus ResolveGroup(string groupId, IReadOnlyList<ProviderStatus> providers)
    {
        var group = GetGroup(groupId);
        SourceCatalogDocument sourceCatalog;
        lock (_snapshotGate) sourceCatalog = _sources;
        var members = group.Members.Select(member => ResolveMember(member, providers, sourceCatalog)).ToArray();
        return new(group, members);
    }

    public async Task RecordSourcesAsync(IReadOnlyList<ProviderStatus> providers, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            var observedAt = DateTimeOffset.UtcNow;
            SourceCatalogDocument updated;
            lock (_snapshotGate)
            {
                var byIdentity = _sources.Sources.ToDictionary(item => (item.ProviderId, item.SourceId), item => item);
                foreach (var provider in providers)
                    foreach (var source in provider.Sources)
                        byIdentity[(provider.Id, source.SourceId)] = new LastKnownSource
                        {
                            ProviderId = provider.Id,
                            SourceId = source.SourceId,
                            DisplayName = source.DisplayName,
                            Kind = source.Kind,
                            Availability = source.Availability,
                            DefaultPlayback = source.DefaultPlayback,
                            SupportedProducts = [.. source.SupportedProducts],
                            ChannelCount = source.ChannelCount,
                            SampleRateHz = source.SampleRateHz,
                            ObservedAtUtc = observedAt
                        };
                updated = _sources with { RefreshedAtUtc = observedAt, Sources = byIdentity.Values.OrderBy(item => item.ProviderId).ThenBy(item => item.DisplayName).ToList() };
            }
            await WriteAtomicSafeAsync(_paths.SourceCatalogFile, updated, cancellationToken);
            lock (_snapshotGate) _sources = updated;
        }
        finally { _gate.Release(); }
    }

    private async Task UpdateCatalogAsync(ProductCatalogDocument catalog, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritable();
            await WriteAtomicSafeAsync(_paths.CatalogFile, catalog, cancellationToken);
            lock (_snapshotGate) _catalog = catalog;
        }
        finally { _gate.Release(); }
    }

    private static SourceMemberStatus ResolveMember(SourceReference member, IReadOnlyList<ProviderStatus> providers, SourceCatalogDocument lastKnown)
    {
        var provider = providers.FirstOrDefault(item => item.Id.Equals(member.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(member.LogicalIntent))
        {
            if (!string.Equals(member.LogicalIntent, ProductDefaults.DefaultLogicalSourceIntent, StringComparison.Ordinal))
                return new(member, SourceMemberResolution.Unresolved, null, "Unsupported logical intent.");
            if (provider is null || provider.State != ProviderLifecycleState.Connected)
                return new(member, SourceMemberResolution.Stale, null, "Provider is offline; logical intent is retained.");
            var matches = provider.Sources.Where(item => item.DefaultPlayback).ToArray();
            return matches.Length switch
            {
                1 => new(member, SourceMemberResolution.Resolved, ToLastKnown(matches[0], DateTimeOffset.UtcNow), "Logical default playback resolved."),
                > 1 => new(member, SourceMemberResolution.Ambiguous, null, "Provider reported more than one default playback source."),
                _ => new(member, SourceMemberResolution.Unresolved, null, "Provider has no current default playback source.")
            };
        }

        if (provider is { State: ProviderLifecycleState.Connected })
        {
            var exact = provider.Sources.Where(item => item.SourceId.Equals(member.SourceId, StringComparison.Ordinal)).ToArray();
            if (exact.Length == 1) return new(member, SourceMemberResolution.Resolved, ToLastKnown(exact[0], DateTimeOffset.UtcNow), "Exact provider source ID match.");
            if (exact.Length > 1) return new(member, SourceMemberResolution.Ambiguous, null, "Provider returned an ambiguous source ID.");

            if (!string.IsNullOrWhiteSpace(member.LastKnownDisplayName) && !string.IsNullOrWhiteSpace(member.LastKnownKind))
            {
                var metadata = provider.Sources.Where(item =>
                    string.Equals(item.DisplayName, member.LastKnownDisplayName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Kind, member.LastKnownKind, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (metadata.Length == 1) return new(member, SourceMemberResolution.Resolved, ToLastKnown(metadata[0], DateTimeOffset.UtcNow), "Unique high-confidence provider metadata match.");
                if (metadata.Length > 1) return new(member, SourceMemberResolution.Ambiguous, null, "Provider metadata match is ambiguous.");
            }
            return new(member, SourceMemberResolution.Unresolved, null, "Configured source is absent from current discovery.");
        }

        var stale = lastKnown.Sources.FirstOrDefault(item => item.ProviderId.Equals(member.ProviderId, StringComparison.OrdinalIgnoreCase) && item.SourceId.Equals(member.SourceId, StringComparison.Ordinal));
        return stale is null
            ? new(member, SourceMemberResolution.Unresolved, null, "Provider is offline and no last-known metadata exists.")
            : new(member, SourceMemberResolution.Stale, stale, "Last-known metadata; current availability is unknown.");
    }

    private static LastKnownSource ToLastKnown(ProviderSource source, DateTimeOffset observedAt) => new()
    {
        ProviderId = source.ProviderId,
        SourceId = source.SourceId,
        DisplayName = source.DisplayName,
        Kind = source.Kind,
        Availability = source.Availability,
        DefaultPlayback = source.DefaultPlayback,
        SupportedProducts = [.. source.SupportedProducts],
        ChannelCount = source.ChannelCount,
        SampleRateHz = source.SampleRateHz,
        ObservedAtUtc = observedAt
    };

    private static void Bootstrap(Dictionary<string, SourceGroupDefinition> groups, Dictionary<string, ProfileDefinition> profiles)
    {
        if (!groups.ContainsKey(ProductDefaults.DefaultSourceGroupId))
            groups[ProductDefaults.DefaultSourceGroupId] = new SourceGroupDefinition
            {
                Id = ProductDefaults.DefaultSourceGroupId,
                FriendlyName = "Default Playback",
                Members = [new SourceReference { ProviderId = HostConfiguration.DefaultProviderId, LogicalIntent = ProductDefaults.DefaultLogicalSourceIntent }]
            };
        if (!profiles.ContainsKey(ProductDefaults.DefaultProfileId))
            profiles[ProductDefaults.DefaultProfileId] = new ProfileDefinition
            {
                Id = ProductDefaults.DefaultProfileId,
                FriendlyName = "Default Waveform",
                SourceGroupId = ProductDefaults.DefaultSourceGroupId
            };
    }

    private static void ValidateSnapshot(ProductCatalogDocument catalog, Dictionary<string, SourceGroupDefinition> groups, Dictionary<string, ProfileDefinition> profiles)
    {
        foreach (var group in groups.Values)
        {
            var errors = ProductConfigurationValidator.ValidateGroup(group);
            if (errors.Count > 0) throw new InvalidDataException($"Source group '{group.Id}' is invalid: {string.Join(" ", errors)}");
        }
        foreach (var profile in profiles.Values)
        {
            var errors = ProductConfigurationValidator.ValidateProfile(profile, groups.Keys.ToArray());
            if (errors.Count > 0) throw new InvalidDataException($"Profile '{profile.Id}' is invalid: {string.Join(" ", errors)}");
        }
        if (!groups.ContainsKey(catalog.DefaultSourceGroupId)) throw new InvalidDataException("Default source group does not exist.");
        if (!profiles.ContainsKey(catalog.DefaultProfileId)) throw new InvalidDataException("Default profile does not exist.");
    }

    private async Task<Dictionary<string, T>> ReadDirectoryAsync<T>(string directory, CancellationToken cancellationToken) where T : class
    {
        var items = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var value = await ReadRequiredAsync<T>(path, cancellationToken);
            var id = value switch { SourceGroupDefinition group => group.Id, ProfileDefinition profile => profile.Id, _ => throw new NotSupportedException() };
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.Ordinal)) throw new InvalidDataException($"Configuration file '{path}' does not match object ID '{id}'.");
            if (!items.TryAdd(id, value)) throw new InvalidDataException($"Duplicate configuration object ID '{id}'.");
        }
        return items;
    }

    private static async Task<T> ReadRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new JsonException($"Configuration document '{path}' was empty.");
    }

    private async Task WriteAtomicSafeAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        try { await WriteAtomicAsync(path, value, cancellationToken); }
        catch { SaveFailureCount++; throw; }
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private string GroupPath(string id) => Path.Combine(_paths.SourceGroupsDirectory, id + ".json");
    private string ProfilePath(string id) => Path.Combine(_paths.ProfilesDirectory, id + ".json");
    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private void EnsureWritable()
    {
        if (!CanPersist) throw new InvalidOperationException("Product configuration updates are disabled until malformed configuration is repaired.");
    }
}

public sealed class ConfigurationDependencyException(string message) : Exception(message);
