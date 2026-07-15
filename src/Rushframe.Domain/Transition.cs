namespace Rushframe.Domain;

public enum TransitionKind { CrossDissolve, Slide, Zoom, Blur, Wipe, WhipPan, Mask }

public enum TransitionAudioMode
{
    None,
    Crossfade,
}

public sealed class Transition
{
    public TimelineItemId LeftItemId { get; init; }
    public TimelineItemId RightItemId { get; init; }
    public TransitionKind Kind { get; init; } = TransitionKind.CrossDissolve;
    public MediaTime Duration { get; set; }
    public double Alignment { get; set; } = 0.5;
    public TransitionAudioMode AudioMode { get; init; } = TransitionAudioMode.None;
}
