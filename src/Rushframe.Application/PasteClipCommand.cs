using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Application;

public sealed class PasteClipCommand : IEditCommand
{
    public string Description => "Paste clip";

    public required TrackId TrackId { get; init; }
    public required MediaTime TimelineStart { get; init; }
    public required CopyClipCommand CopyCommand { get; init; }

    private TimelineItemId _pastedItemId;

    public EditResult Execute(Sequence sequence)
    {
        if (CopyCommand.Clipboard == null)
            return EditResult.Fail("Nothing to paste");

        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null)
            return EditResult.Fail(new TrackNotFoundError(TrackId));
        if (track.Locked)
            return EditResult.Fail("Track is locked");

        var paste = TimelineItemCloner.Clone(CopyCommand.Clipboard, TimelineStart);

        _pastedItemId = paste.Id;
        track.Items.Add(paste);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var removed = track.Items.RemoveAll(i => i.Id == _pastedItemId);
            if (removed > 0) return EditResult.Ok();
        }
        return EditResult.Fail("Pasted item not found");
    }
}
