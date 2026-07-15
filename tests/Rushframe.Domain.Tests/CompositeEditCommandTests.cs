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
        Assert.Same(track, sequence.Tracks[0]);
        Assert.Equal("Video", track.Name);
        Assert.Single(sequence.Tracks);
    }

    [Fact]
    public void composite_exception_restores_original_track_and_item_instances()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "Video" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        };
        track.Items.Add(item);
        sequence.Tracks.Add(track);
        var command = new Editing.CompositeEditCommand(
            "Exceptional update",
            [
                new MutateThenSucceedCommand(track, item),
                new ThrowingCommand(),
            ]);

        var result = command.Execute(sequence);

        Assert.False(result.Success);
        Assert.Same(track, sequence.Tracks[0]);
        Assert.Same(item, sequence.Tracks[0].Items[0]);
        Assert.Equal("Video", track.Name);
        Assert.Equal(MediaTime.Zero, item.TimelineStart);
    }

    [Fact]
    public void failed_composite_undo_restores_pre_undo_state()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "Video" };
        sequence.Tracks.Add(track);
        var command = new Editing.CompositeEditCommand(
            "Undo failure",
            [
                new FailUndoCommand(track),
                new ToggleMuteCommand(track),
            ]);
        Assert.True(command.Execute(sequence).Success);
        Assert.Equal("Changed", track.Name);
        Assert.True(track.Muted);

        var result = command.Undo(sequence);

        Assert.False(result.Success);
        Assert.Same(track, sequence.Tracks[0]);
        Assert.Equal("Changed", track.Name);
        Assert.True(track.Muted);
    }

    [Fact]
    public void undo_redo_history_is_retained_when_a_command_fails()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "Original" };
        sequence.Tracks.Add(track);
        var command = new RetryableCommand(track);
        var history = new Editing.UndoRedoStack();
        Assert.True(history.Execute(sequence, command).Success);

        command.FailUndo = true;
        var failedUndo = history.Undo(sequence);
        Assert.False(failedUndo.Success);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Changed", track.Name);

        command.FailUndo = false;
        Assert.True(history.Undo(sequence).Success);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("Original", track.Name);

        command.FailExecute = true;
        var failedRedo = history.Redo(sequence);
        Assert.False(failedRedo.Success);
        Assert.True(history.CanRedo);
        Assert.Equal("Original", track.Name);

        command.FailExecute = false;
        Assert.True(history.Redo(sequence).Success);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Changed", track.Name);
    }

    private sealed class MutateThenSucceedCommand(Track track, TimelineItem item) : Editing.IEditCommand
    {
        public string Description => "Mutate";
        public Editing.EditResult Execute(Sequence sequence)
        {
            track.Name = "Changed";
            item.TimelineStart = MediaTime.FromSeconds(10);
            return Editing.EditResult.Ok();
        }
        public Editing.EditResult Undo(Sequence sequence) => Editing.EditResult.Ok();
    }

    private sealed class ThrowingCommand : Editing.IEditCommand
    {
        public string Description => "Throw";
        public Editing.EditResult Execute(Sequence sequence) => throw new InvalidOperationException("boom");
        public Editing.EditResult Undo(Sequence sequence) => Editing.EditResult.Ok();
    }

    private sealed class FailUndoCommand(Track track) : Editing.IEditCommand
    {
        public string Description => "Fail undo";
        public Editing.EditResult Execute(Sequence sequence)
        {
            track.Name = "Changed";
            return Editing.EditResult.Ok();
        }
        public Editing.EditResult Undo(Sequence sequence)
        {
            track.Name = "Partial";
            return Editing.EditResult.Fail("undo rejected");
        }
    }

    private sealed class ToggleMuteCommand(Track track) : Editing.IEditCommand
    {
        public string Description => "Toggle mute";
        public Editing.EditResult Execute(Sequence sequence)
        {
            track.Muted = true;
            return Editing.EditResult.Ok();
        }
        public Editing.EditResult Undo(Sequence sequence)
        {
            track.Muted = false;
            return Editing.EditResult.Ok();
        }
    }

    private sealed class RetryableCommand(Track track) : Editing.IEditCommand
    {
        public bool FailExecute { get; set; }
        public bool FailUndo { get; set; }
        public string Description => "Retryable";

        public Editing.EditResult Execute(Sequence sequence)
        {
            track.Name = FailExecute ? "Partial execute" : "Changed";
            return FailExecute ? Editing.EditResult.Fail("execute rejected") : Editing.EditResult.Ok();
        }

        public Editing.EditResult Undo(Sequence sequence)
        {
            track.Name = FailUndo ? "Partial undo" : "Original";
            return FailUndo ? Editing.EditResult.Fail("undo rejected") : Editing.EditResult.Ok();
        }
    }
}
