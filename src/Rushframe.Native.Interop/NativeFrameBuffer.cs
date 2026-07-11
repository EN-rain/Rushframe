namespace Rushframe.Native.Interop;

public sealed class NativeFrameBuffer : IDisposable
{
    private readonly NativeFrameBufferHandle _handle;

    public NativeFrameBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _handle = NativeFrameBufferHandle.Create(width, height);
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => checked(Width * 4);
    public int Length => checked(Stride * Height);

    public unsafe void CopyFrom(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (source.Length != Length)
        {
            throw new ArgumentException($"Expected exactly {Length} BGRA bytes.", nameof(source));
        }

        NativeError.ThrowIfFailed(NativeMethods.GetFrameBufferInfo(
            _handle.DangerousGetHandle(), out var destination, out _, out var size));
        if ((nuint)source.Length > size)
        {
            throw new InvalidOperationException("Native frame buffer is smaller than its declared dimensions.");
        }

        fixed (byte* sourcePointer = source)
        {
            Buffer.MemoryCopy(sourcePointer, destination, (long)size, source.Length);
        }
    }

    public unsafe void CopyTo(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (destination.Length < Length)
        {
            throw new ArgumentException($"Destination requires at least {Length} bytes.", nameof(destination));
        }

        NativeError.ThrowIfFailed(NativeMethods.GetFrameBufferInfo(
            _handle.DangerousGetHandle(), out var source, out _, out var size));
        fixed (byte* destinationPointer = destination)
        {
            Buffer.MemoryCopy(source, destinationPointer, destination.Length, checked((long)size));
        }
    }

    public void Dispose() => _handle.Dispose();
}
