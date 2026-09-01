using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Auraline.Host.Configuration;
using Auraline.Host.Diagnostics;
using Auraline.Host.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auraline.Host.Waveform;

public sealed class WaveformEngineService(
    ConfigurationStore configuration,
    ProviderManager providerManager,
    WaveformProcessor processor,
    WaveformRenderer renderer,
    WaveformReconnectPolicy reconnectPolicy,
    ILogger<WaveformEngineService> logger,
    StallObservability observability) : IHostedService, IAsyncDisposable, IWaveformEngineStatusProvider, IWaveformRenderStateSource
{
    private const int DefaultRenderWidth = 320;
    private const int DefaultRenderHeight = 120;
    private const int DefaultTargetFps = 30;
    private const double IdleThreshold = 0.004;
    private const int IdleWindow = 12;
    private static readonly string LogicalSourceIntent = "default-playback";
    private static readonly TimeSpan DisabledRetryPollDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DisabledRetryMaxWait = TimeSpan.FromSeconds(12);

    private readonly object _gate = new();
    private CancellationTokenSource? _hostCancellation;
    private Task? _loopTask;

    private WaveformVisualizationState _visualState = WaveformVisualizationState.Unavailable;
    private string? _lastError;
    private string? _retryState;
    private DateTimeOffset? _latestFrameTime;
    private DateTimeOffset? _streamStartedAt;
    private string? _streamId;
    private string? _sourceId;
    private int? _channelCount;
    private int? _sampleRate;
    private string? _sampleFormat;
    private ulong? _expectedSequence;
    private ulong? _expectedFrameIndex;
    private bool _hasActiveStream;
    private int _quietFrames;
    private bool _disposed;

    private long _streamStarts;
    private long _streamStops;
    private long _malformedFrames;
    private long _waveformFrames;
    private long _reconnectAttempts;
    private long _renderedFrames;
    private double? _lastRenderDurationMs;
    private double? _averageRenderDurationMs;
    private WaveformRenderedFrame? _latestFrame;
    private WaveformProcessedFrame? _latestProcessedFrame;

    public WaveformEngineHealth GetHealth()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var latestAge = _latestFrameTime is null ? null : (double?)(now - _latestFrameTime.Value).TotalMilliseconds;
            var streamUptime = _streamStartedAt is null ? null : (double?)(now - _streamStartedAt.Value).TotalMilliseconds;
            return new(
                _visualState.ToString(),
                LogicalSourceIntent,
                HostConfiguration.DefaultProviderId,
                _streamId,
                _sourceId,
                _channelCount,
                _sampleRate,
                _sampleFormat,
                _reconnectAttempts,
                _streamStarts,
                _streamStops,
                _malformedFrames,
                _waveformFrames,
                latestAge,
                streamUptime,
                _lastRenderDurationMs,
                _averageRenderDurationMs,
                DefaultTargetFps,
                _retryState ?? WaveformRetryHint.Unknown.ToString(),
                _renderedFrames,
                _lastError);
        }
    }

    public WaveformRenderedFrame? GetLatestFrame()
    {
        lock (_gate) return _latestFrame;
    }

    public WaveformRenderSnapshot CaptureRenderState()
    {
        lock (_gate)
        {
            var processed = _latestProcessedFrame ?? new WaveformProcessedFrame(
                "no-stream", 0, 0, 0, [0f], [[0f]]);
            return new WaveformRenderSnapshot(processed, _visualState);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loopTask is not null) return Task.CompletedTask;
        _hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_hostCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hostCancellation is null) return;

        _hostCancellation.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            _loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _hostCancellation?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        SetVisualState(WaveformVisualizationState.Unavailable, "initializing");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!TryResolveProviderEndpoint(out var provider))
            {
                SetVisualState(WaveformVisualizationState.Unavailable, "provider endpoint unavailable");
                await WaitForRetryAsync(cancellationToken, WaveformRetryHint.WaitForSource, TimeSpan.FromSeconds(1));
                continue;
            }

            if (!IsProviderEnabled(provider.ProviderId))
            {
                SetVisualState(WaveformVisualizationState.Unavailable, "provider disabled");
                await WaitForProviderEnabled(provider.ProviderId, cancellationToken);
                continue;
            }

            using var socket = new ClientWebSocket();
            try
            {
                var wsUri = BuildWaveformUri(provider.Endpoint);
                observability.Record("waveform_open_attempt", streamId: _streamId, reason: wsUri.AbsolutePath);
                logger.LogInformation("Opening waveform websocket {Uri}", wsUri);
                SetVisualState(WaveformVisualizationState.Reconnecting, "connecting");

                reconnectPolicy.Reset();
                lock (_gate)
                {
                    _reconnectAttempts = 0;
                    _lastError = null;
                    _retryState = null;
                }
                await socket.ConnectAsync(wsUri, cancellationToken);

                SetVisualState(WaveformVisualizationState.Reconnecting, "stream not started");
                var terminalHint = await ConsumeStreamAsync(socket, provider.ProviderId, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (terminalHint.HasValue)
                {
                    var hint = NormalizeRetryHint(terminalHint.Value);
                    if (hint == WaveformRetryHint.DoNotRetry) { SetVisualState(WaveformVisualizationState.Unavailable, "retry disabled by provider"); await WaitForRetryAsync(cancellationToken, hint, Timeout.InfiniteTimeSpan); }
                    else { await WaitForRetryAsync(cancellationToken, hint, null); }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Waveform stream loop failed for {ProviderId}", provider.ProviderId);
                string? lastError;
                lock (_gate) { _lastError = lastError = ex.Message; }
                var delayHint = ex is WaveformProtocolException ? WaveformRetryHint.RetryLater : WaveformRetryHint.WaitForSource;
                var delay = reconnectPolicy.NextDelay(delayHint);
                lock (_gate) { _reconnectAttempts = reconnectPolicy.AttemptCount; }
                if (reconnectPolicy.IsRetrySuppressed)
                {
                    SetVisualState(WaveformVisualizationState.Unavailable, lastError);
                    await WaitForProviderEnabled(provider.ProviderId, cancellationToken);
                }
                else
                {
                    SetVisualState(WaveformVisualizationState.Reconnecting, lastError);
                    await WaitForRetryAsync(cancellationToken, delayHint, delay);
                }
            }
        }
    }

    private async Task<WaveformRetryHint?> ConsumeStreamAsync(ClientWebSocket socket, string providerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var (messageType, payload) = await ReceiveMessageAsync(socket, cancellationToken);
            if (messageType == WebSocketMessageType.Close || payload.Length == 0)
            {
                lock (_gate)
                {
                    if (_hasActiveStream) _streamStops++;
                    _lastError = "websocket closed";
                }
                observability.RecordWaveformStopped(_streamId, "websocket closed");
                CloseStream();
                SetVisualState(WaveformVisualizationState.Reconnecting, "websocket closed");
                return WaveformRetryHint.WaitForSource;
            }

            if (messageType == WebSocketMessageType.Text)
            {
                var hint = HandleTextEvent(Encoding.UTF8.GetString(payload));
                if (hint.HasValue) return hint;
            }
            else if (messageType == WebSocketMessageType.Binary)
            {
                HandleBinaryEvent(payload);
            }

            if (!IsProviderEnabled(providerId))
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "provider disabled", cancellationToken);
                return WaveformRetryHint.WaitForSource;
            }
        }

        return null;
    }

    private async Task<(WebSocketMessageType messageType, byte[] payload)> ReceiveMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream(8192);
        WebSocketReceiveResult result;
        var buffer = new byte[8192];
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return (WebSocketMessageType.Close, []);
            if (result.Count > 0) ms.Write(buffer, 0, result.Count);
            if (ms.Length > WaveformProtocolParser.MaxBinaryPayloadBytes + 64)
                throw new InvalidOperationException("Waveform message exceeded protocol-safe size.");
        } while (!result.EndOfMessage);

        return (result.MessageType, ms.ToArray());
    }

    private WaveformRetryHint? HandleTextEvent(string json)
    {
        try
        {
            var eventType = ParseEventType(json);
            switch (eventType)
            {
                case "stream_started":
                    StartStream(WaveformProtocolParser.ParseStreamStarted(json));
                    return null;

                case "stream_stopped":
                    var stopped = WaveformProtocolParser.ParseStreamStopped(json);
                    if (IsCurrentStream(stopped.StreamId))
                    {
                        lock (_gate) _streamStops++;
                        observability.RecordWaveformStopped(stopped.StreamId, stopped.Reason);
                        CloseStream();
                        SetVisualState(WaveformVisualizationState.Reconnecting, stopped.Reason);
                        return WaveformRetryHint.WaitForSource;
                    }
                    return null;

                case "stream_error":
                    var error = WaveformProtocolParser.ParseStreamError(json);
                    lock (_gate)
                    {
                        if (_hasActiveStream) _streamStops++;
                        _retryState = error.RetryHint.ToString();
                        _lastError = $"{error.Kind}:{error.ScopeType}:{error.ScopeId}";
                    }
                    observability.RecordWaveformStopped(_streamId, $"{error.Kind}:{error.ScopeType}:{error.ScopeId}");
                    CloseStream();
                    SetVisualState(WaveformVisualizationState.Reconnecting, error.Kind);
                    if (error.RetryHint == WaveformRetryHint.DoNotRetry)
                        return WaveformRetryHint.DoNotRetry;
                    return NormalizeRetryHint(error.RetryHint);

                default:
                    lock (_gate) _malformedFrames++;
                    return null;
            }
        }
        catch (WaveformProtocolException ex)
        {
            lock (_gate)
            {
                _malformedFrames++;
                _lastError = ex.Message;
            }
            logger.LogWarning(ex, "Malformed waveform text event");
            throw;
        }
    }

    private void HandleBinaryEvent(byte[] payload)
    {
        if (!_hasActiveStream || _channelCount is null || _sampleRate is null || _sampleFormat is null)
            throw new InvalidOperationException("Binary frame received before stream_started.");

        var frame = WaveformProtocolParser.ParseWaveformBinary(payload, _channelCount.Value);
        ValidateContinuity(frame);
        var processed = processor.ProcessFrame(frame, _streamId!);
        lock (_gate) { _waveformFrames++; _latestFrameTime = DateTimeOffset.UtcNow; _latestProcessedFrame = processed; }

        EvaluateAudioState(processor.CurrentMaxMagnitude);
        RenderFrame(processed, frame.Sequence);
    }

    private void RenderFrame(WaveformProcessedFrame processed, ulong sequence)
    {
        var sw = Stopwatch.StartNew();
        var visualState = _visualState;
        var rendered = renderer.Render(
            processed,
            visualState,
            DefaultRenderWidth,
            DefaultRenderHeight,
            sequence,
            DateTimeOffset.UtcNow,
            DefaultTargetFps,
            (int)(sequence % int.MaxValue));
        sw.Stop();

        lock (_gate)
        {
            _latestFrame = rendered;
            _renderedFrames++;
            _lastRenderDurationMs = sw.Elapsed.TotalMilliseconds;
            _averageRenderDurationMs = _averageRenderDurationMs is null
                ? _lastRenderDurationMs
                : (_averageRenderDurationMs.Value * 0.8) + (_lastRenderDurationMs.Value * 0.2);
        }
    }

    private void EvaluateAudioState(double maxMagnitude)
    {
        lock (_gate)
        {
            if (_visualState == WaveformVisualizationState.Reconnecting || _visualState == WaveformVisualizationState.Unavailable) return;

            if (maxMagnitude > IdleThreshold)
            {
                _quietFrames = 0;
                _visualState = WaveformVisualizationState.Active;
                return;
            }

            _quietFrames++;
            if (_quietFrames >= IdleWindow) _visualState = WaveformVisualizationState.Idle;
            else if (_visualState != WaveformVisualizationState.Idle) _visualState = WaveformVisualizationState.Active;
        }
    }

    private void ValidateContinuity(WaveformBinaryFrame frame)
    {
        if (_expectedSequence is not null && frame.Sequence != _expectedSequence.Value)
            throw new InvalidOperationException("Waveform sequence was discontinuous.");
        if (_expectedFrameIndex is not null && frame.FrameIndex != _expectedFrameIndex.Value)
            throw new InvalidOperationException("Waveform frame index was discontinuous.");

        _expectedSequence = frame.Sequence + 1;
        _expectedFrameIndex = frame.FrameIndex + frame.FrameCount;
    }

    private void StartStream(WaveformStreamStartedEvent started)
    {
        reconnectPolicy.MarkConnected();

        lock (_gate)
        {
            _streamId = started.StreamId;
            _sourceId = started.SourceId;
            _channelCount = started.ChannelCount;
            _sampleRate = started.SampleRateHz;
            _sampleFormat = started.SampleFormat;
            _streamStartedAt = DateTimeOffset.UtcNow;
            _latestFrameTime = _streamStartedAt;
            _expectedSequence = null;
            _expectedFrameIndex = null;
            _quietFrames = 0;
            _retryState = null;
            _hasActiveStream = true;
            _streamStarts++;
            _reconnectAttempts = 0;
            _lastError = null;
            _visualState = WaveformVisualizationState.Active;
            processor.Reset();
        }

        providerManager.UpdateSourceMetadata(HostConfiguration.DefaultProviderId, started.SourceId, started.ChannelCount, started.SampleRateHz);
        observability.RecordWaveformStarted(started.StreamId, started.SourceId);
        logger.LogInformation("Waveform stream started StreamId={StreamId} SourceId={SourceId}", _streamId, _sourceId);
    }

    private void CloseStream()
    {
        lock (_gate)
        {
            _streamId = null;
            _sourceId = null;
            _sampleRate = null;
            _sampleFormat = null;
            _channelCount = null;
            _streamStartedAt = null;
            _latestFrameTime = null;
            _expectedSequence = null;
            _expectedFrameIndex = null;
            _hasActiveStream = false;
            _quietFrames = 0;
        }
    }

    private static WaveformRetryHint NormalizeRetryHint(WaveformRetryHint hint) =>
        hint switch
        {
            WaveformRetryHint.RetryNow => WaveformRetryHint.RetryNow,
            WaveformRetryHint.WaitForSource => WaveformRetryHint.WaitForSource,
            WaveformRetryHint.RetryLater => WaveformRetryHint.WaitForSource,
            WaveformRetryHint.RequestPermission => WaveformRetryHint.WaitForSource,
            WaveformRetryHint.ChangeFormat => WaveformRetryHint.WaitForSource,
            WaveformRetryHint.Unknown => WaveformRetryHint.WaitForSource,
            _ => WaveformRetryHint.WaitForSource
        };

    private async Task WaitForRetryAsync(CancellationToken cancellationToken, WaveformRetryHint hint, TimeSpan? explicitDelay)
    {
        var delay = explicitDelay ?? reconnectPolicy.NextDelay(hint);
        if (delay == Timeout.InfiniteTimeSpan || reconnectPolicy.IsRetrySuppressed || hint == WaveformRetryHint.DoNotRetry)
        {
            SetVisualState(WaveformVisualizationState.Unavailable, "retry suspended");
            delay = DisabledRetryMaxWait;
        }
        else if (reconnectPolicy.HasExceededUnavailableThreshold)
        {
            SetVisualState(WaveformVisualizationState.Unavailable, "retry delay capped");
        }

        lock (_gate) _reconnectAttempts = reconnectPolicy.AttemptCount;
        observability.Record("waveform_reconnect_wait", streamId: _streamId, durationMs: delay == Timeout.InfiniteTimeSpan ? null : delay.TotalMilliseconds,
            reason: hint.ToString());
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }

    private async Task WaitForProviderEnabled(string providerId, CancellationToken cancellationToken)
    {
        var waited = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested && !IsProviderEnabled(providerId) && waited < TimeSpan.FromMinutes(5))
        {
            await Task.Delay(DisabledRetryPollDelay, cancellationToken);
            waited += DisabledRetryPollDelay;
        }
    }

    private static string ParseEventType(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new WaveformProtocolException("Waveform event did not define a valid type.");
        return type.GetString() ?? "unknown";
    }

    private bool IsCurrentStream(string streamId) =>
        string.Equals(_streamId, streamId, StringComparison.Ordinal);

    private bool IsProviderEnabled(string providerId) =>
        configuration.Current.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))?.Enabled ?? false;

    private bool TryResolveProviderEndpoint(out (string ProviderId, string Endpoint) provider)
    {
        provider = default;
        var configured = configuration.Current.Providers
            .FirstOrDefault(p => p.Id.Equals(HostConfiguration.DefaultProviderId, StringComparison.OrdinalIgnoreCase));
        if (configured is null) return false;
        if (!Uri.TryCreate(configured.Endpoint, UriKind.Absolute, out var baseUri))
            return false;

        provider = (configured.Id, configured.Endpoint);
        return true;
    }

    private static Uri BuildWaveformUri(string endpoint)
    {
        var baseUri = new Uri(endpoint);
        var uriBuilder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/v1/waveform",
            Query = $"source={Uri.EscapeDataString(LogicalSourceIntent)}"
        };
        return uriBuilder.Uri;
    }

    private void SetVisualState(WaveformVisualizationState state, string? reason)
    {
        lock (_gate)
        {
            _visualState = state;
            if (reason is not null) _lastError = reason;
            if (state is not WaveformVisualizationState.Reconnecting and not WaveformVisualizationState.Unavailable)
                _retryState = state.ToString();
        }
    }
}
