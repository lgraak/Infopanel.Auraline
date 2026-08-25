using Auraline.Host.Configuration;
using Microsoft.Win32;

namespace Auraline.Host.Platform.Windows;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Auraline";
    private readonly IWindowsStartupRegistry _registry;

    public WindowsStartupRegistration()
        : this(new WindowsStartupRegistry())
    {
    }

    internal WindowsStartupRegistration(IWindowsStartupRegistry registry) => _registry = registry;

    public StartupRegistrationResult Apply(bool enabled, string executablePath)
    {
        try
        {
            if (enabled) _registry.SetValue(RunKey, ValueName, $"\"{executablePath}\"");
            else _registry.DeleteValue(RunKey, ValueName);
            return new(true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new(false, ex.Message);
        }
    }
}

internal interface IWindowsStartupRegistry
{
    void SetValue(string keyPath, string valueName, string value);
    void DeleteValue(string keyPath, string valueName);
}

internal sealed class WindowsStartupRegistry : IWindowsStartupRegistry
{
    public void SetValue(string keyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(keyPath);
        key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
