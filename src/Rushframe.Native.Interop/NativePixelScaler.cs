namespace Rushframe.Native.Interop;

public static class NativePixelScaler
{
    public static unsafe void ScaleBgra(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        Span<byte> destination,
        int destinationWidth,
        int destinationHeight)
    {
        ValidateDimensions(sourceWidth, sourceHeight, nameof(sourceWidth), nameof(sourceHeight));
        ValidateDimensions(destinationWidth, destinationHeight, nameof(destinationWidth), nameof(destinationHeight));

        var sourceStride = checked(sourceWidth * 4);
        var destinationStride = checked(destinationWidth * 4);
        var sourceLength = checked(sourceStride * sourceHeight);
        var destinationLength = checked(destinationStride * destinationHeight);

        if (source.Length < sourceLength)
        {
            throw new ArgumentException($"Source requires at least {sourceLength} BGRA bytes.", nameof(source));
        }

        if (destination.Length < destinationLength)
        {
            throw new ArgumentException($"Destination requires at least {destinationLength} BGRA bytes.", nameof(destination));
        }

        fixed (byte* sourcePointer = source)
        fixed (byte* destinationPointer = destination)
        {
            NativeError.ThrowIfFailed(NativeMethods.ScaleBgra(
                sourcePointer,
                sourceWidth,
                sourceHeight,
                sourceStride,
                destinationPointer,
                destinationWidth,
                destinationHeight,
                destinationStride));
        }
    }

    private static void ValidateDimensions(int width, int height, string widthName, string heightName)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(widthName, width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(heightName, height, "Height must be positive.");
        }
    }
}
