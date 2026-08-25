using Auraline.Host.Waveform;

namespace Auraline.Host.Tests;

public sealed class WaveformReconnectPolicyTests
{
    [Fact]
    public void NextDelayProgressesThenCaps()
    {
        var policy = new WaveformReconnectPolicy();

        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.NextDelay(WaveformRetryHint.WaitForSource));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay(WaveformRetryHint.WaitForSource));
        Assert.Equal(TimeSpan.FromSeconds(2), policy.NextDelay(WaveformRetryHint.WaitForSource));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextDelay(WaveformRetryHint.WaitForSource));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextDelay(WaveformRetryHint.WaitForSource));
    }

    [Fact]
    public void RetryNowReturnsImmediateDelayAndKeepsBaseOffset()
    {
        var policy = new WaveformReconnectPolicy();
        Assert.Equal(TimeSpan.Zero, policy.NextDelay(WaveformRetryHint.RetryNow));
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.NextDelay(WaveformRetryHint.WaitForSource));
    }

    [Fact]
    public void ResetClearsAttemptsAndSuppression()
    {
        var policy = new WaveformReconnectPolicy();
        policy.NextDelay(WaveformRetryHint.WaitForSource);
        Assert.Equal(1, policy.AttemptCount);

        policy.NextDelay(WaveformRetryHint.DoNotRetry);
        Assert.True(policy.IsRetrySuppressed);
        Assert.Equal(Timeout.InfiniteTimeSpan, policy.NextDelay(WaveformRetryHint.WaitForSource));
        Assert.Equal(2, policy.AttemptCount);

        policy.Reset();
        Assert.False(policy.IsRetrySuppressed);
        Assert.Equal(0, policy.AttemptCount);
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.NextDelay(WaveformRetryHint.WaitForSource));
    }

    [Fact]
    public void AttemptCountCanReachUnavailableThreshold()
    {
        var policy = new WaveformReconnectPolicy();

        for (var i = 0; i < WaveformReconnectPolicy.UnavailableStateThreshold; i++)
            policy.NextDelay(WaveformRetryHint.WaitForSource);

        Assert.True(policy.HasExceededUnavailableThreshold);
    }
}
