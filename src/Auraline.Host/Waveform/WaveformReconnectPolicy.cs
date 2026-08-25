using System;

namespace Auraline.Host.Waveform;

public sealed class WaveformReconnectPolicy
{
    public const int UnavailableStateThreshold = 6;

    private static readonly TimeSpan[] DelaySteps =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private int _attempt;
    private int _retryCount;
    private bool _doNotRetry;
    private WaveformRetryHint _lastHint = WaveformRetryHint.Unknown;
    private readonly object _gate = new();

    public int AttemptCount
    {
        get
        {
            lock (_gate) return _retryCount;
        }
    }

    public string LastHint
    {
        get
        {
            lock (_gate) return _lastHint.ToString();
        }
    }

    public bool HasExceededUnavailableThreshold
    {
        get
        {
            lock (_gate) return _retryCount >= UnavailableStateThreshold;
        }
    }

    public bool IsRetrySuppressed
    {
        get
        {
            lock (_gate) return _doNotRetry;
        }
    }

    public TimeSpan NextDelay(WaveformRetryHint hint)
    {
        lock (_gate)
        {
            _lastHint = hint;
            if (_doNotRetry) return Timeout.InfiniteTimeSpan;
            _retryCount++;
            if (hint == WaveformRetryHint.DoNotRetry)
            {
                _doNotRetry = true;
                return Timeout.InfiniteTimeSpan;
            }

            _doNotRetry = false;
            if (hint == WaveformRetryHint.RetryNow) return TimeSpan.Zero;

            var delay = DelaySteps[Math.Min(_attempt, DelaySteps.Length - 1)];
            if (_attempt < DelaySteps.Length - 1) _attempt++;
            return delay;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _attempt = 0;
            _retryCount = 0;
            _doNotRetry = false;
            _lastHint = WaveformRetryHint.Unknown;
        }
    }

    public void MarkConnected()
    {
        Reset();
    }
}
