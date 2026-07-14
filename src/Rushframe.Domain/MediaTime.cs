namespace Rushframe.Domain;

/// <summary>
/// Frame-safe media time stored as integer ticks. 120,000 ticks per second divides evenly across
/// the standard integer and NTSC-derived frame rates used by Rushframe.
/// </summary>
public readonly record struct MediaTime : IComparable<MediaTime>
{
    public const long TicksPerSecond = 120_000;

    public long Ticks { get; }

    // Kept for backward-compatible JSON and callers that previously treated MediaTime as a fraction.
    public long Numerator => Ticks;
    public long Denominator => TicksPerSecond;

    public MediaTime(long numerator, long denominator)
    {
        denominator = denominator > 0 ? denominator : 1;
        Ticks = checked((long)Math.Round(numerator * (double)TicksPerSecond / denominator));
    }

    private MediaTime(long ticks)
    {
        Ticks = ticks;
    }

    public static readonly MediaTime Zero = new(0L);

    public double Seconds => (double)Ticks / TicksPerSecond;

    public static MediaTime FromTicks(long ticks) => new(ticks);

    public static MediaTime FromFraction(long numerator, long denominator) => new(numerator, denominator);

    public static MediaTime FromSeconds(double seconds)
    {
        if (!double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return new MediaTime(checked((long)Math.Round(seconds * TicksPerSecond)));
    }

    public static MediaTime FromFrame(long frame, FrameRate frameRate) =>
        new(frameRate.FrameToTicks(frame));

    public static MediaTime FromFrame(int frame, double fps) =>
        FromFrame(frame, FrameRate.FromDouble(fps));

    public long ToNearestFrame(FrameRate frameRate) => frameRate.TicksToNearestFrame(Ticks);

    public MediaTime SnapToFrame(FrameRate frameRate) => frameRate.Snap(this);

    public MediaTime Add(MediaTime other) => new(checked(Ticks + other.Ticks));

    public MediaTime Subtract(MediaTime other) => new(checked(Ticks - other.Ticks));

    public MediaTime Multiply(double factor)
    {
        if (!double.IsFinite(factor)) throw new ArgumentOutOfRangeException(nameof(factor));
        return new MediaTime(checked((long)Math.Round(Ticks * factor)));
    }

    public MediaTime Divide(double divisor)
    {
        if (!double.IsFinite(divisor) || Math.Abs(divisor) < double.Epsilon)
            throw new ArgumentOutOfRangeException(nameof(divisor));
        return new MediaTime(checked((long)Math.Round(Ticks / divisor)));
    }

    public MediaTime Clamp(MediaTime minimum, MediaTime maximum) =>
        FromTicks(Math.Clamp(Ticks, minimum.Ticks, maximum.Ticks));

    public int CompareTo(MediaTime other) => Ticks.CompareTo(other.Ticks);

    public static MediaTime operator +(MediaTime a, MediaTime b) => a.Add(b);
    public static MediaTime operator -(MediaTime a, MediaTime b) => a.Subtract(b);
    public static MediaTime operator *(MediaTime value, double factor) => value.Multiply(factor);
    public static MediaTime operator /(MediaTime value, double divisor) => value.Divide(divisor);
    public static bool operator <(MediaTime a, MediaTime b) => a.Ticks < b.Ticks;
    public static bool operator >(MediaTime a, MediaTime b) => a.Ticks > b.Ticks;
    public static bool operator <=(MediaTime a, MediaTime b) => a.Ticks <= b.Ticks;
    public static bool operator >=(MediaTime a, MediaTime b) => a.Ticks >= b.Ticks;

    public override string ToString() => Seconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
