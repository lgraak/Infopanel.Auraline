using System.IO.MemoryMappedFiles;
using Auraline.Contracts;
using InfoPanel.Auraline.Core;

namespace InfoPanel.Auraline.Platform.Windows;

internal sealed class WindowsSharedMemoryFrameReaderFactory : IPluginFrameReaderFactory
{
    public IAuralineFrameReader Open(FrameTransportDescriptor descriptor) =>
        new WindowsSharedMemoryFrameReader(descriptor);
}

internal static class WindowsSharedMemoryLayout
{
    public const string TransportKind = "windows-shared-memory";
    public const string PixelFormat = "rgba8888-premul";
    public const uint Magic = 0x4C525541;
    public const int HeaderSize = 128;
    public const int SlotCount = 2;
    public const int PixelFormatCode = 1;
    public const int MinimumDimension = 16;
    public const int MaximumDimension = 2048;

    public const int MagicOffset = 0;
    public const int MajorVersionOffset = 4;
    public const int MinorVersionOffset = 8;
    public const int HeaderSizeOffset = 12;
    public const int WidthOffset = 16;
    public const int HeightOffset = 20;
    public const int StrideOffset = 24;
    public const int PixelFormatOffset = 28;
    public const int PayloadLengthOffset = 32;
    public const int SlotCapacityOffset = 36;
    public const int SlotCountOffset = 40;
    public const int PublishVersionOffset = 48;
    public const int FrameSequenceOffset = 56;
    public const int TimestampTicksOffset = 64;
    public const int ActiveSlotOffset = 72;
    public const int PremultipliedOffset = 76;
    public const int TargetFpsOffset = 80;
}

internal sealed unsafe class WindowsSharedMemoryFrameReader : IAuralineFrameReader
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _basePointer;
    private bool _pointerAcquired;
    private bool _disposed;
    private ulong _lastSequence;

    public WindowsSharedMemoryFrameReader(FrameTransportDescriptor descriptor)
    {
        ValidateDescriptor(descriptor);
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
            var signedSequence = Volatile.Read(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.FrameSequenceOffset));
            var timestamp = Volatile.Read(ref *(long*)(_basePointer + WindowsSharedMemoryLayout.TimestampTicksOffset));
            var targetFps = Volatile.Read(ref *(int*)(_basePointer + WindowsSharedMemoryLayout.TargetFpsOffset));
            if (!IsGeometrySafe(width, height, stride, payloadLength, slotCapacity) ||
                activeSlot is < 0 or >= WindowsSharedMemoryLayout.SlotCount ||
                signedSequence <= 0 || targetFps is not (30 or 60))
                return false;

            var sequence = checked((ulong)signedSequence);
            if (sequence <= _lastSequence) return false;

            var pixels = new byte[payloadLength];
            new ReadOnlySpan<byte>(
                _basePointer + WindowsSharedMemoryLayout.HeaderSize + activeSlot * slotCapacity,
                payloadLength).CopyTo(pixels);
            Thread.MemoryBarrier();
            var after = Volatile.Read(ref publishVersion);
            if (before != after || (after & 1) != 0) continue;

            _lastSequence = sequence;
            frame = new FrameReadResult(
                width,
                height,
                stride,
                WindowsSharedMemoryLayout.PixelFormat,
                true,
                sequence,
                timestamp,
                targetFps,
                pixels);
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

    private static void ValidateDescriptor(FrameTransportDescriptor descriptor)
    {
        if (!string.Equals(descriptor.Kind, WindowsSharedMemoryLayout.TransportKind, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported frame transport '{descriptor.Kind}'.");
        if (!ContractVersion.Current.IsCompatibleWith(descriptor.LayoutVersion))
            throw new NotSupportedException($"Unsupported shared-memory layout major version {descriptor.LayoutVersion.Major}.");
        if (descriptor.HeaderSize != WindowsSharedMemoryLayout.HeaderSize ||
            descriptor.SlotCount != WindowsSharedMemoryLayout.SlotCount)
            throw new NotSupportedException("Unsupported shared-memory layout geometry.");
        var maximumAllocation = checked(
            (long)WindowsSharedMemoryLayout.HeaderSize +
            (long)WindowsSharedMemoryLayout.MaximumDimension * WindowsSharedMemoryLayout.MaximumDimension * 4 *
            WindowsSharedMemoryLayout.SlotCount);
        if (string.IsNullOrWhiteSpace(descriptor.ResourceName) ||
            descriptor.AllocationSize < WindowsSharedMemoryLayout.HeaderSize ||
            descriptor.AllocationSize > maximumAllocation ||
            !string.Equals(descriptor.PixelFormat, WindowsSharedMemoryLayout.PixelFormat, StringComparison.Ordinal))
            throw new InvalidDataException("Shared-memory transport descriptor bounds or pixel format were invalid.");
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
            _view.ReadInt32(WindowsSharedMemoryLayout.PixelFormatOffset) != WindowsSharedMemoryLayout.PixelFormatCode ||
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
        if (width is < WindowsSharedMemoryLayout.MinimumDimension or > WindowsSharedMemoryLayout.MaximumDimension ||
            height is < WindowsSharedMemoryLayout.MinimumDimension or > WindowsSharedMemoryLayout.MaximumDimension)
            return false;
        try
        {
            var expectedStride = checked(width * 4);
            var expectedPayload = checked(expectedStride * height);
            var requiredAllocation = checked(
                (long)WindowsSharedMemoryLayout.HeaderSize +
                (long)slotCapacity * WindowsSharedMemoryLayout.SlotCount);
            return stride == expectedStride &&
                   payloadLength == expectedPayload &&
                   slotCapacity >= payloadLength &&
                   requiredAllocation <= Descriptor.AllocationSize;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
