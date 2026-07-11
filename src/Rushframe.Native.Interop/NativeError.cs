using System.Text;

namespace Rushframe.Native.Interop;

internal static class NativeError
{
    internal static unsafe void ThrowIfFailed(NativeResult result)
    {
        if (result == NativeResult.Ok)
        {
            return;
        }

        var required = NativeMethods.GetLastError(null, 0);
        if (required <= 1)
        {
            throw new NativeMediaException(result, "No native error message was provided.");
        }

        var buffer = new byte[checked((int)required)];
        fixed (byte* pointer = buffer)
        {
            NativeMethods.GetLastError(pointer, (nuint)buffer.Length);
        }

        var terminator = Array.IndexOf(buffer, (byte)0);
        var length = terminator >= 0 ? terminator : buffer.Length;
        throw new NativeMediaException(result, Encoding.UTF8.GetString(buffer, 0, length));
    }
}
