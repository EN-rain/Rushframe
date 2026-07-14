namespace Rushframe.Domain.Editing;

public sealed class AddTrackCommand : IEditCommand
{
    public string Description => $"Add track {TrackKind}";

    public required TrackKind TrackKind { get; init; }
    public int? InsertAt { get; init; }

    private Track? _added;

    public EditResult Execute(Sequence sequence)
    {
        _added = new Track { Kind = TrackKind, Name = TrackKind.ToString(), Order = InsertAt ?? sequence.Tracks.Count };
        if (InsertAt.HasValue)
            sequence.Tracks.Insert(Math.Clamp(InsertAt.Value, 0, sequence.Tracks.Count), _added);
        else
            sequence.Tracks.Add(_added);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_added != null && sequence.Tracks.Remove(_added))
        {
            _added = null;
            return EditResult.Ok();
        }
        return EditResult.Fail("Track not found");
    }
}

public sealed class DeleteTrackCommand : IEditCommand
{
    public string Description => $"Delete track {TrackId}";

    public required TrackId TrackId { get; init; }

    private int _index;
    private Track? _removed;

    public EditResult Execute(Sequence sequence)
    {
        var idx = sequence.Tracks.FindIndex(t => t.Id == TrackId);
        if (idx < 0) return EditResult.Fail(new TrackNotFoundError(TrackId));

        _index = idx;
        _removed = sequence.Tracks[idx];
        sequence.Tracks.RemoveAt(idx);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_removed == null) return EditResult.Fail("Nothing to undo");
        sequence.Tracks.Insert(Math.Min(_index, sequence.Tracks.Count), _removed);
        _removed = null;
        return EditResult.Ok();
    }
}

public sealed class RenameTrackCommand : IEditCommand
{
    public string Description => $"Rename track {TrackId}";

    public required TrackId TrackId { get; init; }
    public required string NewName { get; init; }

    private string? _oldName;

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));

        _oldName = track.Name;
        track.Name = NewName;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldName == null) return EditResult.Fail("Nothing to undo");
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));

        track.Name = _oldName;
        return EditResult.Ok();
    }
}

public sealed class ReorderTrackCommand : IEditCommand
{
    public string Description => $"Reorder track {TrackId}";

    public required TrackId TrackId { get; init; }
    public required int NewIndex { get; init; }

    private int _oldIndex;

    public EditResult Execute(Sequence sequence)
    {
        var idx = sequence.Tracks.FindIndex(t => t.Id == TrackId);
        if (idx < 0) return EditResult.Fail(new TrackNotFoundError(TrackId));

        _oldIndex = idx;
        var track = sequence.Tracks[idx];
        sequence.Tracks.RemoveAt(idx);
        sequence.Tracks.Insert(Math.Clamp(NewIndex, 0, sequence.Tracks.Count), track);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var idx = sequence.Tracks.FindIndex(t => t.Id == TrackId);
        if (idx < 0) return EditResult.Fail(new TrackNotFoundError(TrackId));

        var track = sequence.Tracks[idx];
        sequence.Tracks.RemoveAt(idx);
        sequence.Tracks.Insert(Math.Min(_oldIndex, sequence.Tracks.Count), track);
        return EditResult.Ok();
    }
}

public sealed class DuplicateTrackCommand : IEditCommand
{
    public string Description => $"Duplicate track {TrackId}";

    public required TrackId TrackId { get; init; }

    private Track? _duplicate;

    public EditResult Execute(Sequence sequence)
    {
        var idx = sequence.Tracks.FindIndex(t => t.Id == TrackId);
        if (idx < 0) return EditResult.Fail(new TrackNotFoundError(TrackId));

        var original = sequence.Tracks[idx];
        _duplicate = new Track
        {
            Kind = original.Kind,
            Name = original.Name + " (copy)",
            Order = original.Order + 1,
        };

        foreach (var item in original.Items)
            _duplicate.Items.Add(TimelineItemCloner.Clone(item));

        sequence.Tracks.Insert(idx + 1, _duplicate);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_duplicate != null && sequence.Tracks.Remove(_duplicate))
        {
            _duplicate = null;
            return EditResult.Ok();
        }
        return EditResult.Fail("Duplicate track not found");
    }
}

public sealed class ToggleTrackMuteCommand : IEditCommand
{
    public string Description => $"Toggle mute for track {TrackId}";
    public required TrackId TrackId { get; init; }
    private bool _oldMute;

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        _oldMute = track.Muted;
        track.Muted = !track.Muted;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        track.Muted = _oldMute;
        return EditResult.Ok();
    }
}

public sealed class ToggleTrackSoloCommand : IEditCommand
{
    public string Description => $"Toggle solo for track {TrackId}";
    public required TrackId TrackId { get; init; }
    private bool _oldSolo;

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        _oldSolo = track.Solo;
        track.Solo = !track.Solo;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        track.Solo = _oldSolo;
        return EditResult.Ok();
    }
}

public sealed class ToggleTrackLockCommand : IEditCommand
{
    public string Description => $"Toggle lock for track {TrackId}";
    public required TrackId TrackId { get; init; }
    private bool _oldLocked;

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        _oldLocked = track.Locked;
        track.Locked = !track.Locked;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(t => t.Id == TrackId);
        if (track == null) return EditResult.Fail(new TrackNotFoundError(TrackId));
        track.Locked = _oldLocked;
        return EditResult.Ok();
    }
}
