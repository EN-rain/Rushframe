namespace Rushframe.Domain.Editing;

public sealed class AddClipCommand : IEditCommand
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
