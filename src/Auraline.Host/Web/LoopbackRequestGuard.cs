namespace Auraline.Host.Web;

internal static class LoopbackRequestGuard
{
    public static bool IsAllowed(HttpRequest request, int hostPort)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Equals("cross-site", StringComparison.OrdinalIgnoreCase)) return false;

        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttp &&
               uri.Host == "127.0.0.1" &&
               uri.Port == hostPort;
    }
}
