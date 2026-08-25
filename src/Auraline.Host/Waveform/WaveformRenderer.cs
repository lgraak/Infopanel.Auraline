using SkiaSharp;

namespace Auraline.Host.Waveform;

public sealed class WaveformRenderer
{
    public const int MinimumDimension = 16;
    public const int MaximumDimension = 2048;
    public const string PixelFormat = "rgba8888-premul";
    private const string DefaultTraceColor = "#76b9ff";

    private readonly SKColor _baseColor;
    private readonly float _lineWidth;

    public WaveformRenderer(string? traceColor = null, float lineWidth = 2f)
    {
        TraceColor = string.IsNullOrWhiteSpace(traceColor) ? DefaultTraceColor : traceColor;
        _baseColor = ParseColor(TraceColor);
        _lineWidth = Math.Max(1f, lineWidth);
    }

    public string TraceColor { get; }

    public WaveformRenderedFrame Render(
        WaveformProcessedFrame frame,
        WaveformVisualizationState visualState,
        int targetWidth,
        int targetHeight,
        ulong frameSequence,
        DateTimeOffset renderTimestamp,
        int targetFps,
        int sampleSeed)
    {
        ValidateDimensions(targetWidth, targetHeight);
        var samples = SelectDisplaySamples(frame.MonoSamples, visualState, sampleSeed);
        var imageInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Empty);

        var stroke = DetermineColor(visualState);
        using var strokePaint = new SKPaint
        {
            Color = stroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, _lineWidth)
        };
        using var statePaint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(178), TextSize = Math.Max(12, targetHeight / 8), IsStroke = false };

        if (samples.Length > 0)
        {
            var path = BuildPath(samples, targetWidth, targetHeight);
            canvas.DrawPath(path, strokePaint);
        }

        DrawStateOverlay(visualState, canvas, targetWidth, targetHeight, statePaint);
        using var image = surface.Snapshot();
        var stride = targetWidth * 4;
        using var pixelBitmap = new SKBitmap(imageInfo);
        var pixelBufferAddress = pixelBitmap.GetPixels();
        if (pixelBufferAddress == IntPtr.Zero)
            throw new InvalidOperationException("Waveform rendering did not expose a pixel buffer.");

        var success = image.ReadPixels(imageInfo, pixelBufferAddress, stride, 0, 0);
        if (!success)
        {
            throw new InvalidOperationException("Waveform rendering did not produce pixel data.");
        }
        var pixels = pixelBitmap.GetPixelSpan().ToArray();

        return new WaveformRenderedFrame(targetWidth, targetHeight, PixelFormat, stride, frameSequence, renderTimestamp.UtcTicks,
            renderTimestamp.UtcDateTime.ToString("O"), true, visualState.ToString(), targetFps, pixels);
    }

    public byte[] EncodePng(WaveformRenderedFrame frame)
    {
        ValidateDimensions(frame.Width, frame.Height);
        if (!string.Equals(frame.PixelFormat, PixelFormat, StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported waveform pixel format '{frame.PixelFormat}'.", nameof(frame));

        var expectedStride = checked(frame.Width * 4);
        var expectedLength = checked(expectedStride * frame.Height);
        if (frame.Stride != expectedStride || frame.Pixels.Length != expectedLength)
            throw new ArgumentException("Waveform frame pixel geometry was invalid.", nameof(frame));

        var imageInfo = new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(imageInfo);
        frame.Pixels.AsSpan().CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private SKColor DetermineColor(WaveformVisualizationState visualState)
    {
        return visualState switch
        {
            WaveformVisualizationState.Active => _baseColor,
            WaveformVisualizationState.Idle => Dim(_baseColor, 130),
            WaveformVisualizationState.Reconnecting => new SKColor(255, 200, 80, 140),
            WaveformVisualizationState.Unavailable => new SKColor(180, 180, 190, 110),
            WaveformVisualizationState.Degraded => new SKColor(200, 120, 255, 140),
            _ => SKColors.Gray
        };
    }

    private static void DrawStateOverlay(
        WaveformVisualizationState visualState,
        SKCanvas canvas,
        int width,
        int height,
        SKPaint paint)
    {
        var text = visualState is WaveformVisualizationState.Reconnecting ? "Reconnecting…" : visualState is WaveformVisualizationState.Unavailable ? "Source unavailable" : null;
        if (text is null) return;
        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        var x = Math.Max(8, (width - bounds.Width) / 2);
        var y = Math.Min(height - 4, 16 + (Math.Abs(bounds.Top)));
        if (x < width && y >= 0) canvas.DrawText(text, x, y, paint);
    }

    private static SKPath BuildPath(float[] samples, int width, int height)
    {
        var path = new SKPath();
        var centerY = (height - 1) / 2f;
        var halfHeight = Math.Max(1f, (height - 1) / 2f * 0.92f);
        for (var i = 0; i < samples.Length; i++)
        {
            var x = (float)i / Math.Max(1, samples.Length - 1) * (width - 1);
            var sample = Math.Clamp(samples[i], -1f, 1f);
            var y = centerY - sample * halfHeight;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        return path;
    }

    private static float[] SelectDisplaySamples(float[] samples, WaveformVisualizationState state, int frameCountReference)
    {
        if (samples.Length == 0) return [];
        if (state == WaveformVisualizationState.Active) return ApplyDeterministicScale(samples);
        return BuildIdleWave(Math.Max(32, samples.Length), frameCountReference);
    }

    private static float[] ApplyDeterministicScale(float[] samples)
    {
        if (samples.Length == 0) return [];
        var scaled = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++) scaled[i] = Math.Clamp(samples[i] * 0.85f, -1f, 1f);
        return scaled;
    }

    private static float[] BuildIdleWave(int sampleCount, int sequence)
    {
        var idle = new float[sampleCount];
        for (var i = 0; i < idle.Length; i++)
        {
            var phase = sequence * 0.045 + (i * 0.18);
            var envelope = 0.001f + 0.00025f * ((sequence + i) % 8);
            idle[i] = (float)(Math.Sin(phase + Math.PI / 2) * envelope);
        }
        return idle;
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width is < MinimumDimension or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), $"Waveform width {width} is outside supported range.");
        if (height is < MinimumDimension or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(height), $"Waveform height {height} is outside supported range.");
    }

    private static SKColor ParseColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return SKColors.White;
        if (color.StartsWith("#") && (color.Length is 7 or 9))
        {
            if (uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                if (color.Length == 7)
                {
                    var red8 = (byte)(value >> 16);
                    var green8 = (byte)(value >> 8);
                    var blue8 = (byte)value;
                    return new SKColor(red8, green8, blue8);
                }

                var a = (byte)(value >> 24);
                var red = (byte)(value >> 16);
                var green = (byte)(value >> 8);
                var blue = (byte)value;
                return new SKColor(red, green, blue, a);
            }
        }
        return color.Equals("default", StringComparison.OrdinalIgnoreCase) ? _DefaultColor : SKColors.White;
    }

    private static SKColor Dim(SKColor color, byte alpha)
    {
        return new SKColor(color.Red, color.Green, color.Blue, alpha);
    }

    private static readonly SKColor _DefaultColor = new(118, 185, 255, 255);
}
