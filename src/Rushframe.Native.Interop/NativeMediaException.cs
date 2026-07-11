namespace Rushframe.Native.Interop;

public sealed class NativeMediaException : Exception
{
    internal NativeMediaException(NativeResult result, string message)
        : base($"Rushframe native operation failed ({result}): {message}")
    {
        ResultCode = (int)result;
    }

    public int ResultCode { get; }
}
