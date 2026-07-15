using System.Text.Json;
using Rushframe.Desktop.Controllers;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;
using Rushframe.Media.Native;

namespace Rushframe.Desktop.Tests;

public sealed class AgentAutomationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rushframe-agent-automation", Guid.NewGuid().ToString("N"));

    public AgentAutomationTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Edit_plan_compiles_multiple_actions_into_one_atomic_command()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var factory = new AgentEditCommandFactory();
        var compiler = new AgentEditPlanCompiler(factory);
        var payload = Json("""
        {
          "summary": "Add title and review marker",
          "operations": [
            { "action": "add_text", "text": "Launch", "start": 0, "duration": 2 },
            { "action": "add_marker", "label": "Review", "time": 1 }
          ]
        }
        """);

        var compiled = compiler.Compile(project, sequence, payload, 0);
        var stack = new UndoRedoStack();
        var result = stack.Execute(sequence, compiled.Command!);

        Assert.True(compiled.Success);
        Assert.Equal(2, compiled.Operations.Count);
        Assert.True(result.Success);
        var textTrack = Assert.Single(sequence.Tracks, track => track.Kind == TrackKind.Text);
        Assert.Equal("Launch", Assert.Single(textTrack.Items).TextContent);
        Assert.Single(sequence.Markers);
        Assert.True(stack.Undo(sequence).Success);
        Assert.Empty(sequence.Tracks);
        Assert.Empty(sequence.Markers);
    }

    [Fact]
    public void Edit_plan_rejects_locked_target_before_execution()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var item = new TimelineItem { Locked = true, Duration = MediaTime.FromSeconds(2) };
        sequence.Tracks.Add(new Track { Kind = TrackKind.Video, Items = { item } });
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());
        var payload = Json($$"""
        {
          "operations": [
            { "action": "delete_clip", "item_id": "{{item.Id}}" }
          ]
        }
        """);

        var compiled = compiler.Compile(project, sequence, payload, 0);

        Assert.False(compiled.Success);
        Assert.Contains("locked", compiled.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(sequence.Tracks[0].Items);
    }

    [Theory]
    [InlineData("rename_track")]
    [InlineData("reorder_track")]
    public void Edit_plan_rejects_locked_track_metadata_mutations(string action)
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var track = new Track { Kind = TrackKind.Video, Name = "Locked", Locked = true };
        sequence.Tracks.Add(track);
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());
        var payload = action == "rename_track"
            ? Json($$"""{ "operations": [{ "action": "rename_track", "track_id": "{{track.Id}}", "name": "Renamed" }] }""")
            : Json($$"""{ "operations": [{ "action": "reorder_track", "track_id": "{{track.Id}}", "new_index": 0 }] }""");

        var compiled = compiler.Compile(project, sequence, payload, 0);

        Assert.False(compiled.Success);
        Assert.Contains("locked", compiled.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Locked", track.Name);
        Assert.Single(sequence.Tracks);
    }

    [Fact]
    public void Transcript_caption_action_creates_text_track_when_missing()
    {
        var mediaPath = Path.Combine(_root, "speech.mp4");
        File.WriteAllBytes(mediaPath, [0x00]);
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = mediaPath,
            RelativeProjectPath = mediaPath,
            Duration = MediaTime.FromSeconds(5),
        };
        project.MediaLibrary.Add(asset);
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis
        {
            MediaAssetId = asset.Id,
            Metadata = new MediaIntelligenceTechnicalMetadata { Duration = MediaTime.FromSeconds(5), HasAudio = true, HasVideo = true },
            Transcript =
            {
                new MediaIntelligenceTranscriptSegment
                {
                    SegmentId = "s1",
                    Start = MediaTime.Zero,
                    End = MediaTime.FromSeconds(2),
                    Text = "hello rushframe",
                    Words =
                    {
                        new MediaIntelligenceWord { Start = MediaTime.Zero, End = MediaTime.FromSeconds(0.8), Text = "hello" },
                        new MediaIntelligenceWord { Start = MediaTime.FromSeconds(0.9), End = MediaTime.FromSeconds(1.8), Text = "rushframe" },
                    },
                },
            },
        });
        var payload = Json($$"""
        {
          "media_asset_id": "{{asset.Id}}",
          "source_start": 0,
          "source_end": 2,
          "timeline_start": 3,
          "words_per_chunk": 2
        }
        """);
        var factory = new AgentEditCommandFactory();

        var build = factory.Build(project, project.MainSequence!, payload, "add_captions_from_transcript", 0);
        var result = new UndoRedoStack().Execute(project.MainSequence!, build.Command!);

        Assert.True(build.Success);
        Assert.True(result.Success);
        var track = Assert.Single(project.MainSequence!.Tracks, value => value.Kind == TrackKind.Text);
        var caption = Assert.Single(track.Items);
        Assert.Equal("hello rushframe", caption.TextContent);
        Assert.Equal(3, caption.TimelineStart.Seconds, 3);
        Assert.Equal(asset.Id, caption.MediaIntelligenceSourceAssetId);
    }

    [Fact]
    public void Composition_validation_blocks_custom_execution_and_path_escape()
    {
        var projectDirectory = Path.Combine(_root, "composition");
        Directory.CreateDirectory(projectDirectory);
        var service = new ExternalCompositionService(new FfmpegMediaService());
        var custom = service.Validate(new ExternalCompositionSpec
        {
            Name = "Unsafe",
            Kind = ExternalCompositionKind.Custom,
            ProjectDirectory = projectDirectory,
            OutputPath = "render.mp4",
            Width = 1920,
            Height = 1080,
            DurationSeconds = 5,
        }, null);
        var escaped = service.Validate(new ExternalCompositionSpec
        {
            Name = "Escape",
            Kind = ExternalCompositionKind.Remotion,
            ProjectDirectory = projectDirectory,
            EntryPoint = "src/index.ts",
            CompositionId = "Main",
            OutputPath = Path.Combine("..", "outside.mp4"),
            Width = 1920,
            Height = 1080,
            DurationSeconds = 5,
        }, null);

        Assert.False(custom.Success);
        Assert.Contains(custom.Errors, error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
        Assert.False(escaped.Success);
        Assert.Contains(escaped.Errors, error => error.Contains("inside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Composition_validation_requires_project_local_installed_binary()
    {
        var projectDirectory = Path.Combine(_root, "remotion");
        Directory.CreateDirectory(projectDirectory);
        var service = new ExternalCompositionService(new FfmpegMediaService());

        var validation = service.Validate(new ExternalCompositionSpec
        {
            Name = "Intro",
            Kind = ExternalCompositionKind.Remotion,
            ProjectDirectory = projectDirectory,
            EntryPoint = "src/index.ts",
            CompositionId = "Main",
            OutputPath = "renders/intro.mp4",
            Width = 1920,
            Height = 1080,
            DurationSeconds = 5,
        }, null);

        Assert.False(validation.Success);
        Assert.Contains(validation.Errors, error => error.Contains("not installed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Errors, error => error.Contains("will not download", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Text_font_path_must_be_a_registered_project_asset()
    {
        var fontPath = Path.Combine(_root, "custom.ttf");
        File.WriteAllBytes(fontPath, [0x00]);
        var project = new Project();
        var factory = new AgentEditCommandFactory();
        var rejected = factory.Build(project, project.MainSequence!, Json($$"""
        {
          "text": "Unsafe font",
          "font_family": "{{fontPath.Replace("\\", "\\\\")}}"
        }
        """), "add_text", 0);

        project.MediaLibrary.Add(new MediaAsset
        {
            Kind = MediaKind.Font,
            OriginalPath = fontPath,
            RelativeProjectPath = fontPath,
        });
        var accepted = factory.Build(project, project.MainSequence!, Json($$"""
        {
          "text": "Registered font",
          "font_family": "{{fontPath.Replace("\\", "\\\\")}}"
        }
        """), "add_text", 0);

        Assert.False(rejected.Success);
        Assert.Contains("registered", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(accepted.Success);
    }

    [Fact]
    public void Agent_numeric_payload_rejects_non_finite_text_values()
    {
        var project = new Project();
        var build = new AgentEditCommandFactory().Build(
            project,
            project.MainSequence!,
            Json("""{ "text": "Bad size", "font_size": "NaN" }"""),
            "add_text",
            0);

        Assert.False(build.Success);
        Assert.Contains("finite", build.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Applied_agent_plan_record_and_workflow_follow_undo_and_redo()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());
        var plan = compiler.Compile(project, sequence, Json("""
        {
          "summary": "Add approved title",
          "operations": [
            { "action": "add_text", "text": "Approved", "start": 0, "duration": 2 }
          ]
        }
        """), 0);
        Assert.True(plan.Success);
        var initialStageCount = project.Workflow.Stages.Count;
        var initialActiveStage = project.Workflow.ActiveStageId;
        var initialAgentDraftStatus = project.Workflow.Stages.Single(stage => stage.Id == "agent_draft").Status;
        var command = new AgentPlanApplicationCommand(project, plan, project.Revision);
        var history = new UndoRedoStack();

        Assert.True(history.Execute(sequence, command).Success);
        project.IncrementRevision();
        Assert.Single(project.AgentEditPlans);
        Assert.Equal(AgentEditPlanStatus.Applied, project.AgentEditPlans[0].Status);
        Assert.Equal("agent_draft", project.Workflow.ActiveStageId);
        Assert.Single(sequence.Tracks.SelectMany(track => track.Items));

        Assert.True(history.Undo(sequence).Success);
        project.IncrementRevision();
        Assert.Empty(project.AgentEditPlans);
        Assert.Equal(initialStageCount, project.Workflow.Stages.Count);
        Assert.Equal(initialActiveStage, project.Workflow.ActiveStageId);
        Assert.Equal(initialAgentDraftStatus, project.Workflow.Stages.Single(stage => stage.Id == "agent_draft").Status);
        Assert.Empty(sequence.Tracks);

        Assert.True(history.Redo(sequence).Success);
        project.IncrementRevision();
        Assert.Single(project.AgentEditPlans);
        Assert.Equal(project.Revision, project.AgentEditPlans[0].AppliedRevision);
        Assert.Equal("agent_draft", project.Workflow.ActiveStageId);
        Assert.Single(sequence.Tracks.SelectMany(track => track.Items));
    }

    [Fact]
    public void Isolated_plan_review_mutates_only_the_snapshot()
    {
        var liveProject = new Project();
        var liveSequence = liveProject.MainSequence!;
        var liveRevision = liveProject.Revision;
        var snapshot = ProjectSerializer.CreateSnapshot(liveProject);
        var snapshotSequence = snapshot.MainSequence!;
        var compiler = new AgentEditPlanCompiler(new AgentEditCommandFactory());
        var plan = compiler.Compile(snapshot, snapshotSequence, Json("""
        {
          "summary": "Review title",
          "operations": [
            { "action": "add_text", "text": "Draft only", "start": 0, "duration": 2 }
          ]
        }
        """), 0);

        Assert.True(plan.Success);
        Assert.True(plan.Command!.Execute(snapshotSequence).Success);
        snapshot.IncrementRevision();

        Assert.Empty(liveSequence.Tracks);
        Assert.Equal(liveRevision, liveProject.Revision);
        var snapshotTrack = Assert.Single(snapshotSequence.Tracks);
        Assert.Equal("Draft only", Assert.Single(snapshotTrack.Items).TextContent);
        Assert.Equal(liveRevision + 1, snapshot.Revision);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
