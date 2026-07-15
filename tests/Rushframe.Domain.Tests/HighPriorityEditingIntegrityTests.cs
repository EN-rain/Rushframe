using Rushframe.Application;
using Rushframe.Domain.Editing;

namespace Rushframe.Domain.Tests;

public sealed class HighPriorityEditingIntegrityTests
{
    [Fact]
    public void adjustment_layers_are_compatible_with_video_and_overlay_tracks()
    {
        Assert.True(TrackCompatibility.IsItemCompatibleWithTrack(ItemKind.AdjustmentLayer, TrackKind.Video));
        Assert.True(TrackCompatibility.IsItemCompatibleWithTrack(ItemKind.AdjustmentLayer, TrackKind.Overlay));
        Assert.False(TrackCompatibility.IsItemCompatibleWithTrack(ItemKind.AdjustmentLayer, TrackKind.Audio));
    }

    [Fact]
    public void add_clip_rejects_incompatible_destination_without_mutation()
    {
        var sequence = new Sequence();
        var audio = new Track { Kind = TrackKind.Audio, Name = "A1" };
        sequence.Tracks.Add(audio);
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            Duration = MediaTime.FromSeconds(1),
            SourceDuration = MediaTime.FromSeconds(1),
        };

        var result = new AddClipCommand { TrackId = audio.Id, Item = text }.Execute(sequence);

        Assert.False(result.Success);
        Assert.Empty(audio.Items);
    }

    [Fact]
    public void paste_rejects_incompatible_destination_without_mutation()
    {
        var sequence = new Sequence();
        var textTrack = new Track { Kind = TrackKind.Text, Name = "T1" };
        var audioTrack = new Track { Kind = TrackKind.Audio, Name = "A1" };
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = "caption",
            Duration = MediaTime.FromSeconds(1),
            SourceDuration = MediaTime.FromSeconds(1),
        };
        textTrack.Items.Add(text);
        sequence.Tracks.AddRange([textTrack, audioTrack]);
        var copy = new CopyClipCommand { ItemId = text.Id };
        Assert.True(copy.Execute(sequence).Success);

        var result = new PasteClipCommand
        {
            TrackId = audioTrack.Id,
            TimelineStart = MediaTime.FromSeconds(2),
            CopyCommand = copy,
        }.Execute(sequence);

        Assert.False(result.Success);
        Assert.Empty(audioTrack.Items);
    }

    [Fact]
    public void track_structure_commands_keep_order_equal_to_canonical_list_position()
    {
        var sequence = new Sequence();
        var first = new Track { Kind = TrackKind.Video, Name = "First", Order = 90 };
        var second = new Track { Kind = TrackKind.Video, Name = "Second", Order = -4 };
        sequence.Tracks.AddRange([first, second]);
        TrackOrdering.Normalize(sequence);
        var history = new UndoRedoStack();

        Assert.True(history.Execute(sequence, new ReorderTrackCommand { TrackId = second.Id, NewIndex = 0 }).Success);
        Assert.Equal([0, 1], sequence.Tracks.Select(track => track.Order));
        Assert.Same(second, sequence.Tracks[0]);

        Assert.True(history.Execute(sequence, new DuplicateTrackCommand { TrackId = second.Id }).Success);
        Assert.Equal([0, 1, 2], sequence.Tracks.Select(track => track.Order));

        Assert.True(history.Undo(sequence).Success);
        Assert.Equal([0, 1], sequence.Tracks.Select(track => track.Order));
        Assert.True(history.Undo(sequence).Success);
        Assert.Equal([0, 1], sequence.Tracks.Select(track => track.Order));
        Assert.Same(first, sequence.Tracks[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void delete_commands_remove_referencing_transitions_and_undo_restores_exact_order(bool ripple)
    {
        var sequence = BuildTransitionSequence(out var track, out var first, out var middle, out var last);
        var incoming = new Transition
        {
            LeftItemId = first.Id,
            RightItemId = middle.Id,
            Kind = TransitionKind.CrossDissolve,
            Duration = MediaTime.FromSeconds(0.2),
        };
        var outgoing = new Transition
        {
            LeftItemId = middle.Id,
            RightItemId = last.Id,
            Kind = TransitionKind.Wipe,
            Duration = MediaTime.FromSeconds(0.3),
        };
        sequence.Transitions.AddRange([incoming, outgoing]);
        IEditCommand command = ripple
            ? new RippleDeleteClipCommand { ItemId = middle.Id, Ripple = new RippleState { Enabled = true } }
            : new DeleteClipCommand { ItemId = middle.Id };

        Assert.True(command.Execute(sequence).Success);
        Assert.Empty(sequence.Transitions);
        Assert.DoesNotContain(middle, track.Items);

        Assert.True(command.Undo(sequence).Success);
        Assert.Equal([incoming, outgoing], sequence.Transitions);
        Assert.Same(middle, track.Items[1]);
    }

    [Fact]
    public void delete_marker_undo_restores_original_index()
    {
        var sequence = new Sequence();
        var first = new Marker { Label = "first", Time = MediaTime.Zero };
        var middle = new Marker { Label = "middle", Time = MediaTime.FromSeconds(1) };
        var last = new Marker { Label = "last", Time = MediaTime.FromSeconds(2) };
        sequence.Markers.AddRange([first, middle, last]);
        var command = new DeleteMarkerCommand { MarkerId = middle.Id };

        Assert.True(command.Execute(sequence).Success);
        Assert.Equal([first, last], sequence.Markers);
        Assert.True(command.Undo(sequence).Success);
        Assert.Equal([first, middle, last], sequence.Markers);
    }

    [Fact]
    public void split_reattaches_outgoing_transition_to_right_item_and_undo_restores_original()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var left = Clip(0, 2);
        var next = Clip(2, 2);
        track.Items.AddRange([left, next]);
        sequence.Tracks.Add(track);
        var original = new Transition
        {
            LeftItemId = left.Id,
            RightItemId = next.Id,
            Kind = TransitionKind.CrossDissolve,
            Duration = MediaTime.FromSeconds(0.4),
            Alignment = 0.6,
        };
        sequence.Transitions.Add(original);
        var command = new SplitClipCommand
        {
            TrackId = track.Id,
            ItemId = left.Id,
            SplitTime = MediaTime.FromSeconds(1),
        };

        Assert.True(command.Execute(sequence).Success);
        var right = track.Items.Single(item => item.Id != left.Id && item.Id != next.Id);
        var replacement = Assert.Single(sequence.Transitions);
        Assert.Equal(right.Id, replacement.LeftItemId);
        Assert.Equal(next.Id, replacement.RightItemId);
        Assert.Equal(original.Kind, replacement.Kind);
        Assert.Equal(original.Duration, replacement.Duration);
        Assert.Equal(original.Alignment, replacement.Alignment);

        Assert.True(command.Undo(sequence).Success);
        Assert.Equal([left, next], track.Items);
        Assert.Same(original, Assert.Single(sequence.Transitions));
        Assert.Equal(2, left.Duration.Seconds, 3);
    }

    [Fact]
    public void split_reversed_clip_uses_tail_for_left_and_head_for_right()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Reversed = true,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(4),
            SourceStart = MediaTime.FromSeconds(10),
            SourceDuration = MediaTime.FromSeconds(8),
            Speed = 2,
        };
        track.Items.Add(item);
        sequence.Tracks.Add(track);
        var history = new UndoRedoStack();
        var command = new SplitClipCommand
        {
            TrackId = track.Id,
            ItemId = item.Id,
            SplitTime = MediaTime.FromSeconds(1.5),
        };

        Assert.True(history.Execute(sequence, command).Success);
        var right = track.Items.Single(candidate => candidate.Id != item.Id);
        Assert.Equal(15, item.SourceStart.Seconds, 3);
        Assert.Equal(3, item.SourceDuration.Seconds, 3);
        Assert.Equal(10, right.SourceStart.Seconds, 3);
        Assert.Equal(5, right.SourceDuration.Seconds, 3);
        var rightId = right.Id;

        Assert.True(history.Undo(sequence).Success);
        Assert.Equal(10, item.SourceStart.Seconds, 3);
        Assert.Equal(8, item.SourceDuration.Seconds, 3);
        Assert.True(history.Redo(sequence).Success);
        Assert.Equal(rightId, track.Items.Single(candidate => candidate.Id != item.Id).Id);
    }

    [Fact]
    public void split_partitions_animation_channels_at_local_split_time()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = Clip(0, 4);
        var channel = new AnimationChannel { PropertyName = AnimationPropertyNames.PositionY, DefaultValue = 0 };
        channel.Keyframes.Add(new Keyframe { Time = MediaTime.Zero, Value = 0 });
        channel.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(4), Value = 400 });
        item.AnimationChannels.Add(channel);
        track.Items.Add(item);
        sequence.Tracks.Add(track);
        var command = new SplitClipCommand
        {
            TrackId = track.Id,
            ItemId = item.Id,
            SplitTime = MediaTime.FromSeconds(2),
        };

        Assert.True(command.Execute(sequence).Success);
        var right = track.Items.Single(candidate => candidate.Id != item.Id);
        Assert.Equal(200, item.GetAnimationChannel(AnimationPropertyNames.PositionY)!.GetValueAt(MediaTime.FromSeconds(2)), 3);
        Assert.Equal(200, right.GetAnimationChannel(AnimationPropertyNames.PositionY)!.GetValueAt(MediaTime.Zero), 3);
        Assert.Equal(400, right.GetAnimationChannel(AnimationPropertyNames.PositionY)!.GetValueAt(MediaTime.FromSeconds(2)), 3);

        Assert.True(command.Undo(sequence).Success);
        Assert.Equal(400, item.GetAnimationChannel(AnimationPropertyNames.PositionY)!.GetValueAt(MediaTime.FromSeconds(4)), 3);
    }

    [Fact]
    public void split_rejects_segmented_speed_curve_without_mutation()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        var item = Clip(0, 4);
        item.SpeedCurve = new SpeedCurve();
        item.SpeedCurve.Segments.Add(new SpeedSegment
        {
            SourceStart = MediaTime.Zero,
            SourceEnd = MediaTime.FromSeconds(4),
            Speed = 2,
        });
        track.Items.Add(item);
        sequence.Tracks.Add(track);

        var result = new SplitClipCommand
        {
            TrackId = track.Id,
            ItemId = item.Id,
            SplitTime = MediaTime.FromSeconds(2),
        }.Execute(sequence);

        Assert.False(result.Success);
        Assert.Single(track.Items);
        Assert.Equal(4, item.Duration.Seconds, 3);
    }

    private static Sequence BuildTransitionSequence(
        out Track track,
        out TimelineItem first,
        out TimelineItem middle,
        out TimelineItem last)
    {
        var sequence = new Sequence();
        track = new Track { Kind = TrackKind.Video, Name = "V1" };
        first = Clip(0, 1);
        middle = Clip(1, 1);
        last = Clip(2, 1);
        track.Items.AddRange([first, middle, last]);
        sequence.Tracks.Add(track);
        return sequence;
    }

    private static TimelineItem Clip(double start, double duration) => new()
    {
        Kind = ItemKind.Clip,
        TimelineStart = MediaTime.FromSeconds(start),
        Duration = MediaTime.FromSeconds(duration),
        SourceDuration = MediaTime.FromSeconds(duration),
    };
}
