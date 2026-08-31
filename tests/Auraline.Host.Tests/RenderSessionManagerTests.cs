using Auraline.Contracts;
using Auraline.Host.RenderSessions;
using Auraline.Host.Waveform;
using Auraline.Host.Configuration;
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

    [Fact]
    public async Task SavedProfileRevisionHotAppliesWithoutReplacingActiveSession()
    {
        var fixture = new Fixture();
        var profiles = new MutableProfiles();
        await using var manager = new RenderSessionManager(
            fixture.TransportFactory,
            new FakeWaveformSource(),
            new WaveformRenderer(),
            new SystemRenderSessionClock(),
            new RenderSessionOptions(32, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1)),
            NullLogger<RenderSessionManager>.Instance,
            profiles);
        var attachment = manager.Attach(ProductDefaults.DefaultProfileId, 320, 120, 30, ContractVersion.Current);
        Assert.True(SpinWait.SpinUntil(() => fixture.TransportFactory.PublishedCount > 0, TimeSpan.FromSeconds(2)));

        profiles.Profile = profiles.Profile with { Revision = 2, Waveform = profiles.Profile.Waveform with { Color = "#FF0000" } };

        Assert.True(SpinWait.SpinUntil(() => manager.GetDiagnostic(attachment.Session.SessionId)?.HotApplyCount == 1, TimeSpan.FromSeconds(2)));
        var diagnostic = manager.GetDiagnostic(attachment.Session.SessionId)!;
        Assert.Equal(2, diagnostic.ProfileRevision);
        Assert.Equal(attachment.Session.SessionId, diagnostic.SessionId);
    }

    [Fact]
    public async Task MultipleSchedulersSurviveHotApplyAndRepeatedTeardownAtThirtyAndSixtyFps()
    {
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var profiles = new MutableProfiles();
            var transports = new FakeTransportFactory();
            var manager = new RenderSessionManager(
                transports,
                new FakeWaveformSource(),
                new WaveformRenderer(),
                new StressClock(),
                new RenderSessionOptions(32, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1)),
                NullLogger<RenderSessionManager>.Instance,
                profiles);

            try
            {
                manager.Attach(ProductDefaults.DefaultProfileId, 64, 32, 30, ContractVersion.Current);
                manager.Attach(ProductDefaults.DefaultProfileId, 80, 40, 60, ContractVersion.Current);
                manager.Attach(ProductDefaults.DefaultProfileId, 96, 48, 30, ContractVersion.Current);
                manager.Attach(ProductDefaults.DefaultProfileId, 112, 56, 60, ContractVersion.Current);

                Assert.True(SpinWait.SpinUntil(() => transports.PublishedCount >= 100, TimeSpan.FromSeconds(10)));

                for (var revision = 2; revision <= 20; revision++)
                {
                    profiles.Profile = profiles.Profile with
                    {
                        Revision = revision,
                        Waveform = profiles.Profile.Waveform with
                        {
                            Color = revision % 2 == 0 ? "#FF5533" : "#76B9FF",
                            SmoothingEnabled = revision % 3 == 0,
                            SmoothingAmount = revision % 3 == 0 ? 0.7 : 0
                        }
                    };
                }

                Assert.True(SpinWait.SpinUntil(
                    () => transports.PublishedCount >= 300 &&
                        manager.GetDiagnostics().Sessions.All(session => session.HotApplyCount > 0),
                    TimeSpan.FromSeconds(10)));
                Assert.Equal(4, manager.GetDiagnostics().ActiveSessionCount);
                Assert.Contains(manager.GetDiagnostics().Sessions, session => session.TargetFps == 30);
                Assert.Contains(manager.GetDiagnostics().Sessions, session => session.TargetFps == 60);
            }
            finally
            {
                await manager.DisposeAsync();
            }

            Assert.Equal(4, transports.CreatedCount);
            Assert.Equal(4, transports.DisposedCount);
        }
    }

    private sealed class Fixture(int cap = 32)
    {
        public FakeClock Clock { get; } = new();
        public FakeTransportFactory TransportFactory { get; } = new();

        public RenderSessionManager CreateManager(IProfileCatalog? profiles = null) => new(
            TransportFactory,
            new FakeWaveformSource(),
            new WaveformRenderer(),
            Clock,
            new RenderSessionOptions(cap, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1)),
            NullLogger<RenderSessionManager>.Instance,
            profiles);
    }

    private sealed class MutableProfiles : IProfileCatalog
    {
        private ProfileDefinition _profile = new()
        {
            Id = ProductDefaults.DefaultProfileId,
            FriendlyName = "Default Waveform",
            SourceGroupId = ProductDefaults.DefaultSourceGroupId
        };

        public ProfileDefinition Profile
        {
            get => Volatile.Read(ref _profile);
            set => Volatile.Write(ref _profile, value);
        }

        public IReadOnlyList<ProfileDefinition> GetProfiles() => [Profile];
        public ProfileDefinition GetProfile(string profileId) => profileId == Profile.Id ? Profile : throw new KeyNotFoundException();
    }

    private sealed class FakeClock : IRenderSessionClock
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public DateTimeOffset UtcNow => _now;
        public void Advance(TimeSpan duration) => _now += duration;
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class StressClock : IRenderSessionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }
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
