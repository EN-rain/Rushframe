namespace Rushframe.Domain.Editing;

public sealed class SplitClipCommand : IEditCommand
{
    public string Description => $"Split clip {ItemId} at {SplitTime.Seconds:F2}s";

    public required TrackId TrackId { get; init; }
    public required TimelineItemId ItemId { get; init; }
    public required MediaTime SplitTime { get; init; }

    private MediaTime _originalDuration;
    private MediaTime _originalSourceDuration;
    private readonly List<TimelineItem> _addedItems = [];

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null)
            return EditResult.Fail("Track not found");
        if (track.Locked)
            return EditResult.Fail("Track is locked");

        var index = track.Items.FindIndex(i => i.Id == ItemId);
        if (index < 0)
            return EditResult.Fail("Item not found");

        var item = track.Items[index];
        if (item.Locked)
            return EditResult.Fail("Item is locked");
        if (SplitTime <= item.TimelineStart || SplitTime >= item.TimelineStart.Add(item.Duration))
            return EditResult.Fail("Split time is outside item bounds");

        _originalDuration = item.Duration;
        _originalSourceDuration = item.SourceDuration;
        var offset = SplitTime.Subtract(item.TimelineStart);
        var remaining = item.Duration.Subtract(offset);
        var sourceOffset = MediaTime.FromSeconds(offset.Seconds * item.Speed);

        var right = TimelineItemCloner.Clone(item, SplitTime);
        right.Duration = remaining;
        right.SourceStart = item.SourceStart.Add(sourceOffset);
        right.SourceDuration = MediaTime.FromSeconds(remaining.Seconds * item.Speed);

        item.Duration = offset;
        item.SourceDuration = MediaTime.FromSeconds(offset.Seconds * item.Speed);
        track.Items.Insert(index + 1, right);
        _addedItems.Add(right);

        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail("Track not found");

        foreach (var added in _addedItems)
            track.Items.Remove(added);
        _addedItems.Clear();

        var orig = track.Items.FirstOrDefault(i => i.Id == ItemId);
        if (orig != null)
        {
            orig.Duration = _originalDuration;
            orig.SourceDuration = _originalSourceDuration;
        }

        return EditResult.Ok();
    }
}
