namespace Rushframe.Domain.Tests;

public sealed class SplitClipCommandTests
{
    [Fact]
    public void split_preserves_total_source_range()
    {
        var seq = MakeSequenceWithOneClip(out var trackId, out var itemId);

        var splitTime = MediaTime.FromSeconds(5);
        var cmd = new Editing.SplitClipCommand
        {
            TrackId = trackId,
            ItemId = itemId,
            SplitTime = splitTime,
        };

        var result = cmd.Execute(seq);
        Assert.True(result.Success);

        var track = seq.Tracks[0];
        Assert.Equal(2, track.Items.Count);
        Assert.Equal(5, track.Items[0].Duration.Seconds, 3);
        Assert.Equal(5, track.Items[1].Duration.Seconds, 3);
        Assert.Equal(5, track.Items[1].TimelineStart.Seconds, 3);
    }

    [Fact]
    public void split_undo_restores_single_item()
    {
        var seq = MakeSequenceWithOneClip(out var trackId, out var itemId);

        var cmd = new Editing.SplitClipCommand
        {
            TrackId = trackId,
            ItemId = itemId,
            SplitTime = MediaTime.FromSeconds(5),
        };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Single(seq.Tracks[0].Items);
        Assert.Equal(10, seq.Tracks[0].Items[0].Duration.Seconds, 3);
        Assert.Equal(itemId, seq.Tracks[0].Items[0].Id);
    }

    [Fact]
    public void split_outside_bounds_returns_error()
    {
        var seq = MakeSequenceWithOneClip(out var trackId, out var itemId);

        var cmd = new Editing.SplitClipCommand
        {
            TrackId = trackId,
            ItemId = itemId,
            SplitTime = MediaTime.FromSeconds(20),
        };

        var result = cmd.Execute(seq);
        Assert.False(result.Success);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void split_rejects_locked_track()
    {
        var seq = MakeSequenceWithOneClip(out var trackId, out var itemId);
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.SplitClipCommand
        {
            TrackId = trackId,
            ItemId = itemId,
            SplitTime = MediaTime.FromSeconds(5),
        };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Single(seq.Tracks[0].Items);
    }

    private static Sequence MakeSequenceWithOneClip(out TrackId trackId, out TimelineItemId itemId)
    {
        var seq = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem
        {
            Duration = MediaTime.FromSeconds(10),
            SourceDuration = MediaTime.FromSeconds(30),
        };
        track.Items.Add(item);
        seq.Tracks.Add(track);

        trackId = track.Id;
        itemId = item.Id;
        return seq;
    }
}
