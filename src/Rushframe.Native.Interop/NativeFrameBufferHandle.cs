using Microsoft.Win32.SafeHandles;

namespace Rushframe.Native.Interop;

internal sealed class NativeFrameBufferHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeFrameBufferHandle() : base(ownsHandle: true)
    {
    }

    internal static NativeFrameBufferHandle Create(int width, int height)
    {
        NativeError.ThrowIfFailed(NativeMethods.CreateFrameBuffer(width, height, out var pointer));
        var handle = new NativeFrameBufferHandle();
        handle.SetHandle(pointer);
        return handle;
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.DestroyFrameBuffer(handle);
        return true;
    }
}
