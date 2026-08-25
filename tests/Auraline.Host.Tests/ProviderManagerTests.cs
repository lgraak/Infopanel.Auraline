using System.Collections.Concurrent;
using Auraline.Host.Configuration;
using Auraline.Host.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraline.Host.Tests;

public sealed class ProviderManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuralineTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnabledProviderConnectsDiscoversAndManualReconnectStartsFreshAttempt()
    {
        var connector = new FakeConnector();
        var (store, manager) = await CreateManagerAsync(connector);

        await manager.StartAsync(default);
        await WaitUntilAsync(() => manager.GetStatuses().Single().State == ProviderLifecycleState.Connected);
        Assert.Single(manager.GetStatuses().Single().Sources);

        await manager.ReconnectAsync(HostConfiguration.DefaultProviderId);
        await WaitUntilAsync(() => connector.Attempts >= 2 && manager.GetStatuses().Single().State == ProviderLifecycleState.Connected);

        Assert.Equal(2, connector.Attempts);
        await manager.StopAsync(default);
        manager.Dispose();
        GC.KeepAlive(store);
    }

    [Fact]
    public async Task DisableCancelsLifecycleAndEnableReconnectsAutomatically()
    {
        var connector = new FakeConnector();
        var (store, manager) = await CreateManagerAsync(connector, enabled: false);
        await manager.StartAsync(default);
        Assert.Equal(ProviderLifecycleState.Disabled, manager.GetStatuses().Single().State);

        await manager.SetEnabledAsync(HostConfiguration.DefaultProviderId, true);
        await WaitUntilAsync(() => manager.GetStatuses().Single().State == ProviderLifecycleState.Connected);
        await manager.SetEnabledAsync(HostConfiguration.DefaultProviderId, false);

        var status = manager.GetStatuses().Single();
        Assert.False(status.Enabled);
        Assert.Equal(ProviderLifecycleState.Disabled, status.State);
        Assert.False(store.Current.Providers.Single().Enabled);
        await manager.StopAsync(default);
        manager.Dispose();
    }

    [Fact]
    public async Task FailureEntersReconnectWithBoundedDelayAndShutdownCancelsWait()
    {
        var connector = new FakeConnector(new HttpRequestException("connection refused"));
        var delay = new BlockingDelay();
        var (_, manager) = await CreateManagerAsync(connector, delay: delay);
        await manager.StartAsync(default);

        await WaitUntilAsync(() => delay.Observed.Count > 0);
        var status = manager.GetStatuses().Single();
        Assert.Equal(ProviderLifecycleState.Reconnecting, status.State);
        Assert.Contains("connection refused", status.LastError);
        Assert.Equal(TimeSpan.FromMilliseconds(500), delay.Observed.Single());

        await manager.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        Assert.Equal(ProviderLifecycleState.Disconnected, manager.GetStatuses().Single().State);
        manager.Dispose();
    }

    [Fact]
    public async Task ManualRefreshReplacesSourceSnapshot()
    {
        var connector = new FakeConnector();
        var (_, manager) = await CreateManagerAsync(connector);
        await manager.StartAsync(default);
        await WaitUntilAsync(() => manager.GetStatuses().Single().State == ProviderLifecycleState.Connected);

        await manager.RefreshSourcesAsync(HostConfiguration.DefaultProviderId);

        Assert.Equal(2, connector.Attempts);
        Assert.Equal("revision-2", manager.GetStatuses().Single().DiscoveryRevision);
        await manager.StopAsync(default);
        manager.Dispose();
    }

    private async Task<(ConfigurationStore Store, ProviderManager Manager)> CreateManagerAsync(FakeConnector connector, bool enabled = true, IAsyncDelay? delay = null)
    {
        var store = new ConfigurationStore(AuralinePaths.FromRoot(_root));
        await store.LoadAsync();
        if (!enabled)
            await store.UpdateAsync(current => current with { Providers = current.Providers.Select(p => p with { Enabled = false }).ToList() });
        var manager = new ProviderManager(store, connector, delay ?? new BlockingDelay(), NullLogger<ProviderManager>.Instance);
        return (store, manager);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeConnector(Exception? failure = null) : IProviderConnector
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        public Task<ProviderConnectionResult> ConnectAndDiscoverAsync(ProviderConfiguration provider, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (failure is not null) return Task.FromException<ProviderConnectionResult>(failure);
            IReadOnlyList<ProviderSource> sources = [new(provider.Id, "opaque-source", "Speakers", "playback", "available", true, ["waveform"])];
            return Task.FromResult(new ProviderConnectionResult($"revision-{attempt}", sources));
        }
    }

    private sealed class BlockingDelay : IAsyncDelay
    {
        public ConcurrentQueue<TimeSpan> Observed { get; } = new();
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Observed.Enqueue(delay);
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
