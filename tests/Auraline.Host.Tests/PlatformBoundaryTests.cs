using Auraline.Host.Platform.Windows;

namespace Auraline.Host.Tests;

public sealed class PlatformBoundaryTests
{
    [Fact]
    public void WindowsPathsPreserveLocalAppDataLayout()
    {
        var localAppData = Path.Combine("test-root", "local-app-data");
        var paths = new WindowsPlatformPaths(() => localAppData).GetPaths();

        Assert.Equal(Path.Combine(localAppData, "Auraline"), paths.Root);
        Assert.Equal(Path.Combine(localAppData, "Auraline", "config", "host.json"), paths.ConfigFile);
        Assert.Equal(Path.Combine(localAppData, "Auraline", "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void StartupRegistrationPreservesQuotedCommandAndDeleteBehavior()
    {
        var registry = new FakeRegistry();
        var registration = new WindowsStartupRegistration(registry);

        Assert.True(registration.Apply(true, @"C:\Program Files\Auraline\Auraline.Host.exe").Succeeded);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", registry.KeyPath);
        Assert.Equal("Auraline", registry.ValueName);
        Assert.Equal("\"C:\\Program Files\\Auraline\\Auraline.Host.exe\"", registry.Value);

        Assert.True(registration.Apply(false, "ignored").Succeeded);
        Assert.True(registry.Deleted);
    }

    private sealed class FakeRegistry : IWindowsStartupRegistry
    {
        public string? KeyPath { get; private set; }
        public string? ValueName { get; private set; }
        public string? Value { get; private set; }
        public bool Deleted { get; private set; }

        public void SetValue(string keyPath, string valueName, string value)
        {
            KeyPath = keyPath;
            ValueName = valueName;
            Value = value;
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            KeyPath = keyPath;
            ValueName = valueName;
            Deleted = true;
        }
    }
}
