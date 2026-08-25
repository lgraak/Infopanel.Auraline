using System.Text.Json;

namespace Auraline.Host.Configuration;

public sealed record ConfigurationLoadResult(HostConfiguration Configuration, bool Created, string? Error, bool CanPersist);

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly AuralinePaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigurationStore(AuralinePaths paths) => _paths = paths;

    public HostConfiguration Current { get; private set; } = HostConfiguration.CreateDefault();
    public string? LoadError { get; private set; }
    public bool CanPersist { get; private set; } = true;

    public async Task<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.ConfigFile))
        {
            Current = HostConfiguration.CreateDefault();
            await SaveCoreAsync(Current, cancellationToken);
            return new(Current, true, null, true);
        }

        try
        {
            await using var stream = File.OpenRead(_paths.ConfigFile);
            var loaded = await JsonSerializer.DeserializeAsync<HostConfiguration>(stream, JsonOptions, cancellationToken)
                         ?? throw new JsonException("Configuration document was empty.");
            var errors = ConfigurationValidator.Validate(loaded);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            Current = loaded;
            LoadError = null;
            CanPersist = true;
            return new(Current, false, null, true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            Current = HostConfiguration.CreateDefault();
            LoadError = $"Configuration was preserved but could not be loaded: {ex.Message}";
            CanPersist = false;
            return new(Current, false, LoadError, false);
        }
    }

    public async Task UpdateAsync(Func<HostConfiguration, HostConfiguration> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!CanPersist) throw new InvalidOperationException("Configuration updates are disabled until the malformed configuration is repaired.");
            var candidate = update(Current);
            var errors = ConfigurationValidator.Validate(candidate);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            await SaveCoreAsync(candidate, cancellationToken);
            Current = candidate;
        }
        finally { _gate.Release(); }
    }

    private async Task SaveCoreAsync(HostConfiguration configuration, CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        var temporary = _paths.ConfigFile + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        if (File.Exists(_paths.ConfigFile)) File.Replace(temporary, _paths.ConfigFile, null);
        else File.Move(temporary, _paths.ConfigFile);
    }
}
