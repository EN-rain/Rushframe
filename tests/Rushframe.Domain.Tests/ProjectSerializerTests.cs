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

        var track = new Track { Kind = TrackKind.Video, Name = "V1", Order = 1 };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = MediaAssetId.New(),
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
}
