using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class ProjectSerializerTests
{
    [Fact]
    public void save_reload_is_equivalent()
    {
        var project = new Project { Name = "Test Project" };
        var seq = project.MainSequence!;
        seq.Name = "Main Seq";
        seq.Width = 1920;
        seq.Height = 1080;

        var assetId = MediaAssetId.New();
        var track = new Track { Kind = TrackKind.Video, Name = "V1", Order = 1 };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            TimelineStart = MediaTime.FromSeconds(2),
            Duration = MediaTime.FromSeconds(10),
            SourceStart = MediaTime.Zero,
            SourceDuration = MediaTime.FromSeconds(15),
            Speed = 1.5,
            Volume = 0.8,
            Opacity = 0.9,
            Transform = new Transform2D { PositionX = 100, PositionY = 200 },
        };
        track.Items.Add(item);
        seq.Tracks.Add(track);

        var asset = new MediaAsset
        {
            Id = assetId,
            Kind = MediaKind.Video,
            OriginalPath = @"C:\clips\test.mp4",
            RelativeProjectPath = "clips/test.mp4",
            Duration = MediaTime.FromSeconds(60),
        };
        project.MediaLibrary.Add(asset);

        var json = ProjectSerializer.Serialize(project);
        var restored = ProjectSerializer.Deserialize(json);

        Assert.Equal(project.Name, restored.Name);
        Assert.Single(restored.Sequences);
        Assert.Single(restored.MainSequence!.Tracks);
        Assert.Single(restored.MediaLibrary);

        var restoredTrack = restored.MainSequence.Tracks[0];
        Assert.Equal("V1", restoredTrack.Name);
        Assert.Equal(TrackKind.Video, restoredTrack.Kind);

        var restoredItem = restoredTrack.Items[0];
        Assert.Equal(2, restoredItem.TimelineStart.Seconds, 3);
        Assert.Equal(10, restoredItem.Duration.Seconds, 3);
        Assert.Equal(1.5, restoredItem.Speed);
        Assert.Equal(0.8, restoredItem.Volume);
        Assert.Equal(0.9, restoredItem.Opacity);
        Assert.Equal(100, restoredItem.Transform.PositionX);
    }

    [Fact]
    public void unknown_extension_data_survives_round_trip()
    {
        var project = new Project { Name = "Extension Test" };
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem { Duration = MediaTime.FromSeconds(5) };
        track.Items.Add(item);
        project.MainSequence!.Tracks.Add(track);

        var json = ProjectSerializer.Serialize(project);
        var restored = ProjectSerializer.Deserialize(json);

        Assert.Equal("Extension Test", restored.Name);
        Assert.Single(restored.MainSequence!.Tracks[0].Items);
        Assert.Equal(5, restored.MainSequence.Tracks[0].Items[0].Duration.Seconds, 3);
    }

    [Fact]
    public void legacy_project_is_migrated_to_current_schema()
    {
        const string legacy = """
        {
          "name": "Legacy",
          "sequences": [
            {
              "name": "Main",
              "width": 1920,
              "height": 1080,
              "fps": 29.97,
              "tracks": [],
              "markers": [],
              "transitions": []
            }
          ],
          "mediaLibrary": [],
          "mediaIntelligence": [],
          "campaignDescription": "",
          "tasks": []
        }
        """;

        var project = ProjectSerializer.Deserialize(legacy);

        Assert.Equal(Project.CurrentSchemaVersion, project.SchemaVersion);
        Assert.Equal(FrameRate.Fps29_97, project.MainSequence!.FrameRate);
        Assert.NotNull(project.MainSequence.Background);
    }

    [Fact]
    public void multi_channel_animation_and_presentation_round_trip()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.FrameRate = FrameRate.Fps23_976;
        sequence.Background = new CanvasBackground
        {
            Kind = CanvasBackgroundKind.LinearGradient,
            PrimaryColor = "#112233",
            SecondaryColor = "#445566",
        };
        sequence.LayoutGuides.Add(new LayoutGuide { Kind = LayoutGuideKind.YouTubeShorts, Name = "Shorts" });
        var item = new TimelineItem { Kind = ItemKind.Image, Duration = MediaTime.FromSeconds(2) };
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.Opacity,
            DefaultValue = 1,
            Keyframes =
            {
                new Keyframe { Time = MediaTime.Zero, Value = 0 },
                new Keyframe { Time = MediaTime.FromSeconds(2), Value = 1 },
            },
        });
        sequence.Tracks.Add(new Track { Kind = TrackKind.Overlay, Name = "O1", Items = { item } });

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(FrameRate.Fps23_976, restored.MainSequence!.FrameRate);
        Assert.Equal(CanvasBackgroundKind.LinearGradient, restored.MainSequence.Background.Kind);
        Assert.Single(restored.MainSequence.LayoutGuides);
        Assert.Single(restored.MainSequence.Tracks[0].Items[0].AnimationChannels);
    }

    [Fact]
    public void serialized_json_contains_media_time_fields()
    {
        var project = new Project { Name = "MediaTime Test" };
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem
        {
            TimelineStart = MediaTime.FromSeconds(1.5),
            Duration = MediaTime.FromSeconds(3.25),
        };
        track.Items.Add(item);
        project.MainSequence!.Tracks.Add(track);

        var json = ProjectSerializer.Serialize(project);

        Assert.Contains("numerator", json);
        Assert.Contains("denominator", json);
    }

    [Fact]
    public void migration_missing_modified_time_is_deterministic()
    {
        const string legacy = """
        {
          "schemaVersion": 0,
          "name": "Legacy",
          "sequences": []
        }
        """;

        var first = ProjectMigrationPipeline.MigrateToCurrent(legacy);
        var second = ProjectMigrationPipeline.MigrateToCurrent(legacy);
        var restored = ProjectSerializer.Deserialize(first);

        Assert.Equal(first, second);
        Assert.Equal(DateTimeOffset.UnixEpoch, restored.ModifiedUtc);
    }

    [Fact]
    public void migration_prefers_existing_created_time_for_missing_modified_time()
    {
        const string legacy = """
        {
          "schemaVersion": 0,
          "name": "Legacy",
          "createdUtc": "2024-01-02T03:04:05+00:00",
          "sequences": []
        }
        """;

        var restored = ProjectSerializer.Deserialize(ProjectMigrationPipeline.MigrateToCurrent(legacy));

        Assert.Equal(DateTimeOffset.Parse("2024-01-02T03:04:05+00:00"), restored.ModifiedUtc);
    }

    [Fact]
    public void deserialize_repairs_safe_numeric_and_order_invariants()
    {
        var project = new Project();
        var track = new Track { Kind = TrackKind.Text, Name = "T1", Order = 9 };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = "Repair me",
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        });
        project.MainSequence!.Tracks.Add(track);
        var root = System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(project))!.AsObject();
        var sequence = root["sequences"]![0]!.AsObject();
        sequence["width"] = 0;
        sequence["height"] = 99_999;
        var trackNode = sequence["tracks"]![0]!.AsObject();
        trackNode["order"] = 42;
        var item = trackNode["items"]![0]!.AsObject();
        item["timelineStart"]!["numerator"] = -120_000;
        item["duration"]!["numerator"] = 0;
        item["sourceStart"]!["numerator"] = -10;
        item["sourceDuration"]!["numerator"] = 0;
        item["speed"] = 0;
        item["volume"] = 9;
        item["opacity"] = 2;
        item["cropLeft"] = 0.8;
        item["cropRight"] = 0.8;
        item["transform"]!["scaleX"] = 0;

        var restored = ProjectSerializer.Deserialize(root.ToJsonString());
        var restoredSequence = restored.MainSequence!;
        var restoredTrack = Assert.Single(restoredSequence.Tracks);
        var restoredItem = Assert.Single(restoredTrack.Items);

        Assert.Equal(2, restoredSequence.Width);
        Assert.Equal(16_384, restoredSequence.Height);
        Assert.Equal(0, restoredTrack.Order);
        Assert.Equal(MediaTime.Zero, restoredItem.TimelineStart);
        Assert.True(restoredItem.Duration > MediaTime.Zero);
        Assert.True(restoredItem.SourceDuration > MediaTime.Zero);
        Assert.Equal(0.1, restoredItem.Speed, 3);
        Assert.Equal(4, restoredItem.Volume, 3);
        Assert.Equal(1, restoredItem.Opacity, 3);
        Assert.True(restoredItem.CropLeft + restoredItem.CropRight < 0.999);
        Assert.Equal(0.001, restoredItem.Transform.ScaleX, 6);
    }

    [Fact]
    public void deserialize_rejects_duplicate_item_ids()
    {
        var project = new Project();
        var track = new Track { Kind = TrackKind.Text, Name = "T1" };
        track.Items.Add(new TimelineItem { Kind = ItemKind.Text, TextContent = "one", Duration = MediaTime.FromSeconds(1), SourceDuration = MediaTime.FromSeconds(1) });
        track.Items.Add(new TimelineItem { Kind = ItemKind.Text, TextContent = "two", Duration = MediaTime.FromSeconds(1), SourceDuration = MediaTime.FromSeconds(1) });
        project.MainSequence!.Tracks.Add(track);
        var root = System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(project))!.AsObject();
        var items = root["sequences"]![0]!["tracks"]![0]!["items"]!.AsArray();
        items[1]!["id"] = items[0]!["id"]!.DeepClone();

        var error = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(root.ToJsonString()));

        Assert.Contains("Duplicate timeline item ID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void deserialize_rejects_missing_media_references()
    {
        var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "clip.mp4", Duration = MediaTime.FromSeconds(2) };
        var project = new Project();
        project.MediaLibrary.Add(asset);
        project.MainSequence!.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Duration = MediaTime.FromSeconds(2),
                    SourceDuration = MediaTime.FromSeconds(2),
                },
            },
        });
        var root = System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(project))!.AsObject();
        root["mediaLibrary"] = new System.Text.Json.Nodes.JsonArray();

        var error = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(root.ToJsonString()));

        Assert.Contains("references missing media asset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void deserialize_removes_dangling_transitions()
    {
        var project = new Project();
        var first = new TimelineItem { Kind = ItemKind.Text, TextContent = "one", Duration = MediaTime.FromSeconds(1), SourceDuration = MediaTime.FromSeconds(1) };
        var second = new TimelineItem { Kind = ItemKind.Text, TextContent = "two", TimelineStart = MediaTime.FromSeconds(1), Duration = MediaTime.FromSeconds(1), SourceDuration = MediaTime.FromSeconds(1) };
        project.MainSequence!.Tracks.Add(new Track { Kind = TrackKind.Text, Name = "T1", Items = { first, second } });
        project.MainSequence.Transitions.Add(new Transition
        {
            LeftItemId = first.Id,
            RightItemId = second.Id,
            Duration = MediaTime.FromSeconds(0.25),
        });
        var root = System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(project))!.AsObject();
        root["sequences"]![0]!["tracks"]![0]!["items"]!.AsArray().RemoveAt(1);

        var restored = ProjectSerializer.Deserialize(root.ToJsonString());

        Assert.Empty(restored.MainSequence!.Transitions);
        Assert.Single(restored.MainSequence.Tracks[0].Items);
    }

    [Fact]
    public void serialize_normalizes_only_an_isolated_snapshot()
    {
        var project = new Project { SchemaVersion = 1 };
        project.AutomationProviders.Clear();
        project.ExportVariants.Clear();
        project.Workflow.Stages.Clear();
        project.Overview.SequenceCount = 99;

        var json = ProjectSerializer.Serialize(project);
        var persisted = ProjectSerializer.Deserialize(json);

        Assert.Equal(1, project.SchemaVersion);
        Assert.Empty(project.AutomationProviders);
        Assert.Empty(project.ExportVariants);
        Assert.Empty(project.Workflow.Stages);
        Assert.Equal(99, project.Overview.SequenceCount);
        Assert.Equal(Project.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.NotEmpty(persisted.AutomationProviders);
        Assert.NotEmpty(persisted.ExportVariants);
        Assert.NotEmpty(persisted.Workflow.Stages);
    }
}
