namespace Rushframe.Domain.Editing;

public sealed class ApplyTransitionCommand : IEditCommand
{
    public string Description => "Apply transition";

    public required TimelineItemId LeftItemId { get; init; }
    public required TimelineItemId RightItemId { get; init; }
    public TransitionKind Kind { get; init; } = TransitionKind.CrossDissolve;
    public MediaTime Duration { get; init; } = MediaTime.FromSeconds(1);
    public double Alignment { get; init; } = 0.5;

    private Transition? _applied;
    private Transition? _previous;
    private int _index = -1;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var left = track.Items.FirstOrDefault(i => i.Id == LeftItemId);
            var right = track.Items.FirstOrDefault(i => i.Id == RightItemId);
            if (left == null || right == null) continue;

            var overlap = left.TimelineEnd.Seconds - right.TimelineStart.Seconds;
            if (overlap > 0) return EditResult.Fail("Clips already overlap; cannot apply transition");

            _previous = sequence.Transitions.FirstOrDefault(t => t.LeftItemId == LeftItemId && t.RightItemId == RightItemId);
            _index = _previous == null ? sequence.Transitions.Count : sequence.Transitions.IndexOf(_previous);
            _applied = new Transition
            {
                LeftItemId = LeftItemId,
                RightItemId = RightItemId,
                Kind = Kind,
                Duration = MediaTime.FromSeconds(Math.Min(Duration.Seconds, Math.Min(left.Duration.Seconds, right.Duration.Seconds) / 2)),
                Alignment = Math.Clamp(Alignment, 0, 1),
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
        _applied = null;
        return EditResult.Ok();
    }
}
