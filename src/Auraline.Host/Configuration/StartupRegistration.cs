using Microsoft.Win32;

namespace Auraline.Host.Configuration;

public sealed record StartupRegistrationResult(bool Succeeded, string? Error = null);

public interface IStartupRegistration
{
    StartupRegistrationResult Apply(bool enabled, string executablePath);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Auraline";

    public StartupRegistrationResult Apply(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return new(true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new(false, ex.Message);
        }
    }
}
