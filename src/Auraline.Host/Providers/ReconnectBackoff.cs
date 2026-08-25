namespace Auraline.Host.Providers;

public sealed class ReconnectBackoff
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private int _index;

    public TimeSpan NextDelay()
    {
        var delay = Steps[Math.Min(_index, Steps.Length - 1)];
        if (_index < Steps.Length - 1) _index++;
        return delay;
    }

    public void Reset() => _index = 0;
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
