using System.Text.Json;
using Rushframe.Desktop.Controllers;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class AgentEditingContextTests
{
    [Fact]
    public void Edit_skill_catalog_is_unique_complete_and_authoritative()
    {
        Assert.NotEmpty(AgentEditSkillCatalog.Skills);
        Assert.Equal(
            AgentEditSkillCatalog.Skills.Count,
            AgentEditSkillCatalog.Skills.Select(skill => skill.Action).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(AgentEditSkillCatalog.SupportedActions, AgentEditCommandFactory.SupportedActions);

        foreach (var skill in AgentEditSkillCatalog.Skills)
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Action));
            Assert.False(string.IsNullOrWhiteSpace(skill.Category));
            Assert.False(string.IsNullOrWhiteSpace(skill.Summary));
            Assert.Equal(
                skill.Parameters.Count,
                skill.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        var addClip = Assert.Single(AgentEditSkillCatalog.Skills, skill => skill.Action == "add_clip");
        Assert.Contains(addClip.Parameters, parameter => parameter.Name == "media_asset_id" && parameter.Required);
        var animation = Assert.Single(AgentEditSkillCatalog.Skills, skill => skill.Action == "set_animation_channels");
        Assert.Contains(animation.Parameters, parameter => parameter.Name == "channels" && parameter.Required);
        Assert.Contains(animation.Warnings, warning => warning.Contains("replaces", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Editing_context_is_bounded_and_keeps_selected_item()
    {
        var project = new Project
        {
            CampaignDescription = new string('x', 5000),
        };
        for (var index = 0; index < 105; index++)
            project.Tasks.Add(new CampaignTask { Title = $"Task {index}" });
        var sequence = project.MainSequence!;
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        sequence.Tracks.Add(track);
        for (var index = 0; index < 30; index++)
        {
            track.Items.Add(new TimelineItem
            {
                Kind = ItemKind.Clip,
                TimelineStart = MediaTime.FromSeconds(index),
                Duration = MediaTime.FromSeconds(0.8),
                SourceDuration = MediaTime.FromSeconds(0.8),
            });
        }
        var selected = track.Items[^1];

        var context = AgentEditingContextBuilder.Build(
            project,
            sequence,
            playheadSeconds: 12.5,
            selected.Id,
            new AgentEditingContextRequest(ItemLimit: 25));

        Assert.Equal(AgentEditingContextBuilder.SchemaVersion, context.ContextSchemaVersion);
        Assert.Equal(project.Revision, context.Revision);
        Assert.Equal(12.5, context.PlayheadSeconds, 3);
        Assert.Equal(4000, context.CampaignDescription.Length);
        Assert.True(context.TasksTruncated);
        Assert.Equal(100, context.Tasks.Count);
        Assert.Equal(selected.Id.ToString(), context.SelectedItemId);
        Assert.True(context.TimelineItemsTruncated);
        var contextTrack = Assert.Single(context.Tracks);
        Assert.True(contextTrack.ItemsTruncated);
        Assert.Equal(25, contextTrack.Items.Count);
        Assert.Contains(contextTrack.Items, item => item.Id == selected.Id.ToString());
        Assert.Equal(AgentEditSkillCatalog.Skills.Count, context.EditSkills.ActionCount);
        Assert.Contains("timing", context.EditSkills.Categories.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Edit_plan_quality_is_scored_against_the_projected_timeline()
    {
        var project = new Project();
        project.EditingBrief.CallToAction = "Buy now";
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());

        var plan = compiler.Compile(project, project.MainSequence!, Json("""
        {
          "operations": [
            { "action": "add_text", "text": "Buy now", "start": 0, "duration": 2 }
          ]
        }
        """), 0);

        Assert.True(plan.Success);
        Assert.DoesNotContain(plan.QualityIssues, issue => issue.Code == "cta-missing");
        Assert.Empty(project.MainSequence!.Tracks);
    }

    [Fact]
    public void Edit_plan_projection_rejects_operations_invalidated_by_an_earlier_operation()
    {
        var project = new Project();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        project.MainSequence!.Tracks.Add(track);
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());

        var plan = compiler.Compile(project, project.MainSequence, Json($$"""
        {
          "operations": [
            { "action": "toggle_track_lock", "track_id": "{{track.Id}}" },
            { "action": "rename_track", "track_id": "{{track.Id}}", "name": "Locked rename" }
          ]
        }
        """), 0);

        Assert.False(plan.Success);
        Assert.Contains("cannot be applied atomically", plan.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(track.Locked);
        Assert.Equal("V1", track.Name);
    }

    [Fact]
    public void Editing_context_reports_media_readiness_usage_and_brief_constraints_without_paths()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = Path.Combine("C:\\media", "registered-clip.mp4"),
            RelativeProjectPath = "registered-clip.mp4",
            Duration = MediaTime.FromSeconds(8),
            PixelWidth = 1920,
            PixelHeight = 1080,
        };
        project.MediaLibrary.Add(asset);
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis
        {
            MediaAssetId = asset.Id,
            Metadata = new MediaIntelligenceTechnicalMetadata
            {
                Duration = asset.Duration,
                HasVideo = true,
                HasAudio = true,
            },
        });
        project.EditingBrief.RequiredMediaAssetIds.Add(asset.Id);
        var track = new Track { Kind = TrackKind.Video };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(4),
            SourceDuration = MediaTime.FromSeconds(4),
        });
        sequence.Tracks.Add(track);

        var context = AgentEditingContextBuilder.Build(project, sequence, 0, null, new AgentEditingContextRequest());

        var media = Assert.Single(context.MediaAssets);
        Assert.Equal(asset.Id.ToString(), media.Id);
        Assert.Equal("registered-clip.mp4", media.Name);
        Assert.DoesNotContain('\\', media.Name);
        Assert.DoesNotContain('/', media.Name);
        Assert.True(media.Analyzed);
        Assert.Equal(1, media.TimelineUsageCount);
        Assert.True(media.RequiredByBrief);
        Assert.False(media.ForbiddenByBrief);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
