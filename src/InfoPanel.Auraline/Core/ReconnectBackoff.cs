namespace InfoPanel.Auraline.Core;

internal sealed class ReconnectBackoff
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private int _attempt;

    public TimeSpan Next()
    {
        var delay = Delays[Math.Min(_attempt, Delays.Length - 1)];
        _attempt++;
        return delay;
    }

    public void Reset() => _attempt = 0;
}
