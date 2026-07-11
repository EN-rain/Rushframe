using System.Runtime.InteropServices;

namespace Rushframe.Native.Interop;

internal enum NativeResult
{
    Ok = 0,
    InvalidArgument = 1,
    AllocationFailed = 2,
    BufferTooSmall = 3,
    InternalError = 4,
}

internal static partial class NativeMethods
{
    private const string LibraryName = "Rushframe.Native";

    [LibraryImport(LibraryName, EntryPoint = "rf_create_frame_buffer")]
    internal static partial NativeResult CreateFrameBuffer(int width, int height, out nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "rf_destroy_frame_buffer")]
    internal static partial void DestroyFrameBuffer(nint buffer);

    [LibraryImport(LibraryName, EntryPoint = "rf_get_frame_buffer_info")]
    internal static unsafe partial NativeResult GetFrameBufferInfo(
        nint buffer,
        out byte* data,
        out int stride,
        out nuint size);

    [LibraryImport(LibraryName, EntryPoint = "rf_scale_bgra")]
    internal static unsafe partial NativeResult ScaleBgra(
        byte* source,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        byte* destination,
        int destinationWidth,
        int destinationHeight,
        int destinationStride);

    [LibraryImport(LibraryName, EntryPoint = "rf_get_last_error")]
    internal static unsafe partial nuint GetLastError(byte* destination, nuint destinationSize);
}
