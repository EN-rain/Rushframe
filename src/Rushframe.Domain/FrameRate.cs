namespace Rushframe.Domain;

/// <summary>
/// Rational frame rate. Using a numerator/denominator avoids drift for rates such as 23.976 and 29.97.
/// </summary>
public readonly record struct FrameRate
{
    public int Numerator { get; init; }
    public int Denominator { get; init; }

    public FrameRate(int numerator, int denominator = 1)
    {
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));

        var gcd = GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public static FrameRate Fps23_976 => new(24_000, 1_001);
    public static FrameRate Fps24 => new(24);
    public static FrameRate Fps25 => new(25);
    public static FrameRate Fps29_97 => new(30_000, 1_001);
    public static FrameRate Fps30 => new(30);
    public static FrameRate Fps50 => new(50);
    public static FrameRate Fps59_94 => new(60_000, 1_001);
    public static FrameRate Fps60 => new(60);

    public double Value => (double)Numerator / Denominator;

    public MediaTime FrameDuration => MediaTime.FromFraction(Denominator, Numerator);

    public long FrameToTicks(long frame) =>
        checked((long)Math.Round(frame * (double)MediaTime.TicksPerSecond * Denominator / Numerator));

    public long TicksToNearestFrame(long ticks) =>
        (long)Math.Round(ticks * Value / MediaTime.TicksPerSecond);

    public MediaTime Snap(MediaTime time) => MediaTime.FromTicks(FrameToTicks(TicksToNearestFrame(time.Ticks)));

    public static FrameRate FromDouble(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            return Fps30;

        foreach (var candidate in new[]
                 {
                     Fps23_976, Fps24, Fps25, Fps29_97, Fps30, Fps50, Fps59_94, Fps60,
                 })
        {
            if (Math.Abs(candidate.Value - value) < 0.001)
                return candidate;
        }

        const int scale = 1_000;
        return new FrameRate(Math.Max(1, (int)Math.Round(value * scale)), scale);
    }

    public override string ToString() => Denominator == 1
        ? Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{Numerator}/{Denominator}";

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return Math.Abs(a);
    }
}
