namespace Rushframe.Domain.Editing;

public sealed class AddMarkerCommand : IAtomicEditCommand
{
    public string Description => $"Add marker at {Marker.Time.Seconds:F2}s";

    public required Marker Marker { get; init; }

    public EditResult Execute(Sequence sequence)
    {
        if (Marker.Time.Seconds < 0) return EditResult.Fail("Marker time cannot be negative");
        if (Marker.Duration.Seconds < 0) return EditResult.Fail("Marker duration cannot be negative");
        if (sequence.Markers.Any(candidate => candidate.Id == Marker.Id))
            return EditResult.Fail($"Marker {Marker.Id} already exists");
        sequence.Markers.Add(Marker);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        sequence.Markers.RemoveAll(m => m.Id == Marker.Id);
        return EditResult.Ok();
    }
}

public sealed class EditMarkerCommand : IAtomicEditCommand
{
    public string Description => $"Edit marker {MarkerId}";

    public required MarkerId MarkerId { get; init; }
    public required string NewLabel { get; init; }
    public required MediaTime NewTime { get; init; }
    public string? NewNote { get; init; }
    public string? NewColor { get; init; }
    public MediaTime NewDuration { get; init; }

    private string? _oldLabel;
    private string? _oldNote;
    private MediaTime _oldTime;
    private MediaTime _oldDuration;
    private string? _oldColor;

    public EditResult Execute(Sequence sequence)
    {
        if (NewTime.Seconds < 0) return EditResult.Fail("Marker time cannot be negative");
        if (NewDuration.Seconds < 0) return EditResult.Fail("Marker duration cannot be negative");
        var marker = sequence.Markers.FirstOrDefault(m => m.Id == MarkerId);
        if (marker == null) return EditResult.Fail("Marker not found");

        _oldLabel = marker.Label;
        _oldNote = marker.Note;
        _oldTime = marker.Time;
        _oldDuration = marker.Duration;
        _oldColor = marker.Color;

        marker.Label = NewLabel;
        marker.Note = NewNote;
        marker.Time = NewTime;
        marker.Duration = NewDuration;
        marker.Color = NewColor;
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var marker = sequence.Markers.FirstOrDefault(m => m.Id == MarkerId);
        if (marker == null) return EditResult.Fail("Marker not found");

        marker.Label = _oldLabel ?? marker.Label;
        marker.Note = _oldNote;
        marker.Time = _oldTime;
        marker.Duration = _oldDuration;
        marker.Color = _oldColor;
        return EditResult.Ok();
    }
}

public sealed class DeleteMarkerCommand : IAtomicEditCommand
{
    public string Description => $"Delete marker {MarkerId}";

    public required MarkerId MarkerId { get; init; }

    private Marker? _removed;
    private int _index = -1;

    public EditResult Execute(Sequence sequence)
    {
        var idx = sequence.Markers.FindIndex(m => m.Id == MarkerId);
        if (idx < 0) return EditResult.Fail("Marker not found");

        _index = idx;
        _removed = sequence.Markers[idx];
        sequence.Markers.RemoveAt(idx);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_removed == null) return EditResult.Fail("Nothing to undo");
        sequence.Markers.Insert(Math.Clamp(_index, 0, sequence.Markers.Count), _removed);
        _removed = null;
        return EditResult.Ok();
    }
}

public sealed class ClearMarkersCommand : IAtomicEditCommand
{
    public string Description => "Clear all markers";

    private List<Marker>? _saved;

    public EditResult Execute(Sequence sequence)
    {
        _saved = [..sequence.Markers];
        sequence.Markers.Clear();
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_saved == null) return EditResult.Fail("Nothing to undo");
        sequence.Markers.AddRange(_saved);
        _saved = null;
        return EditResult.Ok();
    }
}
