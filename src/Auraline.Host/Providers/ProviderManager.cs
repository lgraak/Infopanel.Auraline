using System.Collections.Concurrent;
using Auraline.Host.Configuration;

namespace Auraline.Host.Providers;

public sealed class ProviderManager : IHostedService, IDisposable
{
    internal static readonly TimeSpan ConnectedProbeInterval = TimeSpan.FromSeconds(15);

    private readonly ConfigurationStore _configuration;
    private readonly IProviderConnector _connector;
    private readonly IAsyncDelay _delay;
    private readonly ILogger<ProviderManager> _logger;
    private readonly ProductConfigurationStore? _products;
    private readonly ConcurrentDictionary<string, Runtime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _hostCancellation;

    public ProviderManager(ConfigurationStore configuration, IProviderConnector connector, IAsyncDelay delay, ILogger<ProviderManager> logger, ProductConfigurationStore? products = null)
    {
        _configuration = configuration;
        _connector = connector;
        _delay = delay;
        _logger = logger;
        _products = products;
    }

    public IReadOnlyList<ProviderStatus> GetStatuses() => _runtimes.Values.Select(runtime => runtime.Snapshot()).OrderBy(p => p.FriendlyName).ToArray();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var provider in _configuration.Current.Providers)
        {
            var runtime = _runtimes.GetOrAdd(provider.Id, _ => new Runtime(provider));
            if (provider.Enabled) StartRuntime(runtime);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _hostCancellation?.Cancel();
        var tasks = _runtimes.Values.Select(runtime => runtime.StopAsync()).ToArray();
        await Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(string providerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var provider = FindConfiguration(providerId) with { Enabled = enabled };
        await _configuration.UpdateAsync(current => current with
        {
            Providers = current.Providers.Select(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) ? provider : item).ToList()
        }, cancellationToken);

        var runtime = _runtimes.GetOrAdd(provider.Id, _ => new Runtime(provider));
        runtime.UpdateConfiguration(provider);
        if (enabled) StartRuntime(runtime);
        else
        {
            await runtime.StopAsync();
            runtime.SetState(ProviderLifecycleState.Disabled, null);
            _logger.LogInformation("Provider {ProviderId} disabled", providerId);
        }
    }

    public async Task<ProviderConfiguration> AddAsync(ProviderConfiguration provider, CancellationToken cancellationToken = default)
    {
        if (_configuration.Current.Providers.Any(item => item.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Provider '{provider.Id}' already exists.");
        await _configuration.UpdateAsync(current => current with { Providers = [.. current.Providers, provider] }, cancellationToken);
        var runtime = _runtimes.GetOrAdd(provider.Id, _ => new Runtime(provider));
        if (provider.Enabled) StartRuntime(runtime);
        return provider;
    }

    public async Task<ProviderConfiguration> UpdateAsync(string providerId, ProviderConfiguration provider, CancellationToken cancellationToken = default)
    {
        if (!provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provider ID is stable and cannot be changed.", nameof(provider));
        _ = FindConfiguration(providerId);
        await _configuration.UpdateAsync(current => current with
        {
            Providers = current.Providers.Select(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) ? provider : item).ToList()
        }, cancellationToken);
        var runtime = GetRuntime(providerId);
        var wasEnabled = runtime.Snapshot().Enabled;
        var endpointChanged = !runtime.Configuration.Endpoint.Equals(provider.Endpoint, StringComparison.OrdinalIgnoreCase);
        runtime.UpdateConfiguration(provider);
        if (!provider.Enabled)
        {
            await runtime.StopAsync();
            runtime.SetState(ProviderLifecycleState.Disabled, null);
        }
        else if (!wasEnabled || endpointChanged)
        {
            await runtime.StopAsync();
            runtime.SetState(ProviderLifecycleState.Disconnected, null);
            StartRuntime(runtime);
        }
        return provider;
    }

    public async Task DeleteAsync(string providerId, CancellationToken cancellationToken = default)
    {
        _ = FindConfiguration(providerId);
        var dependencies = _products?.GetProviderDependencies(providerId) ?? [];
        if (dependencies.Count > 0)
            throw new ConfigurationDependencyException($"Provider '{providerId}' cannot be deleted because it is referenced by {string.Join("; ", dependencies)}.");
        var runtime = GetRuntime(providerId);
        await runtime.StopAsync();
        await _configuration.UpdateAsync(current => current with
        {
            Providers = current.Providers.Where(item => !item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)).ToList()
        }, cancellationToken);
        _runtimes.TryRemove(providerId, out _);
        runtime.Dispose();
    }

    public async Task ReconnectAsync(string providerId)
    {
        var runtime = GetRuntime(providerId);
        if (!runtime.Snapshot().Enabled) return;
        await runtime.StopAsync();
        runtime.SetState(ProviderLifecycleState.Disconnected, runtime.Snapshot().LastError);
        StartRuntime(runtime);
        _logger.LogInformation("Manual reconnect requested for provider {ProviderId}", providerId);
    }

    public async Task RefreshSourcesAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var runtime = GetRuntime(providerId);
        var snapshot = runtime.Snapshot();
        if (!snapshot.Enabled || snapshot.State != ProviderLifecycleState.Connected)
            throw new InvalidOperationException("Sources can only be refreshed for a connected, enabled provider.");
        try
        {
            var result = await _connector.ConnectAndDiscoverAsync(runtime.Configuration, cancellationToken);
            runtime.SetConnected(result);
            await RecordSourcesBestEffortAsync(cancellationToken);
            _logger.LogInformation("Refreshed {SourceCount} sources for provider {ProviderId}", result.Sources.Count, providerId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.SetState(ProviderLifecycleState.Reconnecting, ConciseError(ex));
            _logger.LogWarning(ex, "Manual source refresh failed for provider {ProviderId}", providerId);
            await ReconnectAsync(providerId);
            throw;
        }
    }

    public void UpdateSourceMetadata(string providerId, string sourceId, int channelCount, int sampleRateHz)
    {
        var runtime = GetRuntime(providerId);
        runtime.UpdateSourceMetadata(sourceId, channelCount, sampleRateHz);
        _ = RecordSourcesBestEffortAsync(CancellationToken.None);
    }

    private void StartRuntime(Runtime runtime)
    {
        if (_hostCancellation is null || _hostCancellation.IsCancellationRequested || runtime.IsRunning) return;
        runtime.Start(token => RunAsync(runtime, token), _hostCancellation.Token);
    }

    private async Task RunAsync(Runtime runtime, CancellationToken cancellationToken)
    {
        var backoff = new ReconnectBackoff();
        var firstAttempt = true;
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested && runtime.Configuration.Enabled)
        {
            var beforeAttempt = runtime.Snapshot();
            if (beforeAttempt.State != ProviderLifecycleState.Connected)
                runtime.SetState(firstAttempt ? ProviderLifecycleState.Connecting : ProviderLifecycleState.Reconnecting, beforeAttempt.LastError);
            try
            {
                var result = await _connector.ConnectAndDiscoverAsync(runtime.Configuration, cancellationToken);
                runtime.SetConnected(result);
                await RecordSourcesBestEffortAsync(cancellationToken);
                backoff.Reset();
                consecutiveFailures = 0;
                firstAttempt = false;
                if (beforeAttempt.State != ProviderLifecycleState.Connected)
                    _logger.LogInformation("Provider {ProviderId} connected; discovered {SourceCount} sources", runtime.Configuration.Id, result.Sources.Count);
                await _delay.DelayAsync(ConnectedProbeInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                var error = ConciseError(ex);
                runtime.SetState(ProviderLifecycleState.Reconnecting, error);
                var retryDelay = backoff.NextDelay();
                runtime.RecordReconnect(retryDelay);
                consecutiveFailures++;
                if (consecutiveFailures <= 4 || (consecutiveFailures - 4) % 12 == 0)
                    _logger.LogWarning("Provider {ProviderId} unavailable ({Reason}); retrying in {RetryDelay}", runtime.Configuration.Id, error, retryDelay);
                try { await _delay.DelayAsync(retryDelay, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                firstAttempt = false;
            }
        }
        if (!runtime.Configuration.Enabled) runtime.SetState(ProviderLifecycleState.Disabled, null);
        else if (runtime.Snapshot().State != ProviderLifecycleState.Disabled) runtime.SetState(ProviderLifecycleState.Disconnected, runtime.Snapshot().LastError);
    }

    private static string ConciseError(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 240 ? message : message[..237] + "...";
    }

    private async Task RecordSourcesBestEffortAsync(CancellationToken cancellationToken)
    {
        if (_products is null || !_products.CanPersist) return;
        try { await _products.RecordSourcesAsync(GetStatuses(), cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not persist the last-known source catalog");
        }
    }

    private ProviderConfiguration FindConfiguration(string providerId) =>
        _configuration.Current.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Provider '{providerId}' was not found.");

    private Runtime GetRuntime(string providerId) => _runtimes.TryGetValue(providerId, out var runtime)
        ? runtime : throw new KeyNotFoundException($"Provider '{providerId}' was not found.");

    public void Dispose()
    {
        _hostCancellation?.Dispose();
        foreach (var runtime in _runtimes.Values) runtime.Dispose();
    }

    private sealed class Runtime : IDisposable
    {
        private readonly object _gate = new();
        private ProviderConfiguration _configuration;
        private ProviderLifecycleState _state;
        private string? _lastError;
        private DateTimeOffset? _lastConnectedAt;
        private string? _revision;
        private IReadOnlyList<ProviderSource> _sources = [];
        private CancellationTokenSource? _cancellation;
        private Task? _task;
        private long _reconnectCount;
        private TimeSpan? _retryDelay;

        public Runtime(ProviderConfiguration configuration)
        {
            _configuration = configuration;
            _state = configuration.Enabled ? ProviderLifecycleState.Disconnected : ProviderLifecycleState.Disabled;
        }

        public ProviderConfiguration Configuration { get { lock (_gate) return _configuration; } }
        public bool IsRunning { get { lock (_gate) return _task is { IsCompleted: false }; } }

        public void UpdateConfiguration(ProviderConfiguration configuration) { lock (_gate) _configuration = configuration; }

        public void Start(Func<CancellationToken, Task> body, CancellationToken hostCancellation)
        {
            lock (_gate)
            {
                if (_task is { IsCompleted: false }) return;
                _cancellation?.Dispose();
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellation);
                _task = Task.Run(() => body(_cancellation.Token), CancellationToken.None);
            }
        }

        public async Task StopAsync()
        {
            Task? task;
            lock (_gate) { _cancellation?.Cancel(); task = _task; }
            if (task is not null)
            {
                try { await task; } catch (OperationCanceledException) { }
            }
        }

        public void SetState(ProviderLifecycleState state, string? error)
        {
            lock (_gate) { _state = state; _lastError = error; }
        }

        public void SetConnected(ProviderConnectionResult result)
        {
            lock (_gate)
            {
                _state = ProviderLifecycleState.Connected;
                _lastError = null;
                _lastConnectedAt = DateTimeOffset.UtcNow;
                _revision = result.DiscoveryRevision;
                _sources = result.Sources;
                _retryDelay = null;
            }
        }

        public void RecordReconnect(TimeSpan retryDelay)
        {
            lock (_gate) { _reconnectCount++; _retryDelay = retryDelay; }
        }

        public void UpdateSourceMetadata(string sourceId, int channelCount, int sampleRateHz)
        {
            lock (_gate)
            {
                if (_sources.Count == 0) return;
                var hasAnyMatch = false;
                var updated = new List<ProviderSource>(_sources.Count);
                foreach (var source in _sources)
                {
                    if (source.SourceId.Equals(sourceId, StringComparison.Ordinal))
                    {
                        hasAnyMatch = true;
                        updated.Add(source with { ChannelCount = channelCount, SampleRateHz = sampleRateHz });
                    }
                    else
                    {
                        updated.Add(source);
                    }
                }

                if (hasAnyMatch) _sources = updated;
            }
        }

        public ProviderStatus Snapshot()
        {
            lock (_gate) return new(_configuration.Id, _configuration.FriendlyName, _configuration.Endpoint,
                _configuration.Enabled, _state, _lastError, _lastConnectedAt, _revision, _sources.ToArray(),
                _reconnectCount, _retryDelay?.TotalMilliseconds);
        }

        public void Dispose() => _cancellation?.Dispose();
    }
}
