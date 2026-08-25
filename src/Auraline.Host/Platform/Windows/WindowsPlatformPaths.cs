using Auraline.Host.Configuration;

namespace Auraline.Host.Platform.Windows;

public sealed class WindowsPlatformPaths : IPlatformPaths
{
    private readonly Func<string> _getLocalAppData;

    public WindowsPlatformPaths()
        : this(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal WindowsPlatformPaths(Func<string> getLocalAppData) => _getLocalAppData = getLocalAppData;

    public AuralinePaths GetPaths() => AuralinePaths.FromRoot(Path.Combine(_getLocalAppData(), "Auraline"));
}
