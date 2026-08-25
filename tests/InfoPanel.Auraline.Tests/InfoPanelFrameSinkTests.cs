using Auraline.Contracts;
using InfoPanel.Auraline.Adapters;
using InfoPanel.Plugins.Graphics;
using SkiaSharp;

namespace InfoPanel.Auraline.Tests;

public sealed class InfoPanelFrameSinkTests
{
    [Fact]
    public void CopiesRgbaPremultipliedPixelsWithoutChannelOrStrideConversion()
    {
        using var writer = new FakeWriter(16, 16);
        var sink = new InfoPanelFrameSink("waveform", writer);
        var pixels = new byte[16 * 16 * 4];
        pixels[0] = 64;
        pixels[1] = 32;
        pixels[2] = 16;
        pixels[3] = 128;
        pixels[7] = 0;
        var frame = new FrameReadResult(16, 16, 64, "rgba8888-premul", true, 7,
            DateTimeOffset.UtcNow.UtcTicks, 30, pixels);

        sink.Publish(frame);

        Assert.Equal(1, writer.InvalidationCount);
        Assert.Equal(pixels, writer.Bitmap.GetPixelSpan().ToArray());
        Assert.Equal(new byte[] { 64, 32, 16, 128 }, writer.Bitmap.GetPixelSpan()[..4].ToArray());
        Assert.Equal(0, writer.Bitmap.GetPixelSpan()[7]);
    }

    [Fact]
    public void ResizesBeforePublishingNewGeometry()
    {
        using var writer = new FakeWriter(16, 16);
        var sink = new InfoPanelFrameSink("waveform", writer);
        var frame = new FrameReadResult(32, 24, 128, "rgba8888-premul", true, 1,
            DateTimeOffset.UtcNow.UtcTicks, 60, new byte[32 * 24 * 4]);

        sink.Publish(frame);

        Assert.Equal((32, 24), (writer.Width, writer.Height));
        Assert.Equal(1, writer.ResizeCount);
        Assert.All(writer.Bitmap.GetPixelSpan().ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("bgra8888", true)]
    [InlineData("rgba8888-premul", false)]
    public void RejectsInvalidPixelSemantics(string format, bool premultiplied)
    {
        using var writer = new FakeWriter(16, 16);
        var sink = new InfoPanelFrameSink("waveform", writer);
        var frame = new FrameReadResult(16, 16, 64, format, premultiplied, 1,
            DateTimeOffset.UtcNow.UtcTicks, 30, new byte[16 * 16 * 4]);
        Assert.Throws<InvalidDataException>(() => sink.Publish(frame));
    }

    [Fact]
    public void DisconnectFailureReplacesStaleFrameWithExplicitTransparentStatusSurface()
    {
        using var writer = new FakeWriter(320, 120);
        writer.Bitmap.Erase(SKColors.Black);
        var sink = new InfoPanelFrameSink("waveform", writer);

        sink.PublishUnavailable("offline");

        Assert.Equal(1, writer.InvalidationCount);
        var pixels = writer.Bitmap.GetPixelSpan();
        Assert.Contains((byte)0, pixels.ToArray().Where((_, index) => index % 4 == 3));
        Assert.Contains(pixels.ToArray().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    private sealed class FakeWriter : IPluginImageWriter
    {
        public FakeWriter(int width, int height) => Replace(width, height);

        public SKBitmap Bitmap { get; private set; } = null!;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int ResizeCount { get; private set; }
        public int InvalidationCount { get; private set; }

        public void Invalidate() => InvalidationCount++;

        public void Resize(int width, int height)
        {
            ResizeCount++;
            Replace(width, height);
        }

        public void Dispose() => Bitmap?.Dispose();

        private void Replace(int width, int height)
        {
            Bitmap?.Dispose();
            Width = width;
            Height = height;
            Bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        }
    }
}
