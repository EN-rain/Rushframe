using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class EditingQualityTests
{
    [Fact]
    public void Editing_brief_round_trips_and_version_three_migrates()
    {
        var project = new Project();
        project.EditingBrief.Purpose = "Launch product";
        project.EditingBrief.TargetDurationSeconds = 30;
        project.EditingBrief.EditingStyle = "product-ad";
        project.EditingBrief.RequiredMessages.Add("Fast setup");

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(Project.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal("Launch product", restored.EditingBrief.Purpose);
        Assert.Equal(30, restored.EditingBrief.TargetDurationSeconds);
        Assert.Equal("product-ad", restored.EditingBrief.EditingStyle);
        Assert.Contains("Fast setup", restored.EditingBrief.RequiredMessages);
    }

    [Fact]
    public void Update_editing_brief_is_undoable()
    {
        var project = new Project();
        project.EditingBrief.Purpose = "Old";
        var command = new UpdateEditingBriefCommand(project, new EditingBrief
        {
            Purpose = "New",
            EditingStyle = "tutorial",
            TargetDurationSeconds = 45,
        });

        Assert.True(command.Execute(project.MainSequence!).Success);
        Assert.Equal("New", project.EditingBrief.Purpose);
        Assert.True(command.Undo(project.MainSequence!).Success);
        Assert.Equal("Old", project.EditingBrief.Purpose);
    }

    [Fact]
    public void Timeline_quality_reports_duration_caption_and_repeated_source()
    {
        var project = new Project();
        project.EditingBrief.TargetDurationSeconds = 10;
        project.EditingBrief.EditingStyle = "social-highlight";
        var sequence = project.MainSequence!;
        var assetId = MediaAssetId.New();
        var track = new Track { Kind = TrackKind.Video };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            TimelineStart = MediaTime.FromSeconds(2),
            Duration = MediaTime.FromSeconds(0.1),
            SourceStart = MediaTime.Zero,
            SourceDuration = MediaTime.FromSeconds(1),
        });
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            TimelineStart = MediaTime.FromSeconds(3),
            Duration = MediaTime.FromSeconds(1),
            SourceStart = MediaTime.Zero,
            SourceDuration = MediaTime.FromSeconds(1),
        });
        sequence.Tracks.Add(track);
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Text,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Text,
                    TimelineStart = MediaTime.Zero,
                    Duration = MediaTime.FromSeconds(0.5),
                    TextContent = new string('x', 60),
                },
            },
        });

        var issues = TimelineQualityAnalyzer.Analyze(project, sequence);

        Assert.Contains(issues, issue => issue.Code == "duration-target");
        Assert.Contains(issues, issue => issue.Code == "short-item");
        Assert.Contains(issues, issue => issue.Code == "caption-reading-speed");
        Assert.Contains(issues, issue => issue.Code == "repeated-source-range");
    }
}
