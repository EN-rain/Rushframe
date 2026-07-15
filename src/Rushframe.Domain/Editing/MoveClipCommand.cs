namespace Rushframe.Domain.Editing;

public sealed class MoveClipCommand : IAtomicEditCommand
{
    public string Description => $"Move clip {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public TrackId? TargetTrackId { get; init; }
    public MediaTime? NewTimelineStart { get; init; }
    public int? NewIndex { get; init; }
    public RippleState Ripple { get; init; } = new();

    private TrackId _sourceTrackId;
    private MediaTime _oldStart;
    private MediaTime _oldDuration;
    private int _oldIndex;
    private readonly List<(TimelineItem Item, MediaTime OldStart)> _rippledItems = [];

    public EditResult Execute(Sequence sequence)
    {
        if (NewTimelineStart is { Seconds: < 0 })
            return EditResult.Fail("Timeline start cannot be negative");
        _rippledItems.Clear();
        foreach (var track in sequence.Tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == ItemId);
            if (idx < 0) continue;
            if (track.Locked)
                return EditResult.Fail("Track is locked");

            _sourceTrackId = track.Id;
            _oldStart = track.Items[idx].TimelineStart;
            _oldDuration = track.Items[idx].Duration;
            _oldIndex = idx;

            var item = track.Items[idx];
            if (item.Locked)
                return EditResult.Fail("Item is locked");

            if (TargetTrackId.HasValue && TargetTrackId.Value != track.Id)
            {
                var destTrack = sequence.Tracks.FirstOrDefault(t => t.Id == TargetTrackId.Value);
                if (destTrack == null)
                    return EditResult.Fail(new TrackNotFoundError(TargetTrackId.Value));
                if (destTrack.Locked)
                    return EditResult.Fail("Track is locked");

                if (!TrackCompatibility.IsItemCompatibleWithTrack(item.Kind, destTrack.Kind))
                    return EditResult.Fail(new ValidationError(
                        nameof(TargetTrackId),
                        $"Cannot move {item.Kind} item to {destTrack.Kind} track"));
            }

            var rippleCandidates = new List<TimelineItem>();
            if (Ripple.Enabled && NewTimelineStart.HasValue)
            {
                var gapEnd = _oldStart.Add(_oldDuration);
                rippleCandidates = track.Items
                    .Where(candidate => candidate.Id != item.Id && candidate.TimelineStart >= gapEnd)
                    .OrderBy(candidate => candidate.TimelineStart.Seconds)
                    .ToList();
                if (rippleCandidates.Any(candidate => candidate.Locked))
                    return EditResult.Fail("A downstream item is locked");
            }

            foreach (var rippled in rippleCandidates)
            {
                _rippledItems.Add((rippled, rippled.TimelineStart));
                rippled.TimelineStart = rippled.TimelineStart.Subtract(_oldDuration);
            }

            track.Items.RemoveAt(idx);

            var targetTrack = TargetTrackId.HasValue
                ? sequence.Tracks.FirstOrDefault(t => t.Id == TargetTrackId.Value)
                : track;

            if (targetTrack == null)
            {
                track.Items.Insert(idx, item);
                return EditResult.Fail("Target track not found");
            }

            if (NewTimelineStart.HasValue)
                item.TimelineStart = NewTimelineStart.Value;

            var insertIdx = NewIndex ?? Math.Min(idx, targetTrack.Items.Count);
            insertIdx = Math.Clamp(insertIdx, 0, targetTrack.Items.Count);
            targetTrack.Items.Insert(insertIdx, item);

            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        var currentTrack = sequence.Tracks.FirstOrDefault(track => track.Items.Any(item => item.Id == ItemId));
        var sourceTrack = sequence.Tracks.FirstOrDefault(track => track.Id == _sourceTrackId);
        if (currentTrack == null) return EditResult.Fail("Item not found");
        if (sourceTrack == null) return EditResult.Fail("Source track not found");
        var itemIndex = currentTrack.Items.FindIndex(item => item.Id == ItemId);
        var item = currentTrack.Items[itemIndex];

        foreach (var (rippled, oldStart) in _rippledItems)
            rippled.TimelineStart = oldStart;
        _rippledItems.Clear();
        currentTrack.Items.RemoveAt(itemIndex);
        item.TimelineStart = _oldStart;
        sourceTrack.Items.Insert(Math.Min(_oldIndex, sourceTrack.Items.Count), item);
        return EditResult.Ok();
    }
}
