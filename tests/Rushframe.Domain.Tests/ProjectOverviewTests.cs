using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class ProjectOverviewTests
{
    [Fact]
    public void Serialize_embeds_effect_and_modifier_overview()
    {
        var project = new Project { Name = "Overview Test" };
        var sequence = project.MainSequence!;
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = @"C:\media\source.mp4",
            Duration = MediaTime.FromSeconds(12),
        };
        project.MediaLibrary.Add(asset);
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(5),
            Opacity = 0.8,
            ColorCorrection = new ColorCorrection { Contrast = 0.2, Saturation = 1.15 },
        };
        item.Transform.ScaleX = 1.2;
        item.Transform.ScaleY = 1.2;
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "glitch",
            Parameters = { ["amount"] = 0.4 },
        });
        track.Items.Add(item);
        sequence.Tracks.Add(track);

        var json = ProjectSerializer.Serialize(project);
        var restored = ProjectSerializer.Deserialize(json);

        Assert.Contains("\"overview\"", json, StringComparison.Ordinal);
        Assert.Equal(1, restored.Overview.SequenceCount);
        Assert.Equal(1, restored.Overview.TrackCount);
        Assert.Equal(1, restored.Overview.TimelineItemCount);
        Assert.Contains("glitch", restored.Overview.EffectTypes);
        var itemOverview = Assert.Single(Assert.Single(restored.Overview.Sequences).Tracks[0].Items);
        Assert.Equal("source.mp4", itemOverview.MediaFile);
        Assert.Contains(itemOverview.Modifiers, value => value.StartsWith("transform scale", StringComparison.Ordinal));
        Assert.Contains(itemOverview.Modifiers, value => value.StartsWith("color ", StringComparison.Ordinal));
        Assert.Contains(itemOverview.Effects, value => value.Contains("glitch", StringComparison.Ordinal));
    }

    [Fact]
    public void Overview_flags_visual_modifiers_on_audio_only_items()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        var asset = new MediaAsset
        {
            Kind = MediaKind.Audio,
            OriginalPath = @"C:\media\voice.wav",
            Duration = MediaTime.FromSeconds(4),
        };
        project.MediaLibrary.Add(asset);
        var track = new Track { Kind = TrackKind.Audio, Name = "A1" };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(4),
            SourceDuration = MediaTime.FromSeconds(4),
            ColorCorrection = new ColorCorrection { Brightness = 0.2 },
        });
        sequence.Tracks.Add(track);

        var overview = ProjectOverviewBuilder.Build(project);

        Assert.Contains(overview.ReviewHints, hint =>
            hint.Contains("audio-only item contains visual modifiers", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_refreshes_snapshot_overview_without_mutating_live_project()
    {
        var project = new Project();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        };
        track.Items.Add(item);
        project.MainSequence!.Tracks.Add(track);

        var initialSnapshot = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.Empty(initialSnapshot.Overview.EffectTypes);
        Assert.Empty(project.Overview.EffectTypes);

        item.Effects.Add(new EffectInstance { EffectTypeId = "filmGrain" });
        var updatedSnapshot = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Empty(project.Overview.EffectTypes);
        Assert.Contains("filmGrain", updatedSnapshot.Overview.EffectTypes);
        Assert.Contains(updatedSnapshot.Overview.ModifierSummary, summary =>
            summary.Modifier == "effects" && summary.ItemCount == 1);
    }
}
