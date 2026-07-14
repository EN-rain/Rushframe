namespace Rushframe.Domain.Editing;

public sealed class DuplicateClipCommand : IEditCommand
{
    public string Description => $"Duplicate clip {ItemId}";

    public required TimelineItemId ItemId { get; init; }

    private TimelineItem? _duplicate;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == ItemId);
            if (idx < 0) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (track.Items[idx].Locked) return EditResult.Fail("Item is locked");

            var original = track.Items[idx];
            _duplicate = TimelineItemCloner.Clone(
                original,
                original.TimelineStart.Add(original.Duration));

            track.Items.Insert(idx + 1, _duplicate);
            return EditResult.Ok();
        }

        return EditResult.Fail("Item not found");
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_duplicate == null) return EditResult.Fail("Nothing to undo");

        foreach (var track in sequence.Tracks)
        {
            if (track.Items.Remove(_duplicate))
            {
                _duplicate = null;
                return EditResult.Ok();
            }
        }

        return EditResult.Fail("Duplicate not found");
    }
}
