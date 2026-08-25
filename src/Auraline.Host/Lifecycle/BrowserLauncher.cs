using System.Diagnostics;

namespace Auraline.Host.Lifecycle;

public interface IBrowserLauncher
{
    bool Open(Uri uri);
}

public sealed class BrowserLauncher(ILogger<BrowserLauncher> logger) : IBrowserLauncher
{
    public bool Open(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            logger.LogInformation("Opened Auraline web UI");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the Auraline web UI");
            return false;
        }
    }
}
