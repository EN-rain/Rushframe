namespace Rushframe.Domain.Tests;

public sealed class MediaTimeTests
{
    [Fact]
    public void FromSeconds_round_trips()
    {
        var t = MediaTime.FromSeconds(15.5);
        Assert.Equal(15.5, t.Seconds, 3);
    }

    [Fact]
    public void Add_preserves_duration()
    {
        var a = MediaTime.FromSeconds(10);
        var b = MediaTime.FromSeconds(5);
        Assert.Equal(15, (a + b).Seconds, 3);
    }

    [Fact]
    public void Subtract_works()
    {
        var a = MediaTime.FromSeconds(10);
        var b = MediaTime.FromSeconds(3);
        Assert.Equal(7, (a - b).Seconds, 3);
    }

    [Fact]
    public void Zero_is_zero()
    {
        Assert.Equal(0, MediaTime.Zero.Seconds);
    }

    [Fact]
    public void Media_time_uses_fixed_integer_ticks()
    {
        var time = MediaTime.FromSeconds(1.5);

        Assert.Equal(MediaTime.TicksPerSecond, time.Denominator);
        Assert.Equal(180_000, time.Ticks);
    }

    [Fact]
    public void Rational_frame_rate_snaps_without_ntsc_drift()
    {
        var frameRate = FrameRate.Fps29_97;
        var frame = MediaTime.FromFrame(300, frameRate);

        Assert.Equal(300, frame.ToNearestFrame(frameRate));
        Assert.Equal(10.01, frame.Seconds, 3);
    }
}
