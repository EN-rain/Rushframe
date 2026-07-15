namespace Rushframe.Domain.Editing;

/// <summary>
/// Atomically replaces a track's item list. Intended for transcript-derived edits
/// such as silence removal and take assembly where a sequence of split/delete
/// commands would require unstable intermediate item identifiers.
/// </summary>
public sealed class ReplaceTrackItemsCommand : IAtomicEditCommand
{
    public required TrackId TrackId { get; init; }
    public required IReadOnlyList<TimelineItem> NewItems { get; init; }
    public string Description { get; init; } = "Replace track items";

    private List<TimelineItem>? _oldItems;
    private readonly List<(int Index, Transition Transition)> _removedTransitions = [];

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail($"Track {TrackId} was not found");
        if (track.Locked) return EditResult.Fail("Track is locked");
        if (track.Items.Any(item => item.Locked))
            return EditResult.Fail("Track contains locked items and cannot be replaced");
        if (NewItems.Any(item => !TrackCompatibility.IsItemCompatibleWithTrack(item.Kind, track.Kind)))
            return EditResult.Fail($"One or more replacement items are incompatible with {track.Kind} tracks");
        if (NewItems.Any(item => item.TimelineStart.Seconds < 0 || item.Duration.Seconds <= 0))
            return EditResult.Fail("Replacement items require non-negative start times and positive durations");
        if (NewItems.Select(item => item.Id).Distinct().Count() != NewItems.Count)
            return EditResult.Fail("Replacement item identifiers must be unique");

        var otherItemIds = sequence.Tracks
            .Where(candidate => candidate.Id != TrackId)
            .SelectMany(candidate => candidate.Items)
            .Select(item => item.Id)
            .ToHashSet();
        if (NewItems.Any(item => otherItemIds.Contains(item.Id)))
            return EditResult.Fail("A replacement item identifier already exists on another track");

        var replacements = NewItems.Select(item => TimelineItemCloner.Clone(item, preserveId: true)).ToList();
        var replacementIds = replacements.Select(item => item.Id).ToHashSet();
        var futureItemIds = otherItemIds.Concat(replacementIds).ToHashSet();
        var oldIds = track.Items.Select(item => item.Id).ToHashSet();

        _oldItems = track.Items.Select(item => TimelineItemCloner.Clone(item, preserveId: true)).ToList();
        _removedTransitions.Clear();
        for (var index = 0; index < sequence.Transitions.Count; index++)
        {
            var transition = sequence.Transitions[index];
            var referencesReplacedTrack = oldIds.Contains(transition.LeftItemId) || oldIds.Contains(transition.RightItemId);
            var remainsValid = futureItemIds.Contains(transition.LeftItemId) && futureItemIds.Contains(transition.RightItemId);
            if (referencesReplacedTrack && !remainsValid)
                _removedTransitions.Add((index, transition));
        }

        foreach (var (index, _) in _removedTransitions.OrderByDescending(entry => entry.Index))
            sequence.Transitions.RemoveAt(index);
        track.Items.Clear();
        track.Items.AddRange(replacements);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail($"Track {TrackId} was not found");
        if (_oldItems == null) return EditResult.Fail("No previous track state was captured");

        var restoredItems = _oldItems.Select(item => TimelineItemCloner.Clone(item, preserveId: true)).ToList();
        track.Items.Clear();
        track.Items.AddRange(restoredItems);
        foreach (var (index, transition) in _removedTransitions.OrderBy(entry => entry.Index))
            sequence.Transitions.Insert(Math.Min(index, sequence.Transitions.Count), transition);
        return EditResult.Ok();
    }
}
