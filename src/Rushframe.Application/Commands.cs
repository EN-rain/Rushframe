using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Application;

public sealed class CopyClipCommand : IEditCommand
{
    public string Description => $"Copy clip {ItemId}";

    public required TimelineItemId ItemId { get; init; }

    public TimelineItem? Clipboard { get; private set; }

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == ItemId);
            if (idx < 0) continue;

            var original = track.Items[idx];
            Clipboard = TimelineItemCloner.Clone(original, original.TimelineStart);

            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        Clipboard = null;
        return EditResult.Ok();
    }
}
