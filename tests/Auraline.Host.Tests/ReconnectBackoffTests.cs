using Auraline.Host.Providers;

namespace Auraline.Host.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void ProgressionCapsAndResetRestartsAtFiveHundredMilliseconds()
    {
        var policy = new ReconnectBackoff();

        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(5), policy.NextDelay());

        policy.Reset();
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.NextDelay());
    }
}
