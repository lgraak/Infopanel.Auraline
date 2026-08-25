using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Auraline.Host.Lifecycle;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Semaphore _semaphore;
    private readonly bool _ownsSemaphore;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    public SingleInstanceCoordinator(string applicationName, string? userIdentity = null)
    {
        var identity = userIdentity ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        var suffix = $"{applicationName}.{hash}";
        _pipeName = suffix;
        _semaphore = new Semaphore(1, 1, $@"Local\{suffix}");
        _ownsSemaphore = _semaphore.WaitOne(0);
    }

    public bool IsPrimary => _ownsSemaphore;

    public void StartListening(Action openAction)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Only the primary instance can listen.");
        _listener = Task.Run(() => ListenAsync(openAction, _cancellation.Token));
    }

    public async Task<bool> SignalOpenAsync(TimeSpan timeout)
    {
        if (IsPrimary) return false;
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutSource.Token);
            await client.WriteAsync("open"u8.ToArray(), timeoutSource.Token);
            await client.FlushAsync(timeoutSource.Token);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException) { return false; }
    }

    private async Task ListenAsync(Action openAction, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[16];
                var count = await server.ReadAsync(buffer, cancellationToken);
                if (Encoding.UTF8.GetString(buffer, 0, count).Equals("open", StringComparison.Ordinal)) openAction();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_listener is not null)
        {
            try { await _listener; } catch (OperationCanceledException) { }
        }
        if (_ownsSemaphore) _semaphore.Release();
        _semaphore.Dispose();
        _cancellation.Dispose();
    }
}
