using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Application;

public sealed class PasteClipCommand : IAtomicEditCommand
{
    public string Description => "Paste clip";

    public required TrackId TrackId { get; init; }
    public required MediaTime TimelineStart { get; init; }
    public required CopyClipCommand CopyCommand { get; init; }

    private TimelineItem? _pastedItem;

    public EditResult Execute(Sequence sequence)
    {
        if (CopyCommand.Clipboard == null)
            return EditResult.Fail("Nothing to paste");
        if (TimelineStart.Seconds < 0)
            return EditResult.Fail("Timeline start cannot be negative");

        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null)
            return EditResult.Fail(new TrackNotFoundError(TrackId));
        if (track.Locked)
            return EditResult.Fail("Track is locked");
        if (!TrackCompatibility.IsItemCompatibleWithTrack(CopyCommand.Clipboard.Kind, track.Kind))
            return EditResult.Fail($"{CopyCommand.Clipboard.Kind} items are not compatible with {track.Kind} tracks");

        _pastedItem ??= TimelineItemCloner.Clone(CopyCommand.Clipboard, TimelineStart);
        track.Items.Add(_pastedItem);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            if (_pastedItem != null && track.Items.Remove(_pastedItem)) return EditResult.Ok();
        }
        return EditResult.Fail("Pasted item not found");
    }
}
