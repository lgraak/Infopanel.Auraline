namespace Auraline.Host.Configuration;

public sealed record StartupRegistrationResult(bool Succeeded, string? Error = null);

public interface IStartupRegistration
{
    StartupRegistrationResult Apply(bool enabled, string executablePath);
}
