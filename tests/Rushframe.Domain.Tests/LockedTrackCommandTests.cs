using Rushframe.Application;

namespace Rushframe.Domain.Tests;

public sealed class LockedTrackCommandTests
{
    [Fact]
    public void duplicate_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.DuplicateClipCommand { ItemId = itemId };
        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void ripple_delete_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.RippleDeleteClipCommand
        {
            ItemId = itemId,
            Ripple = new RippleState { Enabled = true },
        };
        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void add_rejects_locked_track()
    {
        var seq = MakeSequence(out _);
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.AddClipCommand
        {
            TrackId = seq.Tracks[0].Id,
            Item = new TimelineItem
            {
                Duration = MediaTime.FromSeconds(2),
                SourceDuration = MediaTime.FromSeconds(2),
            },
        };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void move_rejects_locked_source_track()
    {
        var seq = MakeSequence(out var itemId);
        var item = seq.Tracks[0].Items[0];
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.MoveClipCommand
        {
            ItemId = itemId,
            NewTimelineStart = MediaTime.FromSeconds(3),
        };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Equal(MediaTime.Zero, item.TimelineStart);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void move_rejects_locked_destination_track()
    {
        var seq = new Sequence();
        var source = new Track { Kind = TrackKind.Text };
        var destination = new Track { Kind = TrackKind.Overlay, Locked = true };
        var item = new TimelineItem
        {
            Kind = ItemKind.Text,
            Duration = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(5),
        };
        source.Items.Add(item);
        seq.Tracks.Add(source);
        seq.Tracks.Add(destination);

        var cmd = new Editing.MoveClipCommand
        {
            ItemId = item.Id,
            TargetTrackId = destination.Id,
        };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Single(source.Items);
        Assert.Empty(destination.Items);
    }

    [Fact]
    public void trim_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        var item = seq.Tracks[0].Items[0];
        seq.Tracks[0].Locked = true;

        var cmd = new Editing.TrimClipCommand
        {
            TrackId = seq.Tracks[0].Id,
            ItemId = itemId,
            NewDuration = MediaTime.FromSeconds(2),
        };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
        Assert.Equal(5, item.Duration.Seconds, 3);
    }

    [Fact]
    public void paste_rejects_locked_track_without_polluting_undo_history()
    {
        var seq = MakeSequence(out var itemId);
        var copy = new CopyClipCommand { ItemId = itemId };
        Assert.True(copy.Execute(seq).Success);
        seq.Tracks[0].Locked = true;

        var history = new Editing.UndoRedoStack();
        var paste = new PasteClipCommand
        {
            TrackId = seq.Tracks[0].Id,
            TimelineStart = MediaTime.FromSeconds(6),
            CopyCommand = copy,
        };

        var result = history.Execute(seq, paste);

        Assert.False(result.Success);
        Assert.False(history.CanUndo);
        Assert.Single(seq.Tracks[0].Items);
    }

    [Fact]
    public void transform_rejects_locked_track_without_polluting_undo_history()
    {
        var seq = MakeSequence(out var itemId);
        var item = seq.Tracks[0].Items[0];
        seq.Tracks[0].Locked = true;
        var history = new Editing.UndoRedoStack();

        var result = history.Execute(seq, new Editing.UpdateTransformCommand
        {
            ItemId = itemId,
            NewTransform = new Transform2D { PositionX = 100 },
        });

        Assert.False(result.Success);
        Assert.False(history.CanUndo);
        Assert.Equal(0, item.Transform.PositionX);
    }

    [Fact]
    public void generic_property_change_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        var item = seq.Tracks[0].Items[0];
        seq.Tracks[0].Locked = true;

        var result = new SetPropertyCommand
        {
            ItemId = itemId,
            PropertyName = nameof(TimelineItem.Opacity),
            NewValue = 0.25,
            Getter = candidate => candidate.Opacity,
            Setter = (candidate, value) => candidate.Opacity = (double)value!,
        }.Execute(seq);

        Assert.False(result.Success);
        Assert.Equal(1, item.Opacity);
    }

    [Fact]
    public void animation_change_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        seq.Tracks[0].Locked = true;

        var result = new Editing.UpdateAnimationChannelsCommand
        {
            ItemId = itemId,
            NewChannels =
            [
                new AnimationChannel
                {
                    PropertyName = AnimationPropertyNames.Opacity,
                    DefaultValue = 1,
                },
            ],
        }.Execute(seq);

        Assert.False(result.Success);
        Assert.Empty(seq.Tracks[0].Items[0].AnimationChannels);
    }

    [Fact]
    public void effect_change_rejects_locked_track()
    {
        var seq = MakeSequence(out var itemId);
        seq.Tracks[0].Locked = true;

        var result = new Editing.AddEffectCommand
        {
            ItemId = itemId,
            EffectTypeId = "blur",
        }.Execute(seq);

        Assert.False(result.Success);
        Assert.Empty(seq.Tracks[0].Items[0].Effects);
    }

    private static Sequence MakeSequence(out TimelineItemId itemId)
    {
        var seq = new Sequence();
        var track = new Track { Kind = TrackKind.Video };
        var item = new TimelineItem
        {
            Duration = MediaTime.FromSeconds(5),
            SourceDuration = MediaTime.FromSeconds(5),
        };
        track.Items.Add(item);
        seq.Tracks.Add(track);
        itemId = item.Id;
        return seq;
    }
}
