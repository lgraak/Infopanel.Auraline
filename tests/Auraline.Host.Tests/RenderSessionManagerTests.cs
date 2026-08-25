using Auraline.Contracts;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraline.Host.Tests;

public sealed class RenderSessionManagerTests
{
    [Fact]
    public async Task CreatesLazilySharesCompatibleRequestsAndSeparatesDimensions()
    {
        var fixture = new Fixture();
        await using var manager = fixture.CreateManager();
        Assert.Empty(manager.GetDiagnostics().Sessions);

        var first = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        var second = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        var otherSize = manager.Attach(AuralineProfiles.DefaultProfileId, 640, 240, 30, ContractVersion.Current);

        Assert.Equal(first.Session.SessionId, second.Session.SessionId);
        Assert.NotEqual(first.Lease.LeaseId, second.Lease.LeaseId);
        Assert.NotEqual(first.Session.SessionId, otherSize.Session.SessionId);
        Assert.Equal(2, manager.GetDiagnostics().ActiveSessionCount);
        Assert.Equal(3, manager.GetDiagnostics().TotalConsumerLeases);
        Assert.Equal(2, fixture.TransportFactory.CreatedCount);
    }

    [Fact]
    public async Task DetachStartsGraceAndReattachCancelsTeardown()
    {
        var fixture = new Fixture();
        await using var manager = fixture.CreateManager();
        var first = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);

        Assert.True(manager.Detach(first.Session.SessionId, first.Lease.LeaseId));
        Assert.Equal(RenderSessionState.Grace.ToString(), manager.GetDiagnostic(first.Session.SessionId)!.State);
        fixture.Clock.Advance(TimeSpan.FromSeconds(14));
        await manager.SweepAsync();
        var reattached = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);

        Assert.Equal(first.Session.SessionId, reattached.Session.SessionId);
        Assert.Equal(RenderSessionState.Active.ToString(), manager.GetDiagnostic(first.Session.SessionId)!.State);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        await manager.SweepAsync();
        Assert.NotNull(manager.GetDiagnostic(first.Session.SessionId));

        Assert.True(manager.Detach(reattached.Session.SessionId, reattached.Lease.LeaseId));
        fixture.Clock.Advance(TimeSpan.FromSeconds(16));
        await manager.SweepAsync();
        Assert.Null(manager.GetDiagnostic(first.Session.SessionId));
        Assert.Equal(1, manager.GetDiagnostics().TeardownCount);
    }

    [Fact]
    public async Task StaleLeaseExpiresWithoutAffectingAnotherConsumer()
    {
        var fixture = new Fixture();
        await using var manager = fixture.CreateManager();
        var stale = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        var renewed = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);

        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.NotNull(manager.Heartbeat(renewed.Session.SessionId, renewed.Lease.LeaseId));
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await manager.SweepAsync();

        var diagnostic = manager.GetDiagnostic(stale.Session.SessionId);
        Assert.NotNull(diagnostic);
        Assert.Equal(1, diagnostic.ConsumerCount);
        Assert.Equal(RenderSessionState.Active.ToString(), diagnostic.State);
        Assert.Null(manager.Heartbeat(stale.Session.SessionId, stale.Lease.LeaseId));
    }

    [Fact]
    public async Task IdleLruSessionIsEvictedBeforeActiveSessions()
    {
        var fixture = new Fixture(cap: 2);
        await using var manager = fixture.CreateManager();
        var oldest = manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        manager.Detach(oldest.Session.SessionId, oldest.Lease.LeaseId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var newer = manager.Attach(AuralineProfiles.DefaultProfileId, 400, 160, 30, ContractVersion.Current);
        manager.Detach(newer.Session.SessionId, newer.Lease.LeaseId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var admitted = manager.Attach(AuralineProfiles.DefaultProfileId, 640, 240, 30, ContractVersion.Current);

        Assert.Null(manager.GetDiagnostic(oldest.Session.SessionId));
        Assert.NotNull(manager.GetDiagnostic(newer.Session.SessionId));
        Assert.NotNull(manager.GetDiagnostic(admitted.Session.SessionId));
        Assert.Equal(1, manager.GetDiagnostics().EvictionCount);
    }

    [Fact]
    public async Task RejectsOnlyWhenAllCapacityIsActiveAndValidatesRequests()
    {
        var fixture = new Fixture(cap: 1);
        await using var manager = fixture.CreateManager();
        manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 60, ContractVersion.Current);

        Assert.Throws<RenderSessionCapacityException>(() =>
            manager.Attach(AuralineProfiles.DefaultProfileId, 640, 240, 30, ContractVersion.Current));
        Assert.Throws<KeyNotFoundException>(() =>
            manager.Attach("missing", 320, 120, 30, ContractVersion.Current));
        Assert.Throws<NotSupportedException>(() =>
            manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, new ContractVersion(2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.Attach(AuralineProfiles.DefaultProfileId, 8, 120, 30, ContractVersion.Current));
        Assert.Equal(1, manager.GetDiagnostics().RejectedSessionCount);
    }

    [Fact]
    public async Task SharedConsumersStartOnlyOneSchedulerAndShutdownCancelsIt()
    {
        var fixture = new Fixture();
        var manager = fixture.CreateManager();
        manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        manager.Attach(AuralineProfiles.DefaultProfileId, 320, 120, 30, ContractVersion.Current);

        Assert.True(SpinWait.SpinUntil(() => fixture.TransportFactory.PublishedCount > 0, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, fixture.TransportFactory.CreatedCount);
        await manager.DisposeAsync();
        Assert.Equal(1, fixture.TransportFactory.DisposedCount);
    }

    [Fact]
    public void SchedulerSkipsMissedDeadlinesInsteadOfAccumulatingBacklog()
    {
        var interval = TimeSpan.FromSeconds(1d / 30);
        var previous = DateTimeOffset.UnixEpoch;
        var now = previous + TimeSpan.FromSeconds(2);

        var next = RenderSessionManager.CalculateNextDeadline(previous, interval, now);

        Assert.Equal(now + interval, next);
    }

    private sealed class Fixture(int cap = 32)
    {
        public FakeClock Clock { get; } = new();
        public FakeTransportFactory TransportFactory { get; } = new();

        public RenderSessionManager CreateManager() => new(
            TransportFactory,
            new FakeWaveformSource(),
            new WaveformRenderer(),
            Clock,
            new RenderSessionOptions(cap, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1)),
            NullLogger<RenderSessionManager>.Instance);
    }

    private sealed class FakeClock : IRenderSessionClock
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan duration) => _now += duration;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FakeWaveformSource : IWaveformRenderStateSource
    {
        private static readonly WaveformProcessedFrame Frame = new("test", 1, 1, 1, [0.5f, -0.5f], [[0.5f, -0.5f]]);
        public WaveformRenderSnapshot CaptureRenderState() => new(Frame, WaveformVisualizationState.Active);
    }

    private sealed class FakeTransportFactory : IAuralineFrameTransportFactory
    {
        private int _created;
        private int _published;
        private int _disposed;
        public int CreatedCount => Volatile.Read(ref _created);
        public int PublishedCount => Volatile.Read(ref _published);
        public int DisposedCount => Volatile.Read(ref _disposed);

        public IAuralineFrameTransport Create(int width, int height, int targetFps)
        {
            Interlocked.Increment(ref _created);
            return new FakeTransport(this, width, height);
        }

        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) => throw new NotSupportedException();

        private sealed class FakeTransport(FakeTransportFactory owner, int width, int height) : IAuralineFrameTransport
        {
            public FrameTransportDescriptor Descriptor { get; } = new("fake", ContractVersion.Current, Guid.NewGuid().ToString("N"), 128L + width * height * 8L, 128, 2, "rgba8888-premul");
            public void Publish(FramePublication frame) => Interlocked.Increment(ref owner._published);
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposed);
                return ValueTask.CompletedTask;
            }
        }
    }
}
