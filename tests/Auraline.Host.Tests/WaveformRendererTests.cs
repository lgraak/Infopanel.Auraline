using Auraline.Host.Waveform;
using Auraline.Host.Configuration;
using SkiaSharp;

namespace Auraline.Host.Tests;

public sealed class WaveformRendererTests
{
    [Fact]
    public void RenderRejectsOutOfRangeDimensions()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 1, 2, 3, [0f], [[0f]]);

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(frame, WaveformVisualizationState.Active, 10, 128, 1, DateTimeOffset.UtcNow, 30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(frame, WaveformVisualizationState.Active, 2049, 128, 1, DateTimeOffset.UtcNow, 30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(frame, WaveformVisualizationState.Active, 128, 8, 1, DateTimeOffset.UtcNow, 30, 0));
    }

    [Fact]
    public void RenderProducesExpectedFrameMetadata()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 7, 3, 1234, [0.2f, -0.4f, 0.1f], [[0.2f, -0.4f, 0.1f]]);
        var rendered = renderer.Render(frame, WaveformVisualizationState.Active, 128, 64, 7, DateTimeOffset.UnixEpoch, 30, 0);

        Assert.Equal(128, rendered.Width);
        Assert.Equal(64, rendered.Height);
        Assert.Equal(WaveformRenderer.PixelFormat, rendered.PixelFormat);
        Assert.Equal(128 * 4, rendered.Stride);
        Assert.Equal(7ul, rendered.Sequence);
        Assert.Equal("Active", rendered.VisualState);
        Assert.True(rendered.Premultiplied);
        Assert.Equal(30, rendered.TargetFps);
    }

    [Fact]
    public void RenderKeepsTransparentBackgroundByDefault()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 1, 1, 1, [0f], [[0f]]);
        var rendered = renderer.Render(frame, WaveformVisualizationState.Active, 64, 64, 1, DateTimeOffset.UtcNow, 30, 1);

        var firstPixelAlpha = rendered.Pixels[3];
        Assert.Equal(0, firstPixelAlpha);
    }

    [Fact]
    public void RenderChangesPixelsForDifferentStates()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 1, 1, 1, [0.9f, -0.9f, 0.9f, -0.9f], [[0.9f, -0.9f, 0.9f, -0.9f]]);

        var active = renderer.Render(frame, WaveformVisualizationState.Active, 80, 40, 1, DateTimeOffset.UtcNow, 30, 1);
        var idle = renderer.Render(frame, WaveformVisualizationState.Idle, 80, 40, 2, DateTimeOffset.UtcNow, 30, 2);
        var reconnecting = renderer.Render(frame, WaveformVisualizationState.Reconnecting, 80, 40, 3, DateTimeOffset.UtcNow, 30, 3);
        var unavailable = renderer.Render(frame, WaveformVisualizationState.Unavailable, 80, 40, 4, DateTimeOffset.UtcNow, 30, 4);

        Assert.False(active.Pixels.SequenceEqual(idle.Pixels));
        Assert.False(active.Pixels.SequenceEqual(unavailable.Pixels));
        Assert.NotEqual(active.VisualState, idle.VisualState);
        Assert.NotEqual(reconnecting.VisualState, unavailable.VisualState);
        Assert.True(VerticalTraceSpan(idle) < VerticalTraceSpan(active));
    }

    [Fact]
    public void EncodePngPreservesRenderedGeometryAndRejectsInvalidPixels()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 1, 1, 1, [0.8f, -0.4f, 0.2f], [[0.8f, -0.4f, 0.2f]]);
        var rendered = renderer.Render(frame, WaveformVisualizationState.Active, 96, 48, 1, DateTimeOffset.UtcNow, 30, 1);

        var png = renderer.EncodePng(rendered);
        using var decoded = SKBitmap.Decode(png);

        Assert.NotNull(decoded);
        Assert.Equal(96, decoded.Width);
        Assert.Equal(48, decoded.Height);
        Assert.Throws<ArgumentException>(() => renderer.EncodePng(rendered with { Pixels = [] }));
    }

    [Fact]
    public void ProfileColorScaleAndSmoothingTruthfullyChangeRendererOutput()
    {
        var renderer = new WaveformRenderer();
        var frame = new WaveformProcessedFrame("stream", 1, 1, 1, [0.2f, -0.2f, 0.2f, -0.2f], [[0.2f, -0.2f, 0.2f, -0.2f]]);
        var automatic = new WaveformProfileSettings { Color = "#0000FF", SmoothingEnabled = false };
        var fixedRed = automatic with { Color = "#FF0000", ScaleMode = WaveformScaleMode.Fixed, FixedScale = 2 };
        var smoothed = fixedRed with { SmoothingEnabled = true, SmoothingAmount = 0.8 };

        var automaticFrame = renderer.Render(frame, WaveformVisualizationState.Active, 160, 80, 1, DateTimeOffset.UtcNow, 30, 1, automatic);
        var fixedFrame = renderer.Render(frame, WaveformVisualizationState.Active, 160, 80, 2, DateTimeOffset.UtcNow, 30, 1, fixedRed);
        var smoothedFrame = renderer.Render(frame, WaveformVisualizationState.Active, 160, 80, 3, DateTimeOffset.UtcNow, 30, 1, smoothed);

        Assert.False(automaticFrame.Pixels.SequenceEqual(fixedFrame.Pixels));
        Assert.False(fixedFrame.Pixels.SequenceEqual(smoothedFrame.Pixels));
        Assert.True(VerticalTraceSpan(fixedFrame) > VerticalTraceSpan(automaticFrame));
    }

    private static int VerticalTraceSpan(WaveformRenderedFrame frame)
    {
        var minY = frame.Height;
        var maxY = -1;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.Pixels[y * frame.Stride + x * 4 + 3] == 0) continue;
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxY < 0 ? 0 : maxY - minY + 1;
    }
}
