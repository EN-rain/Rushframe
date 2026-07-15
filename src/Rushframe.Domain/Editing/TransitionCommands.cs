namespace Rushframe.Domain.Editing;

public sealed class ApplyTransitionCommand : IAtomicEditCommand
{
    public string Description => "Apply transition";

    public required TimelineItemId LeftItemId { get; init; }
    public required TimelineItemId RightItemId { get; init; }
    public TransitionKind Kind { get; init; } = TransitionKind.CrossDissolve;
    public MediaTime Duration { get; init; } = MediaTime.FromSeconds(1);
    public double Alignment { get; init; } = 0.5;
    public TransitionAudioMode AudioMode { get; init; } = TransitionAudioMode.None;

    private Transition? _applied;
    private Transition? _previous;
    private int _index = -1;

    public EditResult Execute(Sequence sequence)
    {
        if (Duration.Seconds <= 0)
            return EditResult.Fail("Transition duration must be greater than zero");

        foreach (var track in sequence.Tracks)
        {
            var left = track.Items.FirstOrDefault(i => i.Id == LeftItemId);
            var right = track.Items.FirstOrDefault(i => i.Id == RightItemId);
            if (left == null || right == null) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (left.Locked || right.Locked) return EditResult.Fail("Item is locked");
            if (track.Kind is not (TrackKind.Video or TrackKind.Overlay))
                return EditResult.Fail("Visual transitions can only be applied on video or overlay tracks");
            if (left.Kind is not (ItemKind.Clip or ItemKind.Image)
                || right.Kind is not (ItemKind.Clip or ItemKind.Image))
                return EditResult.Fail("Transitions require a video or image item pair");

            var overlap = left.TimelineEnd.Seconds - right.TimelineStart.Seconds;
            if (overlap > 0) return EditResult.Fail("Clips already overlap; cannot apply transition");

            _previous = sequence.Transitions.FirstOrDefault(t => t.LeftItemId == LeftItemId && t.RightItemId == RightItemId);
            _index = _previous == null ? sequence.Transitions.Count : sequence.Transitions.IndexOf(_previous);
            _applied ??= new Transition
            {
                LeftItemId = LeftItemId,
                RightItemId = RightItemId,
                Kind = Kind,
                Duration = MediaTime.FromSeconds(Math.Min(Duration.Seconds, Math.Min(left.Duration.Seconds, right.Duration.Seconds) / 2)),
                Alignment = Math.Clamp(Alignment, 0, 1),
                AudioMode = AudioMode,
            };
            if (_previous == null)
                sequence.Transitions.Add(_applied);
            else
                sequence.Transitions[_index] = _applied;

            return EditResult.Ok();
        }

        return EditResult.Fail("Items not found on same track");
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_applied != null)
            sequence.Transitions.Remove(_applied);
        if (_previous != null && _index >= 0)
            sequence.Transitions.Insert(Math.Min(_index, sequence.Transitions.Count), _previous);
        return EditResult.Ok();
    }
}
