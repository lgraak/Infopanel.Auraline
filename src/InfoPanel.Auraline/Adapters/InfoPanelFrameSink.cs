using Auraline.Contracts;
using InfoPanel.Auraline.Core;
using InfoPanel.Plugins.Graphics;
using SkiaSharp;

namespace InfoPanel.Auraline.Adapters;

internal sealed class InfoPanelFrameSink(string imageId, IPluginImageWriter writer) : IPluginFrameSink
{
    private readonly object _gate = new();

    public string ImageId { get; } = imageId;

    public int Width => writer.Width;

    public int Height => writer.Height;

    public void Publish(FrameReadResult frame)
    {
        lock (_gate)
        {
            ValidateFrame(frame);
            if (writer.Width != frame.Width || writer.Height != frame.Height)
                writer.Resize(frame.Width, frame.Height);
            var bitmap = writer.Bitmap;
            if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Premul ||
                bitmap.Width != frame.Width || bitmap.Height != frame.Height || bitmap.RowBytes != frame.Stride)
                throw new InvalidDataException("InfoPanel image writer pixel geometry is incompatible with Auraline RGBA8888-premultiplied frames.");
            frame.Pixels.AsSpan().CopyTo(bitmap.GetPixelSpan());
            writer.Invalidate();
        }
    }

    public void PublishUnavailable(string message)
    {
        lock (_gate)
        {
            var bitmap = writer.Bitmap;
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(255, 120, 120, 220)
            };
            using var font = new SKFont(SKTypeface.Default, Math.Clamp(bitmap.Height / 7f, 12f, 32f));
            const string label = "Auraline unavailable";
            var textWidth = font.MeasureText(label, paint);
            var x = Math.Max(4f, (bitmap.Width - textWidth) / 2f);
            var y = Math.Clamp(bitmap.Height / 2f + font.Size / 3f, 12f, bitmap.Height - 4f);
            canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
            writer.Invalidate();
        }
    }

    private static void ValidateFrame(FrameReadResult frame)
    {
        if (frame.Width is < 16 or > 2048 || frame.Height is < 16 or > 2048 ||
            frame.Stride != checked(frame.Width * 4) ||
            frame.Pixels.Length != checked(frame.Stride * frame.Height) ||
            !string.Equals(frame.PixelFormat, "rgba8888-premul", StringComparison.Ordinal) ||
            !frame.Premultiplied)
            throw new InvalidDataException("Auraline frame is not valid RGBA8888-premultiplied pixel data.");
    }
}
