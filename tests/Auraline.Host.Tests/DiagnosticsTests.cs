using Auraline.Host.Diagnostics;
using Auraline.Host.Configuration;
using Auraline.Host.Platform.Windows;
using Auraline.Host.Providers;
using Auraline.Host.Waveform;
using Auraline.Host.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Core;
using Serilog.Events;
using System.IO.Compression;

namespace Auraline.Host.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task SelfTestPassesWithAvailableFakesAndExportIsSafeReadableAndBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuralineDiagnosticsTests", Guid.NewGuid().ToString("N"));
        var paths = AuralinePaths.FromRoot(root);
        paths.EnsureDirectories();
        try
        {
            var configuration = new ConfigurationStore(paths);
            await configuration.LoadAsync();
            var products = new ProductConfigurationStore(paths);
            await products.LoadAsync();
            using var providers = new ProviderManager(configuration, new AvailableConnector(), new BlockingDelay(), NullLogger<ProviderManager>.Instance, products);
            await providers.StartAsync(default);
            Assert.True(SpinWait.SpinUntil(() => providers.GetStatuses().Single().State == ProviderLifecycleState.Connected, TimeSpan.FromSeconds(2)));
            var renderer = new WaveformRenderer();
            var frame = renderer.Render(new WaveformProcessedFrame("live", 7, 7, 0, [0f, .5f, 0f], [[0f, .5f, 0f]]),
                WaveformVisualizationState.Active, 64, 32, 7, DateTimeOffset.UtcNow, 30, 1);
            var waveform = new AvailableWaveform(frame);
            var status = new HostStatusService(configuration, providers, waveform, null, products);
            var service = new DiagnosticsService(status, providers, products, configuration,
                new AvailableConnector(), new WindowsSharedMemoryFrameTransportFactory(), new AvailableWaveformSelfTester(), renderer, paths,
                new DiagnosticLogLevel(new LoggingLevelSwitch(LogEventLevel.Information)), new DiagnosticsRedactor());

            var result = await service.RunSelfTestAsync(default);

            Assert.Equal("Pass", result.OverallResult);
            Assert.All(result.Stages, stage => Assert.Equal(SelfTestStageStatus.Pass, stage.Status));
            Assert.Equal(0, status.GetHealth().RenderSessions?.TotalConsumerLeases ?? 0);
            var export = service.CreateExport(new DateTimeOffset(2026, 8, 25, 12, 34, 56, TimeSpan.Zero));
            Assert.Equal("auraline-diagnostics-20260825-123456.zip", export.FileName);
            using var archive = new ZipArchive(new MemoryStream(export.Content), ZipArchiveMode.Read);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("diagnostics-summary.md", names);
            Assert.Contains("build.json", names);
            Assert.Contains("providers.json", names);
            Assert.Contains("sources.json", names);
            Assert.Contains("source-groups.json", names);
            Assert.Contains("profiles.json", names);
            Assert.Contains("render-sessions.json", names);
            Assert.Contains("self-test.json", names);
            Assert.Contains("privacy.txt", names);
            var contents = string.Join("\n", archive.Entries.Select(ReadEntry));
            Assert.DoesNotContain("\"pixels\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"samples\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0.1.0-beta.1", contents, StringComparison.Ordinal);
            await providers.StopAsync(default);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RedactorRemovesProfilePathsSecretLikeValuesAndCurrentIdentifiers()
    {
        var redactor = new DiagnosticsRedactor();
        var input = $"user={Environment.UserName}; host={Environment.MachineName}; path=C:\\Users\\beta-user\\repo; token=abc123; provider=Local Resonance Signal";

        var output = redactor.Redact(input);

        Assert.DoesNotContain(Environment.UserName, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("beta-user", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", output, StringComparison.Ordinal);
        Assert.Contains("Local Resonance Signal", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LogLevelDefaultsToInfoAndDebugIsTemporaryRuntimeState()
    {
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        var level = new DiagnosticLogLevel(levelSwitch);

        Assert.Equal("Info", level.Current);
        level.Set("Debug");
        Assert.Equal("Debug", level.Current);
        level.Set("Info");
        Assert.Equal("Info", level.Current);
        Assert.Throws<ArgumentException>(() => level.Set("Trace"));
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), "ProviderUnavailable")]
    [InlineData(typeof(IOException), "SourceUnavailable")]
    [InlineData(typeof(InvalidDataException), "ConfigurationMalformed")]
    [InlineData(typeof(NotSupportedException), "TransportIncompatible")]
    public void MajorFailuresMapToStableCategories(Type exceptionType, string category)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "failure")!;
        Assert.StartsWith(category + ":", DiagnosticsService.Categorize(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void BetaPackageScriptEnforcesFourFilePluginAndChecksums()
    {
        var root = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var script = File.ReadAllText(Path.Combine(root.FullName, "build", "Build-Beta.ps1"));
        Assert.Contains("--self-contained false", script, StringComparison.Ordinal);
        Assert.Contains("Auraline.Contracts.dll", script, StringComparison.Ordinal);
        Assert.Contains("InfoPanel.Auraline.deps.json", script, StringComparison.Ordinal);
        Assert.Contains("InfoPanel.Auraline.dll", script, StringComparison.Ordinal);
        Assert.Contains("PluginInfo.ini", script, StringComparison.Ordinal);
        Assert.Contains("SHA256", script, StringComparison.Ordinal);
        Assert.DoesNotContain("InfoPanel.Plugins.dll'", script, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo start)
    {
        for (var current = start; current.Parent is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current;
        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private sealed class AvailableConnector : IProviderConnector
    {
        public Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionResult("revision", [new ProviderSource(provider.Id, "opaque-source", "Speakers", "playback", "available", true, ["waveform"])]));
    }

    private sealed class BlockingDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class AvailableWaveform(WaveformRenderedFrame frame) : IWaveformEngineStatusProvider
    {
        public WaveformEngineHealth GetHealth() => new("Active", "default-playback", HostConfiguration.DefaultProviderId,
            "stream", "opaque-source", 2, 48000, "f32-le", 0, 1, 0, 0, 7, 1, 100, 1, 1, 30, "Unknown", 7);
        public WaveformRenderedFrame? GetLatestFrame() => frame;
    }

    private sealed class AvailableWaveformSelfTester : IWaveformSelfTester
    {
        public Task<WaveformSelfTestEvidence> OpenAndDecodeAsync(string providerEndpoint, CancellationToken cancellationToken) =>
            Task.FromResult(new WaveformSelfTestEvidence("isolated-stream", "opaque-source", 2, 48000, 8));
    }
}
