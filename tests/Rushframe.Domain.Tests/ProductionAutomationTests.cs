using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class ProductionAutomationTests
{
    [Fact]
    public void Serialize_adds_schema_three_workflow_and_primary_variant()
    {
        var project = new Project { Name = "Automation" };

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(3, restored.SchemaVersion);
        Assert.Equal("brief", restored.Workflow.ActiveStageId);
        Assert.Equal(7, restored.Workflow.Stages.Count);
        var variant = Assert.Single(restored.ExportVariants);
        Assert.Equal("Primary", variant.Name);
        Assert.Equal(restored.MainSequence!.Id, variant.SequenceId);
        Assert.Equal(3, restored.TranscriptEditPolicy.CaptionWordsPerChunk);
    }

    [Fact]
    public void Version_two_project_migrates_to_automation_schema()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "name": "Version Two",
          "sequences": [{
            "name": "Main",
            "width": 1280,
            "height": 720,
            "fps": 30,
            "tracks": [],
            "markers": [],
            "transitions": []
          }],
          "mediaLibrary": [],
          "mediaIntelligence": [],
          "campaignDescription": "",
          "tasks": []
        }
        """;

        var project = ProjectSerializer.Deserialize(json);

        Assert.Equal(Project.CurrentSchemaVersion, project.SchemaVersion);
        Assert.Contains(project.Workflow.Stages, stage => stage.Id == "final_qa");
        Assert.Single(project.ExportVariants);
        Assert.Empty(project.ExternalCompositions);
        Assert.Empty(project.AgentEditPlans);
        Assert.Empty(project.RenderReceipts);
    }

    [Fact]
    public void Replace_track_items_is_atomic_and_undoable()
    {
        var sequence = new Sequence();
        var original = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(10),
            SourceDuration = MediaTime.FromSeconds(10),
        };
        var track = new Track { Kind = TrackKind.Video, Items = { original } };
        sequence.Tracks.Add(track);
        var replacements = new[]
        {
            new TimelineItem
            {
                Kind = ItemKind.Clip,
                TimelineStart = MediaTime.Zero,
                Duration = MediaTime.FromSeconds(3),
                SourceDuration = MediaTime.FromSeconds(3),
            },
            new TimelineItem
            {
                Kind = ItemKind.Clip,
                TimelineStart = MediaTime.FromSeconds(3),
                Duration = MediaTime.FromSeconds(2),
                SourceStart = MediaTime.FromSeconds(6),
                SourceDuration = MediaTime.FromSeconds(2),
            },
        };
        var command = new ReplaceTrackItemsCommand { TrackId = track.Id, NewItems = replacements };

        Assert.True(command.Execute(sequence).Success);
        Assert.Equal(2, track.Items.Count);
        Assert.Equal(5, sequence.Duration.Seconds, 3);
        Assert.True(command.Undo(sequence).Success);
        var restored = Assert.Single(track.Items);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(10, restored.Duration.Seconds, 3);
    }

    [Fact]
    public void Replace_track_items_refuses_locked_content_without_partial_change()
    {
        var sequence = new Sequence();
        var locked = new TimelineItem { Locked = true, Duration = MediaTime.FromSeconds(2) };
        var track = new Track { Kind = TrackKind.Video, Items = { locked } };
        sequence.Tracks.Add(track);
        var command = new ReplaceTrackItemsCommand
        {
            TrackId = track.Id,
            NewItems = [new TimelineItem { Duration = MediaTime.FromSeconds(1) }],
        };

        var result = command.Execute(sequence);

        Assert.False(result.Success);
        Assert.Single(track.Items);
        Assert.Equal(locked.Id, track.Items[0].Id);
    }

    [Fact]
    public void Overview_surfaces_workflow_render_and_composition_failures()
    {
        var project = new Project();
        project.Workflow.EnsureDefaults();
        var review = project.Workflow.Stages.First(stage => stage.Id == "human_review");
        review.Status = ProductionStageStatus.AwaitingApproval;
        project.ExternalCompositions.Add(new ExternalCompositionSpec
        {
            Name = "Broken intro",
            Kind = ExternalCompositionKind.Remotion,
            Status = ExternalCompositionStatus.Failed,
            LastError = "Renderer unavailable",
            Width = 1920,
            Height = 1080,
            DurationSeconds = 5,
        });
        project.RenderReceipts.Add(new RenderReceiptReference
        {
            OutputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4"),
            ReceiptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            VerificationStatus = RenderVerificationStatus.Failed,
            ProjectRevision = 4,
        });

        var overview = ProjectOverviewBuilder.Build(project);

        Assert.Equal("brief", overview.ActiveWorkflowStageId);
        Assert.Contains(overview.ReviewHints, hint => hint.Contains("waiting for human approval", StringComparison.Ordinal));
        Assert.Contains(overview.ReviewHints, hint => hint.Contains("Broken intro", StringComparison.Ordinal));
        Assert.Contains(overview.ReviewHints, hint => hint.Contains("export verification failed", StringComparison.Ordinal));
        Assert.Single(overview.ExternalCompositions);
        Assert.Single(overview.RenderReceipts);
    }
}
