using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class PreviewTimelineSeekMathTests
{
    [Fact]
    public void GetFrameStepTargetSeconds_UsesCanonicalPlayheadWhenAvailable()
    {
        var target = PreviewTimelineSeekMath.GetFrameStepTargetSeconds(
            canonicalTimelineSeconds: 15,
            chunkOffsetSeconds: 12,
            sourcePositionSeconds: 0,
            direction: 1,
            framesPerSecond: 30);

        Assert.Equal(15 + 1.0 / 30, target, precision: 6);
    }

    [Fact]
    public void GetFrameStepTargetSeconds_FallsBackToChunkOffsetAndSourcePosition()
    {
        var target = PreviewTimelineSeekMath.GetFrameStepTargetSeconds(
            canonicalTimelineSeconds: null,
            chunkOffsetSeconds: 12,
            sourcePositionSeconds: 3,
            direction: -1,
            framesPerSecond: 24);

        Assert.Equal(15 - 1.0 / 24, target, precision: 6);
    }
}
