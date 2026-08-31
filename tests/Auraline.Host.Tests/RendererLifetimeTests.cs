using System.Diagnostics;
using System.Text.RegularExpressions;
using Auraline.Host.Configuration;
using Auraline.Host.Waveform;
using Xunit.Abstractions;

namespace Auraline.Host.Tests;

public sealed class RendererLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    public void PerFrameSkiaPathIsDisposedByTheRenderInvocation()
    {
        var repoRoot = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var rendererSource = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "src",
            "Auraline.Host",
            "Waveform",
            "WaveformRenderer.cs"));

        Assert.Matches(
            new Regex(@"using\s+var\s+path\s*=\s*BuildPath\(", RegexOptions.CultureInvariant),
            rendererSource);
    }

    [Fact]
    public async Task SharedRendererSurvivesConcurrentSessionAndPreviewWork()
    {
        var renderer = new WaveformRenderer();
        var samples = Enumerable.Range(0, 128)
            .Select(index => (float)Math.Sin(index * Math.PI / 16))
            .ToArray();
        var frame = new WaveformProcessedFrame("stress", 1, 1, 1, samples, [samples]);
        var states = new[]
        {
            WaveformVisualizationState.Active,
            WaveformVisualizationState.Idle,
            WaveformVisualizationState.Reconnecting,
            WaveformVisualizationState.Unavailable
        };
        var settings = new[]
        {
            new WaveformProfileSettings { Color = "#76B9FF" },
            new WaveformProfileSettings
            {
                Color = "#FF5533",
                ScaleMode = WaveformScaleMode.Fixed,
                FixedScale = 1.5,
                SmoothingEnabled = true,
                SmoothingAmount = 0.65
            }
        };

        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 250; iteration++)
            {
                var width = 64 + (worker % 4) * 16;
                var height = 32 + (iteration % 3) * 8;
                var sequence = checked((ulong)(worker * 250 + iteration + 1));
                var rendered = renderer.Render(
                    frame,
                    states[(worker + iteration) % states.Length],
                    width,
                    height,
                    sequence,
                    DateTimeOffset.UnixEpoch.AddTicks((long)sequence),
                    iteration % 2 == 0 ? 30 : 60,
                    iteration,
                    settings[(worker + iteration) % settings.Length]);

                Assert.Equal(width * height * 4, rendered.Pixels.Length);
                Assert.Equal(sequence, rendered.Sequence);
                if (iteration % 50 == 0)
                    Assert.NotEmpty(renderer.EncodePng(rendered));
            }
        }));

        await Task.WhenAll(workers);
    }

    [Fact]
    public async Task SharedRendererCompletesBoundedHighRateSoak()
    {
        var configuredSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("AURALINE_RENDERER_SOAK_SECONDS"),
            out var parsedSeconds)
            ? parsedSeconds
            : 1;
        var duration = TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 1, 300));
        var renderer = new WaveformRenderer();
        var samples = Enumerable.Range(0, 128)
            .Select(index => (float)Math.Sin(index * Math.PI / 16))
            .ToArray();
        var frame = new WaveformProcessedFrame("soak", 1, 1, 1, samples, [samples]);
        var states = new[]
        {
            WaveformVisualizationState.Active,
            WaveformVisualizationState.Idle,
            WaveformVisualizationState.Reconnecting,
            WaveformVisualizationState.Unavailable
        };
        var stopwatch = Stopwatch.StartNew();
        long renderCount = 0;
        long pngCount = 0;

        var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var iteration = 0;
            while (stopwatch.Elapsed < duration)
            {
                var sequence = checked((ulong)Interlocked.Increment(ref renderCount));
                var rendered = renderer.Render(
                    frame,
                    states[(worker + iteration) % states.Length],
                    64 + worker * 16,
                    32 + worker * 8,
                    sequence,
                    DateTimeOffset.UnixEpoch.AddTicks((long)sequence),
                    iteration % 2 == 0 ? 30 : 60,
                    iteration,
                    new WaveformProfileSettings
                    {
                        Color = iteration % 2 == 0 ? "#76B9FF" : "#FF5533",
                        SmoothingEnabled = iteration % 3 == 0,
                        SmoothingAmount = iteration % 3 == 0 ? 0.65 : 0
                    });

                Assert.Equal(rendered.Width * rendered.Height * 4, rendered.Pixels.Length);
                if ((iteration & 127) == 0)
                {
                    Assert.NotEmpty(renderer.EncodePng(rendered));
                    Interlocked.Increment(ref pngCount);
                }

                iteration++;
            }
        }));

        await Task.WhenAll(workers);

        Assert.True(renderCount > 0);
        output.WriteLine(
            "Completed {0} renders and {1} PNG encodes in {2:F2} seconds.",
            renderCount,
            pngCount,
            stopwatch.Elapsed.TotalSeconds);
    }

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo start)
    {
        var current = start;
        while (current.Parent is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root for renderer lifetime validation.");
    }
}
