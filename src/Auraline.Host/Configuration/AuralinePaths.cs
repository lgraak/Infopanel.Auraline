namespace Auraline.Host.Configuration;

public sealed record AuralinePaths(string Root, string ConfigDirectory, string LogsDirectory, string ConfigFile)
{
    public static AuralinePaths ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FromRoot(Path.Combine(localAppData, "Auraline"));
    }

    public static AuralinePaths FromRoot(string root)
    {
        var configDirectory = Path.Combine(root, "config");
        return new(root, configDirectory, Path.Combine(root, "logs"), Path.Combine(configDirectory, "host.json"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
