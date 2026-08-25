using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Auraline.Host.Waveform;

namespace Auraline.Host.Diagnostics;

public sealed record WaveformSelfTestEvidence(string StreamId, string SourceId, int Channels, int SampleRateHz, ulong Sequence);

public interface IWaveformSelfTester
{
    Task<WaveformSelfTestEvidence> OpenAndDecodeAsync(string providerEndpoint, CancellationToken cancellationToken);
}

public sealed class WaveformSelfTestClient : IWaveformSelfTester
{
    public async Task<WaveformSelfTestEvidence> OpenAndDecodeAsync(string providerEndpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(BuildUri(providerEndpoint), timeout.Token);
        WaveformStreamStartedEvent? started = null;
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var (type, payload) = await ReceiveAsync(socket, timeout.Token);
                if (type == WebSocketMessageType.Close) throw new IOException("Waveform self-test stream closed before a frame was decoded.");
                if (type == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(payload);
                    using var document = JsonDocument.Parse(json);
                    var eventType = document.RootElement.TryGetProperty("type", out var value) ? value.GetString() : null;
                    if (eventType == "stream_started") started = WaveformProtocolParser.ParseStreamStarted(json);
                    else if (eventType == "stream_error")
                    {
                        var error = WaveformProtocolParser.ParseStreamError(json);
                        throw new IOException($"Provider waveform error '{error.Kind}' ({error.RetryHint}).");
                    }
                    else if (eventType == "stream_stopped") throw new IOException($"Waveform self-test stopped: {WaveformProtocolParser.ParseStreamStopped(json).Reason}.");
                }
                else if (type == WebSocketMessageType.Binary && started is not null)
                {
                    var frame = WaveformProtocolParser.ParseWaveformBinary(payload, started.ChannelCount);
                    return new(started.StreamId, started.SourceId, started.ChannelCount, started.SampleRateHz, frame.Sequence);
                }
            }
            throw new TimeoutException("Waveform self-test did not receive a frame within five seconds.");
        }
        finally
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "self-test complete", CancellationToken.None);
        }
    }

    private static Uri BuildUri(string endpoint)
    {
        var baseUri = new Uri(endpoint, UriKind.Absolute);
        return new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/v1/waveform",
            Query = "source=default-playback"
        }.Uri;
    }

    private static async Task<(WebSocketMessageType Type, byte[] Payload)> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(8192);
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return (WebSocketMessageType.Close, []);
            memory.Write(buffer, 0, result.Count);
            if (memory.Length > WaveformProtocolParser.MaxBinaryPayloadBytes) throw new InvalidDataException("Waveform self-test message exceeded its size limit.");
        } while (!result.EndOfMessage);
        return (result.MessageType, memory.ToArray());
    }
}
