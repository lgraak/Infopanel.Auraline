using Auraline.Contracts;
using Auraline.Host.Platform.Windows;

namespace Auraline.Host.Tests;

public sealed class WindowsSharedMemoryFrameTransportTests
{
    [Fact]
    public async Task PublishesVersionedCompleteFramesToMultipleReaders()
    {
        var factory = new WindowsSharedMemoryFrameTransportFactory();
        await using var transport = factory.Create(64, 32, 30);
        using var firstReader = factory.Open(transport.Descriptor);
        using var secondReader = factory.Open(transport.Descriptor);
        var firstPixels = Enumerable.Repeat((byte)0x11, 64 * 32 * 4).ToArray();
        var secondPixels = Enumerable.Repeat((byte)0xE7, 64 * 32 * 4).ToArray();

        Assert.Equal(WindowsSharedMemoryLayout.TransportKind, transport.Descriptor.Kind);
        Assert.Equal(ContractVersion.Current, transport.Descriptor.LayoutVersion);
        Assert.Equal(WindowsSharedMemoryLayout.HeaderSize, transport.Descriptor.HeaderSize);
        Assert.Equal(WindowsSharedMemoryLayout.SlotCount, transport.Descriptor.SlotCount);

        transport.Publish(Frame(64, 32, 1, firstPixels));
        Assert.True(firstReader.TryReadLatest(out var first));
        Assert.NotNull(first);
        Assert.Equal(1ul, first.Sequence);
        Assert.Equal(64, first.Width);
        Assert.Equal(32, first.Height);
        Assert.Equal(64 * 4, first.Stride);
        Assert.Equal(firstPixels, first.Pixels);

        transport.Publish(Frame(64, 32, 2, secondPixels));
        Assert.True(firstReader.TryReadLatest(out var next));
        Assert.True(secondReader.TryReadLatest(out var other));
        Assert.Equal(2ul, next!.Sequence);
        Assert.Equal(secondPixels, next.Pixels);
        Assert.Equal(next.Sequence, other!.Sequence);
        Assert.Equal(next.TimestampUtcTicks, other.TimestampUtcTicks);
        Assert.Equal(next.Pixels, other.Pixels);
    }

    [Fact]
    public async Task RejectsInvalidGeometryVersionAndNonMonotonicSequence()
    {
        var factory = new WindowsSharedMemoryFrameTransportFactory();
        await using var transport = factory.Create(32, 16, 60);
        var pixels = new byte[32 * 16 * 4];

        transport.Publish(Frame(32, 16, 1, pixels, 60));
        Assert.Throws<ArgumentException>(() => transport.Publish(Frame(32, 16, 1, pixels, 60)));
        Assert.Throws<ArgumentException>(() => transport.Publish(Frame(31, 16, 2, new byte[31 * 16 * 4], 60)));
        Assert.Throws<NotSupportedException>(() => factory.Open(transport.Descriptor with
        {
            LayoutVersion = new ContractVersion(2, 0)
        }));
        Assert.Throws<InvalidDataException>(() => factory.Open(transport.Descriptor with
        {
            PixelFormat = "bgra8888"
        }));
        Assert.Throws<InvalidDataException>(() => factory.Open(transport.Descriptor with
        {
            AllocationSize = WindowsSharedMemoryLayout.HeaderSize - 1
        }));
    }

    [Fact]
    public async Task ConcurrentReaderNeverAcceptsMixedSlotContent()
    {
        var factory = new WindowsSharedMemoryFrameTransportFactory();
        await using var transport = factory.Create(320, 120, 60);
        using var reader = factory.Open(transport.Descriptor);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writer = Task.Run(() =>
        {
            ulong sequence = 1;
            while (!cancellation.IsCancellationRequested)
            {
                var marker = (byte)((sequence % 250) + 1);
                transport.Publish(Frame(320, 120, sequence++, Enumerable.Repeat(marker, 320 * 120 * 4).ToArray(), 60));
            }
        });

        var complete = 0;
        while (complete < 20 && !cancellation.IsCancellationRequested)
        {
            if (!reader.TryReadLatest(out var frame) || frame is null) continue;
            var marker = frame.Pixels[0];
            Assert.All(frame.Pixels, value => Assert.Equal(marker, value));
            complete++;
        }
        cancellation.Cancel();
        await writer;
        Assert.True(complete > 0, "Reader should accept at least one complete frame during concurrent publication.");
    }

    [Fact]
    public async Task MappingNameCannotBeReopenedAfterOwnerDisposes()
    {
        var factory = new WindowsSharedMemoryFrameTransportFactory();
        var transport = factory.Create(32, 16, 30);
        var descriptor = transport.Descriptor;
        await transport.DisposeAsync();

        Assert.Throws<FileNotFoundException>(() => factory.Open(descriptor));
    }

    private static FramePublication Frame(int width, int height, ulong sequence, byte[] pixels, int targetFps = 30) =>
        new(width, height, width * 4, "rgba8888-premul", true, sequence, DateTimeOffset.UtcNow.UtcTicks, targetFps, pixels);
}
