namespace Auraline.Host.Lifecycle;

public interface ISingleInstanceCoordinator : IAsyncDisposable
{
    bool IsPrimary { get; }
    void StartListening(Action openAction);
    Task<bool> SignalOpenAsync(TimeSpan timeout);
}
