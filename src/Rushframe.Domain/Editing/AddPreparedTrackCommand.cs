namespace Rushframe.Domain.Editing;

/// <summary>
/// Adds a pre-created track with a stable identifier so a composite command can
/// target that track with subsequent clip operations in the same atomic edit.
/// </summary>
public sealed class AddPreparedTrackCommand : IEditCommand
{
    public required Track Track { get; init; }
    public int? InsertAt { get; init; }
    public string Description => $"Add prepared {Track.Kind} track";

    private bool _added;

    public EditResult Execute(Sequence sequence)
    {
        if (sequence.Tracks.Any(candidate => candidate.Id == Track.Id))
            return EditResult.Fail($"Track {Track.Id} already exists");
        if (InsertAt.HasValue)
            sequence.Tracks.Insert(Math.Clamp(InsertAt.Value, 0, sequence.Tracks.Count), Track);
        else
            sequence.Tracks.Add(Track);
        _added = true;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (!_added) return EditResult.Fail("Prepared track was not added");
        var index = sequence.Tracks.FindIndex(candidate => candidate.Id == Track.Id);
        if (index < 0) return EditResult.Fail($"Track {Track.Id} was not found");
        sequence.Tracks.RemoveAt(index);
        _added = false;
        return EditResult.Ok();
    }
}
