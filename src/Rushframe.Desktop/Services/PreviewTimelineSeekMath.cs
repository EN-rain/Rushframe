namespace Rushframe.Desktop.Services;

public static class PreviewTimelineSeekMath
{
    public static double GetFrameStepTargetSeconds(
        double? canonicalTimelineSeconds,
        double chunkOffsetSeconds,
        double sourcePositionSeconds,
        int direction,
        double framesPerSecond)
    {
        var currentTimelineSeconds = canonicalTimelineSeconds
                                     ?? chunkOffsetSeconds + Math.Max(0, sourcePositionSeconds);
        var fps = double.IsFinite(framesPerSecond) && framesPerSecond > 0 ? framesPerSecond : 30;
        return currentTimelineSeconds + direction / fps;
    }
}
