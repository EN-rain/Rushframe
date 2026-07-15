using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class ApplyMediaIntelligenceCommandTests
{
    [Fact]
    public void execute_maps_source_times_to_trimmed_speed_adjusted_timeline()
    {
        var assetId = MediaAssetId.New();
        var sequence = new Sequence();
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "V1" };
        var target = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            TimelineStart = MediaTime.FromSeconds(10),
            Duration = MediaTime.FromSeconds(5),
            SourceStart = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(10),
            Speed = 2,
        };
        videoTrack.Items.Add(target);
        sequence.Tracks.Add(videoTrack);

        var analysis = new MediaIntelligenceAnalysis
        {
            MediaAssetId = assetId,
            Scenes =
            [
                new() { SceneId = "before", Start = MediaTime.FromSeconds(4), End = MediaTime.FromSeconds(5) },
                new() { SceneId = "inside", Start = MediaTime.FromSeconds(7), End = MediaTime.FromSeconds(9), Description = "Close-up" },
            ],
            Transcript =
            [
                new() { Start = MediaTime.FromSeconds(6), End = MediaTime.FromSeconds(8), Text = "Hello world" },
            ],
        };

        var command = new ApplyMediaIntelligenceCommand
        {
            TargetItemId = target.Id,
            Analysis = analysis,
        };

        Assert.True(command.Execute(sequence).Success);
        Assert.Single(sequence.Markers);
        Assert.Equal(11, sequence.Markers[0].Time.Seconds, 3);
        Assert.Equal("Close-up", sequence.Markers[0].Label);

        var captionTrack = Assert.Single(sequence.Tracks, track => track.Kind == TrackKind.Text);
        var caption = Assert.Single(captionTrack.Items);
        Assert.Equal(10.5, caption.TimelineStart.Seconds, 3);
        Assert.Equal(1, caption.Duration.Seconds, 3);
        Assert.Equal("Hello world", caption.TextContent);
    }

    [Fact]
    public void execute_replaces_previous_generated_content_and_undo_restores_it()
    {
        var assetId = MediaAssetId.New();
        var sequence = new Sequence();
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "V1" };
        var textTrack = new Track { Kind = TrackKind.Text, Name = "AI Captions" };
        var target = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            Duration = MediaTime.FromSeconds(10),
            SourceDuration = MediaTime.FromSeconds(10),
        };
        var oldCaption = new TimelineItem
        {
            Kind = ItemKind.Text,
            Duration = MediaTime.FromSeconds(1),
            TextContent = "old",
            MediaIntelligenceSourceAssetId = assetId,
        };
        var oldMarker = new Marker
        {
            Label = "old scene",
            Time = MediaTime.FromSeconds(1),
            MediaIntelligenceSourceAssetId = assetId,
        };
        videoTrack.Items.Add(target);
        textTrack.Items.Add(oldCaption);
        sequence.Tracks.Add(videoTrack);
        sequence.Tracks.Add(textTrack);
        sequence.Markers.Add(oldMarker);

        var command = new ApplyMediaIntelligenceCommand
        {
            TargetItemId = target.Id,
            Analysis = new MediaIntelligenceAnalysis
            {
                MediaAssetId = assetId,
                Scenes = [new() { SceneId = "new", Start = MediaTime.FromSeconds(2), End = MediaTime.FromSeconds(3) }],
                Transcript = [new() { Start = MediaTime.FromSeconds(2), End = MediaTime.FromSeconds(4), Text = "new" }],
            },
        };

        Assert.True(command.Execute(sequence).Success);
        Assert.DoesNotContain(oldMarker, sequence.Markers);
        Assert.DoesNotContain(oldCaption, textTrack.Items);
        Assert.Single(sequence.Markers);
        Assert.Single(textTrack.Items);
        Assert.Equal("new", textTrack.Items[0].TextContent);

        Assert.True(command.Undo(sequence).Success);
        Assert.Contains(oldMarker, sequence.Markers);
        Assert.Contains(oldCaption, textTrack.Items);
        Assert.DoesNotContain(sequence.Markers, marker => marker.Label == "Scene 1");
    }

    [Fact]
    public void undo_restores_generated_content_at_exact_original_indices()
    {
        var assetId = MediaAssetId.New();
        var sequence = new Sequence();
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "V1" };
        var captionTrack = new Track { Kind = TrackKind.Text, Name = "AI Captions" };
        var target = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            Duration = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(5),
        };
        var beforeCaption = new TimelineItem { Kind = ItemKind.Text, TextContent = "before" };
        var generatedCaption = new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = "generated",
            MediaIntelligenceSourceAssetId = assetId,
        };
        var afterCaption = new TimelineItem { Kind = ItemKind.Text, TextContent = "after" };
        videoTrack.Items.Add(target);
        captionTrack.Items.AddRange([beforeCaption, generatedCaption, afterCaption]);
        sequence.Tracks.AddRange([videoTrack, captionTrack]);
        var beforeMarker = new Marker { Label = "before", Time = MediaTime.Zero };
        var generatedMarker = new Marker
        {
            Label = "generated",
            Time = MediaTime.FromSeconds(1),
            MediaIntelligenceSourceAssetId = assetId,
        };
        var afterMarker = new Marker { Label = "after", Time = MediaTime.FromSeconds(2) };
        sequence.Markers.AddRange([beforeMarker, generatedMarker, afterMarker]);
        var command = new ApplyMediaIntelligenceCommand
        {
            TargetItemId = target.Id,
            Analysis = new MediaIntelligenceAnalysis
            {
                MediaAssetId = assetId,
                Scenes = [new() { SceneId = "new", Start = MediaTime.FromSeconds(3), End = MediaTime.FromSeconds(4) }],
                Transcript = [new() { Start = MediaTime.FromSeconds(3), End = MediaTime.FromSeconds(4), Text = "new" }],
            },
        };

        Assert.True(command.Execute(sequence).Success);
        Assert.True(command.Undo(sequence).Success);

        Assert.Equal([beforeMarker, generatedMarker, afterMarker], sequence.Markers);
        Assert.Equal([beforeCaption, generatedCaption, afterCaption], captionTrack.Items);
        Assert.Equal([videoTrack, captionTrack], sequence.Tracks);
    }

    [Fact]
    public void execute_rejects_locked_caption_track_without_partial_marker_changes()
    {
        var assetId = MediaAssetId.New();
        var sequence = new Sequence();
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "V1" };
        var captionTrack = new Track { Kind = TrackKind.Text, Name = "AI Captions", Locked = true };
        var target = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            Duration = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(5),
        };
        videoTrack.Items.Add(target);
        sequence.Tracks.Add(videoTrack);
        sequence.Tracks.Add(captionTrack);

        var result = new ApplyMediaIntelligenceCommand
        {
            TargetItemId = target.Id,
            Analysis = new MediaIntelligenceAnalysis
            {
                MediaAssetId = assetId,
                Scenes = [new() { SceneId = "scene", Start = MediaTime.Zero, End = MediaTime.FromSeconds(1) }],
                Transcript = [new() { Start = MediaTime.Zero, End = MediaTime.FromSeconds(1), Text = "caption" }],
            },
        }.Execute(sequence);

        Assert.False(result.Success);
        Assert.Empty(sequence.Markers);
        Assert.Empty(captionTrack.Items);
    }

    [Fact]
    public void project_serialization_preserves_analysis_and_generated_links()
    {
        var project = new Project();
        var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "clip.mp4" };
        project.MediaLibrary.Add(asset);
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis
        {
            MediaAssetId = asset.Id,
            SourcePath = asset.OriginalPath,
            Scenes = [new() { SceneId = "scene-1", Start = MediaTime.Zero, End = MediaTime.FromSeconds(2) }],
            Transcript = [new() { Start = MediaTime.Zero, End = MediaTime.FromSeconds(2), Text = "Caption" }],
        });
        project.MainSequence!.Markers.Add(new Marker
        {
            Label = "Scene 1",
            Time = MediaTime.Zero,
            MediaIntelligenceSourceAssetId = asset.Id,
        });

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        var analysis = Assert.Single(restored.MediaIntelligence);
        Assert.Equal(asset.Id, analysis.MediaAssetId);
        Assert.Single(analysis.Scenes);
        Assert.Single(analysis.Transcript);
        Assert.Equal(asset.Id, restored.MainSequence!.Markers[0].MediaIntelligenceSourceAssetId);
    }
}
