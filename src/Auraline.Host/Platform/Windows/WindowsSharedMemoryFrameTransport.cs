using System.IO.MemoryMappedFiles;
using Auraline.Contracts;
using Auraline.Host.Waveform;

namespace Auraline.Host.Platform.Windows;

public sealed class WindowsSharedMemoryFrameTransportFactory : IAuralineFrameTransportFactory
{
    public IAuralineFrameTransport Create(int width, int height, int targetFps) =>
        new WindowsSharedMemoryFrameTransport(width, height, targetFps);

    public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) =>
        new WindowsSharedMemoryFrameReader(descriptor);
}

public static class WindowsSharedMemoryLayout
{
    public const string TransportKind = "windows-shared-memory";
    public const uint Magic = 0x4C525541; // AURL in little-endian byte order.
    public const int HeaderSize = 128;
    public const int SlotCount = 2;
    public const int PixelFormatCodeRgba8888Premultiplied = 1;

    internal const int MagicOffset = 0;
    internal const int MajorVersionOffset = 4;
    internal const int MinorVersionOffset = 8;
    internal const int HeaderSizeOffset = 12;
    internal const int WidthOffset = 16;
    internal const int HeightOffset = 20;
    internal const int StrideOffset = 24;
    internal const int PixelFormatOffset = 28;
    internal const int PayloadLengthOffset = 32;
    internal const int SlotCapacityOffset = 36;
    internal const int SlotCountOffset = 40;
    internal const int PublishVersionOffset = 48;
    internal const int FrameSequenceOffset = 56;
    internal const int TimestampTicksOffset = 64;
    internal const int ActiveSlotOffset = 72;
    internal const int PremultipliedOffset = 76;
    internal const int TargetFpsOffset = 80;
}

public sealed unsafe class WindowsSharedMemoryFrameTransport : IAuralineFrameTransport
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _basePointer;
    private bool _pointerAcquired;
    private bool _disposed;
    private ulong _lastSequence;

    public WindowsSharedMemoryFrameTransport(int width, int height, int targetFps)
    {
        WaveformRenderer.ValidateDimensions(width, height);
        if (targetFps is not (30 or 60))
            throw new ArgumentOutOfRangeException(nameof(targetFps), "Target FPS must be 30 or 60.");

        var stride = checked(width * 4);
        var slotCapacity = checked(stride * height);
        var allocationSize = checked((long)WindowsSharedMemoryLayout.HeaderSize + (long)slotCapacity * WindowsSharedMemoryLayout.SlotCount);
        var resourceName = $"Local\\Auraline.Frame.{Guid.NewGuid():N}";
        _mapping = MemoryMappedFile.CreateNew(resourceName, allocationSize, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, allocationSize, MemoryMappedFileAccess.ReadWrite);

        _view.Write(WindowsSharedMemoryLayout.MagicOffset, WindowsSharedMemoryLayout.Magic);
        _view.Write(WindowsSharedMemoryLayout.MajorVersionOffset, ContractVersion.Current.Major);
        _view.Write(WindowsSharedMemoryLayout.MinorVersionOffset, ContractVersion.Current.Minor);
        _view.Write(WindowsSharedMemoryLayout.HeaderSizeOffset, WindowsSharedMemoryLayout.HeaderSize);
        _view.Write(WindowsSharedMemoryLayout.WidthOffset, width);
        _view.Write(WindowsSharedMemoryLayout.HeightOffset, height);
        _view.Write(WindowsSharedMemoryLayout.StrideOffset, stride);
        _view.Write(WindowsSharedMemoryLayout.PixelFormatOffset, WindowsSharedMemoryLayout.PixelFormatCodeRgba8888Premultiplied);
        _view.Write(WindowsSharedMemoryLayout.PayloadLengthOffset, slotCapacity);
        _view.Write(WindowsSharedMemoryLayout.SlotCapacityOffset, slotCapacity);
        _view.Write(WindowsSharedMemoryLayout.SlotCountOffset, WindowsSharedMemoryLayout.SlotCount);
        _view.Write(WindowsSharedMemoryLayout.PremultipliedOffset, 1);
        _view.Write(WindowsSharedMemoryLayout.TargetFpsOffset, targetFps);

        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _basePointer = pointer + _view.PointerOffset;
        _pointerAcquired = true;

        Descriptor = new FrameTransportDescriptor(
            WindowsSharedMemoryLayout.TransportKind,
            ContractVersion.Current,
            resourceName,
            allocationSize,
            WindowsSharedMemoryLayout.HeaderSize,
            WindowsSharedMemoryLayout.SlotCount,
            WaveformRenderer.PixelFormat);
    }

    public FrameTransportDescriptor Descriptor { get; }

    public void Publish(FramePublication frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFrame(frame);
        if (frame.Sequence <= _lastSequence)
            throw new ArgumentException("Frame sequence must increase monotonically within a render session.", nameof(frame));

        ref var publishVersion = ref *(long*)(_basePointer + WindowsSharedMemoryLayout.PublishVersionOffset);
        var previousVersion = Volatile.Read(ref publishVersion);
        if ((previousVersion & 1) != 0) previousVersion++;
        Interlocked.Exchange(ref publishVersion, previousVersion + 1);

        var activeSlot = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.ActiveSlotOffset));
        var targetSlot = activeSlot == 0 ? 1 : 0;
        var slotCapacity = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.SlotCapacityOffset));
        var target = new Span<byte>(_basePointer + WindowsSharedMemoryLayout.HeaderSize + targetSlot * slotCapacity, frame.Pixels.Length);
        frame.Pixels.Span.CopyTo(target);

        Volatile.Write(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.FrameSequenceOffset), checked((long)frame.Sequence));
        Volatile.Write(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.TimestampTicksOffset), frame.TimestampUtcTicks);
        Volatile.Write(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.ActiveSlotOffset), targetSlot);
        Thread.MemoryBarrier();
        Volatile.Write(ref publishVersion, previousVersion + 2);
        _lastSequence = frame.Sequence;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_pointerAcquired)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
            _basePointer = null;
        }
        _view.Dispose();
        _mapping.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ValidateFrame(FramePublication frame)
    {
        var expectedStride = checked(frame.Width * 4);
        var expectedLength = checked(expectedStride * frame.Height);
        if (frame.Width != _view.ReadInt32(WindowsSharedMemoryLayout.WidthOffset) ||
            frame.Height != _view.ReadInt32(WindowsSharedMemoryLayout.HeightOffset) ||
            frame.Stride != expectedStride || frame.Pixels.Length != expectedLength)
            throw new ArgumentException("Frame geometry does not match the session transport.", nameof(frame));
        if (!string.Equals(frame.PixelFormat, WaveformRenderer.PixelFormat, StringComparison.Ordinal) || !frame.Premultiplied)
            throw new ArgumentException("Frame must use premultiplied RGBA8888 pixels.", nameof(frame));
    }
}

public sealed unsafe class WindowsSharedMemoryFrameReader : IAuralineFrameReader
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _basePointer;
    private bool _pointerAcquired;
    private bool _disposed;

    public WindowsSharedMemoryFrameReader(FrameTransportDescriptor descriptor)
    {
        if (!string.Equals(descriptor.Kind, WindowsSharedMemoryLayout.TransportKind, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported frame transport '{descriptor.Kind}'.");
        if (!ContractVersion.Current.IsCompatibleWith(descriptor.LayoutVersion))
            throw new NotSupportedException($"Unsupported shared-memory layout major version {descriptor.LayoutVersion.Major}.");
        if (descriptor.HeaderSize != WindowsSharedMemoryLayout.HeaderSize || descriptor.SlotCount != WindowsSharedMemoryLayout.SlotCount)
            throw new NotSupportedException("Unsupported shared-memory layout geometry.");
        if (descriptor.AllocationSize < WindowsSharedMemoryLayout.HeaderSize ||
            !string.Equals(descriptor.PixelFormat, WaveformRenderer.PixelFormat, StringComparison.Ordinal))
            throw new InvalidDataException("Shared-memory transport descriptor bounds or pixel format were invalid.");

        Descriptor = descriptor;
        _mapping = MemoryMappedFile.OpenExisting(descriptor.ResourceName, MemoryMappedFileRights.Read);
        _view = _mapping.CreateViewAccessor(0, descriptor.AllocationSize, MemoryMappedFileAccess.Read);
        ValidateHeader();

        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _basePointer = pointer + _view.PointerOffset;
        _pointerAcquired = true;
    }

    public FrameTransportDescriptor Descriptor { get; }

    public bool TryReadLatest(out FrameReadResult? frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ref var publishVersion = ref *(long*)(_basePointer + WindowsSharedMemoryLayout.PublishVersionOffset);
            var before = Volatile.Read(ref publishVersion);
            if (before == 0 || (before & 1) != 0)
            {
                Thread.Yield();
                continue;
            }

            var width = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.WidthOffset));
            var height = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.HeightOffset));
            var stride = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.StrideOffset));
            var payloadLength = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.PayloadLengthOffset));
            var slotCapacity = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.SlotCapacityOffset));
            var activeSlot = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.ActiveSlotOffset));
            var sequence = Volatile.Read(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.FrameSequenceOffset));
            var timestamp = Volatile.Read(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.TimestampTicksOffset));
            var targetFps = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.TargetFpsOffset));
            if (!IsGeometrySafe(width, height, stride, payloadLength, slotCapacity) ||
                activeSlot is < 0 or >= WindowsSharedMemoryLayout.SlotCount || sequence <= 0)
                return false;

            var pixels = new byte[payloadLength];
            new ReadOnlySpan<byte>(_basePointer + WindowsSharedMemoryLayout.HeaderSize + activeSlot * slotCapacity, payloadLength).CopyTo(pixels);
            Thread.MemoryBarrier();
            var after = Volatile.Read(ref publishVersion);
            if (before != after || (after & 1) != 0)
                continue;

            frame = new FrameReadResult(width, height, stride, WaveformRenderer.PixelFormat, true, checked((ulong)sequence), timestamp, targetFps, pixels);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pointerAcquired)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
            _basePointer = null;
        }
        _view.Dispose();
        _mapping.Dispose();
    }

    private void ValidateHeader()
    {
        if (_view.ReadUInt32(WindowsSharedMemoryLayout.MagicOffset) != WindowsSharedMemoryLayout.Magic)
            throw new InvalidDataException("Shared-memory frame signature was invalid.");
        var actualVersion = new ContractVersion(
            _view.ReadInt32(WindowsSharedMemoryLayout.MajorVersionOffset),
            _view.ReadInt32(WindowsSharedMemoryLayout.MinorVersionOffset));
        if (!ContractVersion.Current.IsCompatibleWith(actualVersion))
            throw new NotSupportedException($"Unsupported shared-memory layout major version {actualVersion.Major}.");
        if (_view.ReadInt32(WindowsSharedMemoryLayout.HeaderSizeOffset) != WindowsSharedMemoryLayout.HeaderSize ||
            _view.ReadInt32(WindowsSharedMemoryLayout.SlotCountOffset) != WindowsSharedMemoryLayout.SlotCount ||
            _view.ReadInt32(WindowsSharedMemoryLayout.PixelFormatOffset) != WindowsSharedMemoryLayout.PixelFormatCodeRgba8888Premultiplied ||
            _view.ReadInt32(WindowsSharedMemoryLayout.PremultipliedOffset) != 1 ||
            _view.ReadInt32(WindowsSharedMemoryLayout.TargetFpsOffset) is not (30 or 60) ||
            !IsGeometrySafe(
                _view.ReadInt32(WindowsSharedMemoryLayout.WidthOffset),
                _view.ReadInt32(WindowsSharedMemoryLayout.HeightOffset),
                _view.ReadInt32(WindowsSharedMemoryLayout.StrideOffset),
                _view.ReadInt32(WindowsSharedMemoryLayout.PayloadLengthOffset),
                _view.ReadInt32(WindowsSharedMemoryLayout.SlotCapacityOffset)))
            throw new InvalidDataException("Shared-memory frame header was inconsistent with layout version 1.");
    }

    private bool IsGeometrySafe(int width, int height, int stride, int payloadLength, int slotCapacity)
    {
        if (width is < WaveformRenderer.MinimumDimension or > WaveformRenderer.MaximumDimension ||
            height is < WaveformRenderer.MinimumDimension or > WaveformRenderer.MaximumDimension ||
            stride != checked(width * 4) || payloadLength != checked(stride * height) || slotCapacity < payloadLength)
            return false;
        var required = checked((long)WindowsSharedMemoryLayout.HeaderSize + (long)slotCapacity * WindowsSharedMemoryLayout.SlotCount);
        return required <= Descriptor.AllocationSize;
    }
}
