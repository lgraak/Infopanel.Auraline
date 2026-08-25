namespace Auraline.Host.Web;

internal static class BrandingAssets
{
    private const string MarkResource = "Auraline.Host.Branding.auraline-mark-96.png";

    internal static byte[] MarkPng { get; } = Load(MarkResource);

    private static byte[] Load(string resourceName)
    {
        using var stream = typeof(BrandingAssets).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded branding asset '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
