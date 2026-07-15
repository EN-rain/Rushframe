namespace Rushframe.Domain.Editing;

public sealed class AddClipCommand : IAtomicEditCommand
{
    public string Description => $"Add clip to track {TrackId}";

    public required TrackId TrackId { get; init; }
    public required TimelineItem Item { get; init; }

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null)
            return EditResult.Fail(new TrackNotFoundError(TrackId));
        if (track.Locked)
            return EditResult.Fail("Track is locked");
        if (!TrackCompatibility.IsItemCompatibleWithTrack(Item.Kind, track.Kind))
            return EditResult.Fail($"{Item.Kind} items are not compatible with {track.Kind} tracks");
        if (sequence.Tracks.SelectMany(candidate => candidate.Items).Any(candidate => candidate.Id == Item.Id))
            return EditResult.Fail($"Timeline item {Item.Id} already exists");
        if (Item.TimelineStart.Seconds < 0)
            return EditResult.Fail("Timeline start cannot be negative");
        if (Item.Duration.Seconds <= 0)
            return EditResult.Fail("Duration must be greater than zero");
        if (Item.SourceStart.Seconds < 0 || Item.SourceDuration.Seconds < 0)
            return EditResult.Fail("Source bounds cannot be negative");

        track.Items.Add(Item);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var removed = track.Items.RemoveAll(i => i.Id == Item.Id);
            if (removed > 0) return EditResult.Ok();
        }
        return EditResult.Fail("Added item not found");
    }
}
