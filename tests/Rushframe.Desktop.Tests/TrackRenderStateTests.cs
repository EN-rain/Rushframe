using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class TrackRenderStateTests
{
    [Fact]
    public void realtime_plan_uses_solo_and_canonical_list_order_for_visuals()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Tracks.Clear();
        var first = TextTrack("First", order: 99, solo: false);
        var second = TextTrack("Second", order: -10, solo: true);
        sequence.Tracks.AddRange([first, second]);

        var plan = RealtimeRenderPlan.Build(project, sequence);
        var active = new List<RealtimeRenderPlan.VisualEntry>();
        plan.CollectActiveVisuals(0.5, active);

        var entry = Assert.Single(active);
        Assert.Same(second, entry.Track);

        second.Solo = false;
        plan = RealtimeRenderPlan.Build(project, sequence);
        active.Clear();
        plan.CollectActiveVisuals(0.5, active);
        Assert.Equal([first, second], active.Select(entry => entry.Track));
    }

    [Fact]
    public void realtime_plan_uses_solo_for_audio_tracks()
    {
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Tracks.Clear();
        var firstAsset = new MediaAsset { Kind = MediaKind.Audio, OriginalPath = "first.wav" };
        var secondAsset = new MediaAsset { Kind = MediaKind.Audio, OriginalPath = "second.wav" };
        project.MediaLibrary.AddRange([firstAsset, secondAsset]);
        var first = AudioTrack("First", firstAsset.Id, solo: false);
        var second = AudioTrack("Second", secondAsset.Id, solo: true);
        sequence.Tracks.AddRange([first, second]);

        var plan = RealtimeRenderPlan.Build(project, sequence);
        var active = new List<RealtimeRenderPlan.AudioEntry>();
        plan.CollectActiveAudio(0.5, active);

        var entry = Assert.Single(active);
        Assert.Same(second, entry.Track);
    }

    [Fact]
    public void exact_renderer_source_enforces_solo_and_sequence_list_order()
    {
        var source = File.ReadAllText(SourcePath(
            "src", "Rushframe.Media.Native", "FfmpegTimelineRenderer.cs"));

        Assert.Contains("hasSoloTracks", source, StringComparison.Ordinal);
        Assert.Contains("!hasSoloTracks || track.Solo", source, StringComparison.Ordinal);
        Assert.Contains("sequence.Tracks.IndexOf(entry.Track)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy(track => track.Order)", source, StringComparison.Ordinal);
    }

    private static Track TextTrack(string name, int order, bool solo)
    {
        var track = new Track { Kind = TrackKind.Text, Name = name, Order = order, Solo = solo };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = name,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        });
        return track;
    }

    private static Track AudioTrack(string name, MediaAssetId assetId, bool solo)
    {
        var track = new Track { Kind = TrackKind.Audio, Name = name, Solo = solo };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = assetId,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
        });
        return track;
    }

    private static string SourcePath(params string[] parts) =>
        Path.Combine(FindRepositoryRoot(), Path.Combine(parts));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
