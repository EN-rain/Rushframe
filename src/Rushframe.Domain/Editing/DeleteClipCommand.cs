namespace Rushframe.Domain.Editing;

public sealed class DeleteClipCommand : IAtomicEditCommand
{
    public string Description => $"Delete clip {ItemId}";

    public required TimelineItemId ItemId { get; init; }

    private TrackId _trackId;
    private int _index;
    private TimelineItem? _removed;
    private readonly List<(int Index, Transition Transition)> _removedTransitions = [];

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == ItemId);
            if (idx < 0) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (track.Items[idx].Locked) return EditResult.Fail("Item is locked");

            _trackId = track.Id;
            _index = idx;
            _removed = track.Items[idx];
            _removedTransitions.Clear();
            for (var transitionIndex = 0; transitionIndex < sequence.Transitions.Count; transitionIndex++)
            {
                var transition = sequence.Transitions[transitionIndex];
                if (transition.LeftItemId == ItemId || transition.RightItemId == ItemId)
                    _removedTransitions.Add((transitionIndex, transition));
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

        track.Items.Insert(Math.Min(_index, track.Items.Count), _removed);
        foreach (var (transitionIndex, transition) in _removedTransitions.OrderBy(entry => entry.Index))
            sequence.Transitions.Insert(Math.Min(transitionIndex, sequence.Transitions.Count), transition);
        _removedTransitions.Clear();
        _removed = null;
        return EditResult.Ok();
    }
}
