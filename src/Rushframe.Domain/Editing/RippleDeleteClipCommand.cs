namespace Rushframe.Domain.Editing;

public sealed class RippleDeleteClipCommand : IAtomicEditCommand
{
    public string Description => $"Ripple delete clip {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public RippleState Ripple { get; init; } = new();

    private TrackId _trackId;
    private int _index;
    private TimelineItem? _removed;
    private MediaTime _deleteStart;
    private MediaTime _deleteDuration;
    private readonly List<(TimelineItem Item, MediaTime OldStart)> _rippledItems = [];
    private readonly List<(int Index, Transition Transition)> _removedTransitions = [];

    public EditResult Execute(Sequence sequence)
    {
        _rippledItems.Clear();
        foreach (var track in sequence.Tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == ItemId);
            if (idx < 0) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (track.Items[idx].Locked) return EditResult.Fail("Item is locked");

            _trackId = track.Id;
            _index = idx;
            _removed = track.Items[idx];
            _deleteStart = _removed.TimelineStart;
            _deleteDuration = _removed.Duration;

            var rippleCandidates = new List<TimelineItem>();
            if (Ripple.Enabled)
            {
                var gapEnd = _deleteStart.Add(_deleteDuration);
                rippleCandidates = track.Items
                    .Where(candidate => candidate.Id != _removed.Id && candidate.TimelineStart >= gapEnd)
                    .OrderBy(candidate => candidate.TimelineStart.Seconds)
                    .ToList();
                if (rippleCandidates.Any(candidate => candidate.Locked))
                {
                    _removed = null;
                    return EditResult.Fail("A downstream item is locked");
                }
            }

            _removedTransitions.Clear();
            for (var transitionIndex = 0; transitionIndex < sequence.Transitions.Count; transitionIndex++)
            {
                var transition = sequence.Transitions[transitionIndex];
                if (transition.LeftItemId == ItemId || transition.RightItemId == ItemId)
                    _removedTransitions.Add((transitionIndex, transition));
            }

            foreach (var item in rippleCandidates)
            {
                _rippledItems.Add((item, item.TimelineStart));
                item.TimelineStart = item.TimelineStart.Subtract(_deleteDuration);
            }
            foreach (var (transitionIndex, _) in _removedTransitions.OrderByDescending(entry => entry.Index))
                sequence.Transitions.RemoveAt(transitionIndex);

            track.Items.RemoveAt(idx);
            return EditResult.Ok();
        }

        return EditResult.Fail("Item not found");
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_removed == null) return EditResult.Fail("Nothing to undo");

        var track = sequence.Tracks.FirstOrDefault(t => t.Id == _trackId);
        if (track == null) return EditResult.Fail("Track not found");

        foreach (var (item, oldStart) in _rippledItems)
            item.TimelineStart = oldStart;
        _rippledItems.Clear();

        track.Items.Insert(Math.Min(_index, track.Items.Count), _removed);
        foreach (var (transitionIndex, transition) in _removedTransitions.OrderBy(entry => entry.Index))
            sequence.Transitions.Insert(Math.Min(transitionIndex, sequence.Transitions.Count), transition);
        _removedTransitions.Clear();
        _removed = null;
        return EditResult.Ok();
    }
}
