using Rushframe.Domain.Editing;

namespace Rushframe.Domain.Tests;

public sealed class CampaignAndMediaCommandTests
{
    [Fact]
    public void campaign_description_and_tasks_are_exactly_undoable()
    {
        var project = new Project { CampaignDescription = "Old brief" };
        var sequence = project.MainSequence!;
        var history = new UndoRedoStack();
        var task = new CampaignTask { Title = "Select hook" };

        Assert.True(history.Execute(sequence, new UpdateCampaignDescriptionCommand(project, "New brief")).Success);
        Assert.True(history.Execute(sequence, new AddCampaignTaskCommand(project, task)).Success);
        Assert.True(history.Execute(sequence, new UpdateCampaignTaskCommand(project, task.Id, isCompleted: true)).Success);
        Assert.Equal("New brief", project.CampaignDescription);
        Assert.True(Assert.Single(project.Tasks).IsCompleted);

        Assert.True(history.Undo(sequence).Success);
        Assert.False(task.IsCompleted);
        Assert.True(history.Undo(sequence).Success);
        Assert.Empty(project.Tasks);
        Assert.True(history.Undo(sequence).Success);
        Assert.Equal("Old brief", project.CampaignDescription);

        Assert.True(history.Redo(sequence).Success);
        Assert.True(history.Redo(sequence).Success);
        Assert.True(history.Redo(sequence).Success);
        Assert.Equal("New brief", project.CampaignDescription);
        Assert.Same(task, Assert.Single(project.Tasks));
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void delete_campaign_task_restores_original_index()
    {
        var project = new Project();
        var first = new CampaignTask { Title = "First" };
        var middle = new CampaignTask { Title = "Middle" };
        var last = new CampaignTask { Title = "Last" };
        project.Tasks.AddRange([first, middle, last]);
        var command = new DeleteCampaignTaskCommand(project, middle.Id);

        Assert.True(command.Execute(project.MainSequence!).Success);
        Assert.Equal([first, last], project.Tasks);
        Assert.True(command.Undo(project.MainSequence!).Success);
        Assert.Equal([first, middle, last], project.Tasks);
    }

    [Fact]
    public void failed_composite_rolls_back_project_media_registration()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var asset = new MediaAsset { Kind = MediaKind.Image, OriginalPath = "image.png" };
        var command = new CompositeEditCommand("Fail after registration",
        [
            new AddProjectMediaAssetCommand(project, asset),
            new DeleteTrackCommand { TrackId = TrackId.New() },
        ]);
        var history = new UndoRedoStack();

        var result = history.Execute(sequence, command);

        Assert.False(result.Success);
        Assert.Empty(project.MediaLibrary);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void media_registration_participates_in_composite_undo()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var asset = new MediaAsset { Kind = MediaKind.Image, OriginalPath = "image.png" };
        var track = new Track { Kind = TrackKind.Overlay, Name = "O1" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Image,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        };
        var command = new CompositeEditCommand("Add registered image",
        [
            new AddProjectMediaAssetCommand(project, asset),
            new AddPreparedTrackCommand { Track = track },
            new AddClipCommand { TrackId = track.Id, Item = item },
        ]);
        var history = new UndoRedoStack();

        Assert.True(history.Execute(sequence, command).Success);
        Assert.Same(asset, Assert.Single(project.MediaLibrary));
        Assert.Same(item, Assert.Single(Assert.Single(sequence.Tracks).Items));

        Assert.True(history.Undo(sequence).Success);
        Assert.Empty(project.MediaLibrary);
        Assert.Empty(sequence.Tracks);

        Assert.True(history.Redo(sequence).Success);
        Assert.Same(asset, Assert.Single(project.MediaLibrary));
        Assert.Same(item, Assert.Single(Assert.Single(sequence.Tracks).Items));
    }
}
