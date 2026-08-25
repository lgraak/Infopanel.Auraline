using Auraline.Contracts;
using InfoPanel.Auraline.Core;

namespace InfoPanel.Auraline.Tests;

public sealed class PluginRuntimeTests
{
    [Fact]
    public async Task ConnectsSelectsDefaultAndPublishesFirstFrame()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer-1", 320, 120)]);

        await runtime.TickAsync(CancellationToken.None);

        var diagnostics = runtime.GetDiagnostics();
        Assert.Equal(PluginConnectionState.Connected, diagnostics.State);
        Assert.Equal(AuralineProfiles.DefaultProfileId, diagnostics.SelectedProfileId);
        Assert.Equal("Default Waveform", diagnostics.SelectedProfileName);
        Assert.Equal((320, 120), (sink.Width, sink.Height));
        Assert.Equal(1ul, sink.LastFrame!.Sequence);
        Assert.Single(host.AttachRequests);
    }

    [Fact]
    public async Task ResizeAttachesNewSessionPublishesThenDetachesOldLease()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer-1", 320, 120)]);
        await runtime.TickAsync(CancellationToken.None);
        var oldSession = runtime.GetDiagnostics().Outputs.Single().SessionId;

        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer-1", 640, 240)]);
        await runtime.TickAsync(CancellationToken.None);

        var output = runtime.GetDiagnostics().Outputs.Single();
        Assert.Equal((640, 240), (sink.Width, sink.Height));
        Assert.NotEqual(oldSession, output.SessionId);
        Assert.Contains(host.Detached, item => item.SessionId == oldSession);
        Assert.Equal([(320, 120), (640, 240)], host.AttachRequests.Select(item => (item.Width, item.Height)).ToArray());
    }

    [Fact]
    public async Task TwoOutputsMaintainDistinctDimensionSessions()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var first = new FakeSink(AuralinePlugin.PrimaryImageId);
        var second = new FakeSink(AuralinePlugin.SecondaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([first, second]);
        runtime.SetDemands(
        [
            new(AuralinePlugin.PrimaryImageId, "consumer-1", 320, 120),
            new(AuralinePlugin.SecondaryImageId, "consumer-2", 640, 240)
        ]);

        await runtime.TickAsync(CancellationToken.None);

        Assert.Equal(2, runtime.GetDiagnostics().Outputs.Count(output => output.SessionId is not null));
        Assert.Equal([(320, 120), (640, 240)], host.AttachRequests.Select(item => (item.Width, item.Height)).ToArray());
        Assert.Equal((320, 120), (first.Width, first.Height));
        Assert.Equal((640, 240), (second.Width, second.Height));
    }

    [Fact]
    public async Task SameImageMultipleDemandsUsesLargestWithoutDuplicatingSession()
    {
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, new FakeClock());
        runtime.SetSinks([sink]);
        runtime.SetDemands(
        [
            new(AuralinePlugin.PrimaryImageId, "consumer-1", 320, 120),
            new(AuralinePlugin.PrimaryImageId, "consumer-2", 640, 240)
        ]);

        await runtime.TickAsync(CancellationToken.None);

        Assert.Single(host.AttachRequests);
        Assert.Equal((640, 240), (host.AttachRequests[0].Width, host.AttachRequests[0].Height));
    }

    [Fact]
    public async Task ExpiredHeartbeatReattachesWithoutCrashing()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);
        await runtime.TickAsync(CancellationToken.None);
        host.ExpireNextHeartbeat = true;
        clock.Advance(TimeSpan.FromSeconds(9));

        await runtime.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(501));
        await runtime.TickAsync(CancellationToken.None);

        Assert.Equal(2, host.AttachRequests.Count);
        Assert.NotNull(runtime.GetDiagnostics().Outputs.Single().SessionId);
    }

    [Fact]
    public async Task HostRestartReconnectsAndRenegotiatesAutomatically()
    {
        var clock = new FakeClock();
        var firstHost = new FakeHost();
        var secondHost = new FakeHost();
        var clients = new Queue<IAuralineHostClient>([firstHost, secondHost]);
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = new AuralinePluginRuntime(
            _ => clients.Dequeue(),
            new FakeReaderFactory(),
            clock,
            "test");
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);
        await runtime.TickAsync(CancellationToken.None);
        firstHost.FailProfiles = true;
        clock.Advance(TimeSpan.FromSeconds(6));
        await runtime.TickAsync(CancellationToken.None);
        Assert.Equal(PluginConnectionState.Reconnecting, runtime.GetDiagnostics().State);

        clock.Advance(TimeSpan.FromMilliseconds(501));
        await runtime.TickAsync(CancellationToken.None);

        Assert.Equal(PluginConnectionState.Connected, runtime.GetDiagnostics().State);
        Assert.Single(secondHost.AttachRequests);
        Assert.True(runtime.GetDiagnostics().ReconnectCount >= 1);
    }

    [Fact]
    public async Task DisconnectRetainsLastFrameDuringGraceThenPublishesFailure()
    {
        var clock = new FakeClock();
        var host = new FakeHost { FailProfiles = true };
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);

        await runtime.TickAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await runtime.TickAsync(CancellationToken.None);
        Assert.Equal(0, sink.UnavailableCount);

        clock.Advance(TimeSpan.FromMilliseconds(600));
        await runtime.TickAsync(CancellationToken.None);
        Assert.Equal(1, sink.UnavailableCount);
    }

    [Fact]
    public async Task MissingProfileIsExplicitAndDoesNotFallBack()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, clock);
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);
        runtime.Configure(new Uri(AuralinePlugin.DefaultEndpoint), "missing-profile", 30);

        await runtime.TickAsync(CancellationToken.None);

        var diagnostics = runtime.GetDiagnostics();
        Assert.Equal(PluginConnectionState.Unavailable, diagnostics.State);
        Assert.Contains("missing-profile", diagnostics.LastError);
        Assert.Empty(host.AttachRequests);
    }

    [Fact]
    public async Task AttachFailureIsReportedWithoutCrashingOrInventingASession()
    {
        var host = new FakeHost { FailAttach = true };
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = Runtime(host, new FakeClock());
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);

        await runtime.TickAsync(CancellationToken.None);

        var diagnostics = runtime.GetDiagnostics();
        Assert.Equal(PluginConnectionState.Connected, diagnostics.State);
        Assert.Null(diagnostics.Outputs.Single().SessionId);
        Assert.Contains("capacity", diagnostics.LastError);
    }

    [Fact]
    public async Task UnsupportedTransportIsIncompatibleAndReturnedLeaseIsDetached()
    {
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = new AuralinePluginRuntime(
            _ => host,
            new ThrowingReaderFactory(new NotSupportedException("unsupported layout")),
            new FakeClock(),
            "test");
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);

        await runtime.TickAsync(CancellationToken.None);

        Assert.Equal(PluginConnectionState.Incompatible, runtime.GetDiagnostics().State);
        Assert.Single(host.Detached);
    }

    [Fact]
    public async Task SessionThatNeverPublishesIsReleasedAndRetried()
    {
        var clock = new FakeClock();
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        await using var runtime = new AuralinePluginRuntime(
            _ => host,
            new EmptyReaderFactory(),
            clock,
            "test");
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);
        await runtime.TickAsync(CancellationToken.None);
        clock.Advance(AuralinePluginRuntime.FrameStaleTimeout + TimeSpan.FromMilliseconds(1));

        await runtime.TickAsync(CancellationToken.None);

        Assert.Null(runtime.GetDiagnostics().Outputs.Single().SessionId);
        Assert.Contains("stopped advancing", runtime.GetDiagnostics().LastError);
        Assert.Single(host.Detached);
    }

    [Fact]
    public async Task ShutdownDetachesLeaseAndDisposesReader()
    {
        var host = new FakeHost();
        var sink = new FakeSink(AuralinePlugin.PrimaryImageId);
        var readers = new FakeReaderFactory();
        var runtime = new AuralinePluginRuntime(_ => host, readers, new FakeClock(), "test");
        runtime.SetSinks([sink]);
        runtime.SetDemands([new(AuralinePlugin.PrimaryImageId, "consumer", 320, 120)]);
        await runtime.TickAsync(CancellationToken.None);
        var session = runtime.GetDiagnostics().Outputs.Single().SessionId;

        await runtime.DisposeAsync();

        Assert.Contains(host.Detached, item => item.SessionId == session);
        Assert.Equal(1, readers.DisposedCount);
    }

    private static AuralinePluginRuntime Runtime(FakeHost host, FakeClock clock) => new(
        _ => host,
        new FakeReaderFactory(),
        clock,
        "test");

    private sealed class FakeClock : IPluginRuntimeClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan value) => UtcNow += value;
    }

    private sealed class FakeSink(string imageId) : IPluginFrameSink
    {
        public string ImageId { get; } = imageId;
        public int Width { get; private set; } = 320;
        public int Height { get; private set; } = 120;
        public FrameReadResult? LastFrame { get; private set; }
        public int UnavailableCount { get; private set; }

        public void Publish(FrameReadResult frame)
        {
            Width = frame.Width;
            Height = frame.Height;
            LastFrame = frame;
        }

        public void PublishUnavailable(string message) => UnavailableCount++;
    }

    private sealed class FakeReaderFactory : IPluginFrameReaderFactory
    {
        public int DisposedCount { get; private set; }

        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) =>
            new Reader(descriptor, () => DisposedCount++);

        private sealed class Reader(FrameTransportDescriptor descriptor, Action onDispose) : IAuralineFrameReader
        {
            private bool _read;
            private bool _disposed;
            public FrameTransportDescriptor Descriptor { get; } = descriptor;

            public bool TryReadLatest(out FrameReadResult? frame)
            {
                if (_read)
                {
                    frame = null;
                    return false;
                }
                _read = true;
                var width = ParsePart(Descriptor.ResourceName, 1);
                var height = ParsePart(Descriptor.ResourceName, 2);
                var fps = ParsePart(Descriptor.ResourceName, 3);
                var pixels = Enumerable.Repeat((byte)0x7F, width * height * 4).ToArray();
                frame = new FrameReadResult(width, height, width * 4, "rgba8888-premul", true, 1,
                    DateTimeOffset.UtcNow.UtcTicks, fps, pixels);
                return true;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                onDispose();
            }

            private static int ParsePart(string value, int index) => int.Parse(value.Split('-')[index]);
        }
    }

    private sealed class ThrowingReaderFactory(Exception exception) : IPluginFrameReaderFactory
    {
        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) => throw exception;
    }

    private sealed class EmptyReaderFactory : IPluginFrameReaderFactory
    {
        public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) => new EmptyReader(descriptor);

        private sealed class EmptyReader(FrameTransportDescriptor descriptor) : IAuralineFrameReader
        {
            public FrameTransportDescriptor Descriptor { get; } = descriptor;

            public bool TryReadLatest(out FrameReadResult? frame)
            {
                frame = null;
                return false;
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeHost : IAuralineHostClient
    {
        private int _nextSession;
        public bool FailProfiles { get; set; }
        public bool FailAttach { get; set; }
        public bool ExpireNextHeartbeat { get; set; }
        public List<(string ProfileId, int Width, int Height, int Fps)> AttachRequests { get; } = [];
        public List<(string SessionId, string LeaseId)> Detached { get; } = [];

        public Task<AuralineProfileCatalog> GetProfilesAsync(CancellationToken cancellationToken)
        {
            if (FailProfiles) throw new HttpRequestException("offline");
            return Task.FromResult(new AuralineProfileCatalog(
                ContractVersion.Current,
                "1.0.0-m4",
                [new(AuralineProfiles.DefaultProfileId, "Default Waveform", true, "waveform", "available")]));
        }

        public Task<RenderSessionAttachment> AttachAsync(
            string profileId,
            int width,
            int height,
            int targetFps,
            CancellationToken cancellationToken)
        {
            if (FailAttach)
                throw new AuralineHostException(System.Net.HttpStatusCode.Conflict, "Auraline render-session capacity is exhausted.");
            AttachRequests.Add((profileId, width, height, targetFps));
            var number = ++_nextSession;
            var sessionId = $"session-{width}-{height}-{targetFps}-{number}";
            var attachment = HostClientTests.Attachment(sessionId, $"lease-{number}", width, height, targetFps);
            return Task.FromResult(attachment with
            {
                Session = attachment.Session with
                {
                    Transport = attachment.Session.Transport with { ResourceName = $"frame-{width}-{height}-{targetFps}-{number}" }
                }
            });
        }

        public Task<ConsumerLease?> HeartbeatAsync(
            string sessionId,
            string leaseId,
            CancellationToken cancellationToken)
        {
            if (ExpireNextHeartbeat)
            {
                ExpireNextHeartbeat = false;
                return Task.FromResult<ConsumerLease?>(null);
            }
            return Task.FromResult<ConsumerLease?>(new ConsumerLease(leaseId, sessionId, DateTimeOffset.UtcNow.AddSeconds(25)));
        }

        public Task DetachAsync(string sessionId, string leaseId, CancellationToken cancellationToken)
        {
            Detached.Add((sessionId, leaseId));
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
