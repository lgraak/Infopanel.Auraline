namespace Auraline.Host.Configuration;

public sealed record AuralinePaths(
    string Root,
    string ConfigDirectory,
    string LogsDirectory,
    string ConfigFile,
    string CatalogFile,
    string SourceCatalogFile,
    string SourceGroupsDirectory,
    string ProfilesDirectory)
{
    public static AuralinePaths FromRoot(string root)
    {
        var configDirectory = Path.Combine(root, "config");
        return new(
            root,
            configDirectory,
            Path.Combine(root, "logs"),
            Path.Combine(configDirectory, "host.json"),
            Path.Combine(configDirectory, "catalog.json"),
            Path.Combine(configDirectory, "sources.json"),
            Path.Combine(configDirectory, "source-groups"),
            Path.Combine(configDirectory, "profiles"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SourceGroupsDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
    }
}
