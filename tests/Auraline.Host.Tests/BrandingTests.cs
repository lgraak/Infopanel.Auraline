using System.Drawing;
using Auraline.Host.Platform.Windows;
using Auraline.Host.Web;

namespace Auraline.Host.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void EmbeddedBrandMarkIsPngAndAppearsInHostNavigation()
    {
        Assert.True(BrandingAssets.MarkPng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));

        var page = UiRenderer.ErrorPage("Test", "Test", "/", "system");
        Assert.Contains("/assets/auraline-mark.png", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedTrayIconCanBeLoadedByWindowsIconReader()
    {
        using var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("Auraline.Host.Branding.auraline-tray.ico");
        Assert.NotNull(stream);

        using var icon = new Icon(stream);
        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }
}
