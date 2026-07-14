namespace Rushframe.Domain.Editing;

public sealed class RippleDeleteClipCommand : IEditCommand
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
            _deleteStart = _removed.TimelineStart;
            _deleteDuration = _removed.Duration;

            if (Ripple.Enabled)
            {
                var gapEnd = _deleteStart.Add(_deleteDuration);
                foreach (var item in track.Items.Where(i => i.TimelineStart >= gapEnd).OrderBy(i => i.TimelineStart.Seconds))
                {
                    _rippledItems.Add((item, item.TimelineStart));
                    item.TimelineStart = item.TimelineStart.Subtract(_deleteDuration);
                }
            }

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
        _removed = null;
        return EditResult.Ok();
    }
}
