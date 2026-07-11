namespace Rushframe.Domain.Tests;

public sealed class CompositeEditCommandTests
{
    [Fact]
    public void composite_command_is_one_undo_step()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "Video" };
        sequence.Tracks.Add(track);
        var stack = new Editing.UndoRedoStack();
        var command = new Editing.CompositeEditCommand(
            "Update track",
            new Editing.IEditCommand[]
            {
                new Editing.RenameTrackCommand { TrackId = track.Id, NewName = "Main video" },
                new Editing.ToggleTrackMuteCommand { TrackId = track.Id },
            });

        var execute = stack.Execute(sequence, command);
        var undo = stack.Undo(sequence);

        Assert.True(execute.Success);
        Assert.True(undo.Success);
        Assert.Equal("Video", track.Name);
        Assert.False(track.Muted);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void failed_composite_rolls_back_completed_commands()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "Video" };
        sequence.Tracks.Add(track);
        var command = new Editing.CompositeEditCommand(
            "Failing update",
            new Editing.IEditCommand[]
            {
                new Editing.RenameTrackCommand { TrackId = track.Id, NewName = "Changed" },
                new Editing.DeleteTrackCommand { TrackId = TrackId.New() },
            });

        var result = command.Execute(sequence);

        Assert.False(result.Success);
        Assert.Equal("Video", track.Name);
        Assert.Single(sequence.Tracks);
    }
}
