using Auraline.Host.Web;
using Microsoft.AspNetCore.Http;

namespace Auraline.Host.Tests;

public sealed class LoopbackRequestGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1:48481", null, true)]
    [InlineData("http://evil.example", "cross-site", false)]
    [InlineData("http://127.0.0.1:48480", "same-origin", false)]
    [InlineData(null, "cross-site", false)]
    [InlineData(null, null, true)]
    public void AllowsLocalBrowserAndNonBrowserClientsButRejectsCrossSitePosts(string? origin, string? fetchSite, bool expected)
    {
        var context = new DefaultHttpContext();
        if (origin is not null) context.Request.Headers.Origin = origin;
        if (fetchSite is not null) context.Request.Headers["Sec-Fetch-Site"] = fetchSite;

        Assert.Equal(expected, LoopbackRequestGuard.IsAllowed(context.Request, 48481));
    }
}
