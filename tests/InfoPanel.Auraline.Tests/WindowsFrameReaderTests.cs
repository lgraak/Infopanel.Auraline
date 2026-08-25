using System.IO.MemoryMappedFiles;
using Auraline.Contracts;
using InfoPanel.Auraline.Platform.Windows;

namespace InfoPanel.Auraline.Tests;

public sealed class WindowsFrameReaderTests
{
    [Fact]
    public void ReadsNewCompleteFrameAndIgnoresUnchangedSequence()
    {
        using var mapping = new TestMapping(32, 16, 30);
        mapping.Publish(1, 0x31);
        using var reader = new WindowsSharedMemoryFrameReader(mapping.Descriptor);

        Assert.True(reader.TryReadLatest(out var frame));
        Assert.Equal((32, 16, 128), (frame!.Width, frame.Height, frame.Stride));
        Assert.Equal(Enumerable.Repeat((byte)0x31, 32 * 16 * 4), frame.Pixels);
        Assert.False(reader.TryReadLatest(out _));

        mapping.Publish(2, 0xA7);
        Assert.True(reader.TryReadLatest(out var next));
        Assert.Equal(2ul, next!.Sequence);
        Assert.All(next.Pixels, value => Assert.Equal(0xA7, value));
    }

    [Fact]
    public void RejectsInvalidDescriptorAndHeaderLayouts()
    {
        using var mapping = new TestMapping(32, 16, 30);
        Assert.Throws<NotSupportedException>(() => new WindowsSharedMemoryFrameReader(mapping.Descriptor with
        {
            LayoutVersion = new ContractVersion(2, 0)
        }));
        Assert.Throws<InvalidDataException>(() => new WindowsSharedMemoryFrameReader(mapping.Descriptor with
        {
            PixelFormat = "bgra8888"
        }));

        mapping.WriteMagic(0);
        Assert.Throws<InvalidDataException>(() => new WindowsSharedMemoryFrameReader(mapping.Descriptor));
    }

    [Fact]
    public void OddPublicationIsRejectedUntilComplete()
    {
        using var mapping = new TestMapping(32, 16, 60);
        mapping.MarkTornPublication();
        using var reader = new WindowsSharedMemoryFrameReader(mapping.Descriptor);
        Assert.False(reader.TryReadLatest(out _));

        mapping.Publish(1, 0x42);
        Assert.True(reader.TryReadLatest(out var frame));
        Assert.Equal(60, frame!.TargetFps);
    }

    [Fact]
    public void MissingMappingFailsCleanly()
    {
        var mapping = new TestMapping(32, 16, 30);
        var descriptor = mapping.Descriptor;
        mapping.Dispose();
        Assert.Throws<FileNotFoundException>(() => new WindowsSharedMemoryFrameReader(descriptor));
    }

    private sealed class TestMapping : IDisposable
    {
        private readonly MemoryMappedFile _mapping;
        private readonly MemoryMappedViewAccessor _view;
        private readonly int _payload;
        private long _version;

        public TestMapping(int width, int height, int targetFps)
        {
            _payload = checked(width * height * 4);
            var size = checked((long)WindowsSharedMemoryLayout.HeaderSize + (long)_payload * 2);
            var name = $"Local\\Auraline.ReaderTest.{Guid.NewGuid():N}";
            _mapping = MemoryMappedFile.CreateNew(name, size);
            _view = _mapping.CreateViewAccessor(0, size);
            Descriptor = new FrameTransportDescriptor(
                WindowsSharedMemoryLayout.TransportKind,
                ContractVersion.Current,
                name,
                size,
                WindowsSharedMemoryLayout.HeaderSize,
                WindowsSharedMemoryLayout.SlotCount,
                WindowsSharedMemoryLayout.PixelFormat);
            _view.Write(WindowsSharedMemoryLayout.MagicOffset, WindowsSharedMemoryLayout.Magic);
            _view.Write(WindowsSharedMemoryLayout.MajorVersionOffset, ContractVersion.Current.Major);
            _view.Write(WindowsSharedMemoryLayout.MinorVersionOffset, ContractVersion.Current.Minor);
            _view.Write(WindowsSharedMemoryLayout.HeaderSizeOffset, WindowsSharedMemoryLayout.HeaderSize);
            _view.Write(WindowsSharedMemoryLayout.WidthOffset, width);
            _view.Write(WindowsSharedMemoryLayout.HeightOffset, height);
            _view.Write(WindowsSharedMemoryLayout.StrideOffset, width * 4);
            _view.Write(WindowsSharedMemoryLayout.PixelFormatOffset, WindowsSharedMemoryLayout.PixelFormatCode);
            _view.Write(WindowsSharedMemoryLayout.PayloadLengthOffset, _payload);
            _view.Write(WindowsSharedMemoryLayout.SlotCapacityOffset, _payload);
            _view.Write(WindowsSharedMemoryLayout.SlotCountOffset, WindowsSharedMemoryLayout.SlotCount);
            _view.Write(WindowsSharedMemoryLayout.PremultipliedOffset, 1);
            _view.Write(WindowsSharedMemoryLayout.TargetFpsOffset, targetFps);
        }

        public FrameTransportDescriptor Descriptor { get; }

        public void Publish(long sequence, byte marker)
        {
            var odd = (_version & 1) == 0 ? _version + 1 : _version + 2;
            _view.Write(WindowsSharedMemoryLayout.PublishVersionOffset, odd);
            var active = sequence % 2 == 0 ? 0 : 1;
            _view.WriteArray(
                WindowsSharedMemoryLayout.HeaderSize + active * _payload,
                Enumerable.Repeat(marker, _payload).ToArray(),
                0,
                _payload);
            _view.Write(WindowsSharedMemoryLayout.FrameSequenceOffset, sequence);
            _view.Write(WindowsSharedMemoryLayout.TimestampTicksOffset, DateTimeOffset.UtcNow.UtcTicks);
            _view.Write(WindowsSharedMemoryLayout.ActiveSlotOffset, active);
            _version = odd + 1;
            _view.Write(WindowsSharedMemoryLayout.PublishVersionOffset, _version);
        }

        public void MarkTornPublication()
        {
            _version = 1;
            _view.Write(WindowsSharedMemoryLayout.PublishVersionOffset, _version);
        }

        public void WriteMagic(uint value) => _view.Write(WindowsSharedMemoryLayout.MagicOffset, value);

        public void Dispose()
        {
            _view.Dispose();
            _mapping.Dispose();
        }
    }
}
