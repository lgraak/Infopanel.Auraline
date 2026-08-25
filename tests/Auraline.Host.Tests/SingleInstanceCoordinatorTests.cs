using Auraline.Host.Platform.Windows;

namespace Auraline.Host.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task DuplicateSignalsPrimaryToOpen()
    {
        var name = "Auraline.Test." + Guid.NewGuid().ToString("N");
        var identity = "test-user-" + Guid.NewGuid().ToString("N");
        await using var primary = new SingleInstanceCoordinator(name, identity);
        await using var duplicate = new SingleInstanceCoordinator(name, identity);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(() => opened.TrySetResult());

        Assert.True(primary.IsPrimary);
        Assert.False(duplicate.IsPrimary);
        Assert.True(await duplicate.SignalOpenAsync(TimeSpan.FromSeconds(2)));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
