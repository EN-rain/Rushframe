namespace Rushframe.Domain.Editing;

/// <summary>
/// Atomically replaces a track's item list. Intended for transcript-derived edits
/// such as silence removal and take assembly where a sequence of split/delete
/// commands would require unstable intermediate item identifiers.
/// </summary>
public sealed class ReplaceTrackItemsCommand : IEditCommand
{
    public required TrackId TrackId { get; init; }
    public required IReadOnlyList<TimelineItem> NewItems { get; init; }
    public string Description { get; init; } = "Replace track items";

    private List<TimelineItem>? _oldItems;

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail($"Track {TrackId} was not found");
        if (track.Locked) return EditResult.Fail("Track is locked");
        if (track.Items.Any(item => item.Locked))
            return EditResult.Fail("Track contains locked items and cannot be replaced");

        _oldItems = track.Items.Select(item => TimelineItemCloner.Clone(item, preserveId: true)).ToList();
        track.Items.Clear();
        track.Items.AddRange(NewItems.Select(item => TimelineItemCloner.Clone(item, preserveId: true)));
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail($"Track {TrackId} was not found");
        if (_oldItems == null) return EditResult.Fail("No previous track state was captured");

        track.Items.Clear();
        track.Items.AddRange(_oldItems.Select(item => TimelineItemCloner.Clone(item, preserveId: true)));
        return EditResult.Ok();
    }
}
