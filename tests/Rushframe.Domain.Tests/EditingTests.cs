using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Domain.Tests;

public class EditingTests
{
    private static Sequence MakeSequence()
    {
        var seq = new Sequence();
        seq.Tracks.Add(new Track { Kind = TrackKind.Video, Name = "V1", Order = 0 });
        seq.Tracks.Add(new Track { Kind = TrackKind.Audio, Name = "A1", Order = 1 });
        return seq;
    }

    private static TimelineItem MakeClip() => new()
    {
        Kind = ItemKind.Clip,
        TimelineStart = MediaTime.FromSeconds(5),
        Duration = MediaTime.FromSeconds(10),
        SourceStart = MediaTime.Zero,
        SourceDuration = MediaTime.FromSeconds(10),
    };

    [Fact]
    public void AddTrackCommand_AddsTrack()
    {
        var seq = MakeSequence();
        var cmd = new AddTrackCommand { TrackKind = TrackKind.Text };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Equal(3, seq.Tracks.Count);
        Assert.Equal(TrackKind.Text, seq.Tracks[2].Kind);
    }

    [Fact]
    public void AddTrackCommand_InsertAt()
    {
        var seq = MakeSequence();
        var cmd = new AddTrackCommand { TrackKind = TrackKind.Text, InsertAt = 0 };

        cmd.Execute(seq);

        Assert.Equal(3, seq.Tracks.Count);
        Assert.Equal(TrackKind.Text, seq.Tracks[0].Kind);
    }

    [Fact]
    public void AddTrackCommand_Undo()
    {
        var seq = MakeSequence();
        var cmd = new AddTrackCommand { TrackKind = TrackKind.Text };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal(2, seq.Tracks.Count);
    }

    [Fact]
    public void DeleteTrackCommand_DeletesTrack()
    {
        var seq = MakeSequence();
        var cmd = new DeleteTrackCommand { TrackId = seq.Tracks[0].Id };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Single(seq.Tracks);
    }

    [Fact]
    public void DeleteTrackCommand_Undo()
    {
        var seq = MakeSequence();
        var trackId = seq.Tracks[0].Id;
        var cmd = new DeleteTrackCommand { TrackId = trackId };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal(2, seq.Tracks.Count);
    }

    [Fact]
    public void DeleteTrackCommand_NotFound()
    {
        var seq = MakeSequence();
        var cmd = new DeleteTrackCommand { TrackId = TrackId.New() };

        var result = cmd.Execute(seq);

        Assert.False(result.Success);
    }

    [Fact]
    public void RenameTrackCommand()
    {
        var seq = MakeSequence();
        var cmd = new RenameTrackCommand { TrackId = seq.Tracks[0].Id, NewName = "Video 1" };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Equal("Video 1", seq.Tracks[0].Name);
    }

    [Fact]
    public void RenameTrackCommand_Undo()
    {
        var seq = MakeSequence();
        var cmd = new RenameTrackCommand { TrackId = seq.Tracks[0].Id, NewName = "Video 1" };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal("V1", seq.Tracks[0].Name);
    }

    [Fact]
    public void ReorderTrackCommand()
    {
        var seq = MakeSequence();
        seq.Tracks.Add(new Track { Kind = TrackKind.Text, Name = "T1", Order = 2 });
        var cmd = new ReorderTrackCommand { TrackId = seq.Tracks[2].Id, NewIndex = 0 };

        cmd.Execute(seq);

        Assert.Equal("T1", seq.Tracks[0].Name);
    }

    [Fact]
    public void ReorderTrackCommand_Undo()
    {
        var seq = MakeSequence();
        seq.Tracks.Add(new Track { Kind = TrackKind.Text, Name = "T1", Order = 2 });
        var cmd = new ReorderTrackCommand { TrackId = seq.Tracks[2].Id, NewIndex = 0 };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal("T1", seq.Tracks[2].Name);
    }

    [Fact]
    public void DuplicateTrackCommand()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        seq.Tracks[0].Items.Add(clip);
        var cmd = new DuplicateTrackCommand { TrackId = seq.Tracks[0].Id };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Equal(3, seq.Tracks.Count);
        Assert.Equal("V1 (copy)", seq.Tracks[1].Name);
        Assert.Single(seq.Tracks[1].Items);
    }

    [Fact]
    public void DuplicateTrackCommand_Undo()
    {
        var seq = MakeSequence();
        var cmd = new DuplicateTrackCommand { TrackId = seq.Tracks[0].Id };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal(2, seq.Tracks.Count);
    }

    [Fact]
    public void ToggleTrackMuteCommand()
    {
        var seq = MakeSequence();
        var cmd = new ToggleTrackMuteCommand { TrackId = seq.Tracks[0].Id };

        cmd.Execute(seq);
        Assert.True(seq.Tracks[0].Muted);

        cmd.Undo(seq);
        Assert.False(seq.Tracks[0].Muted);
    }

    [Fact]
    public void ToggleTrackSoloCommand()
    {
        var seq = MakeSequence();
        var cmd = new ToggleTrackSoloCommand { TrackId = seq.Tracks[0].Id };

        cmd.Execute(seq);
        Assert.True(seq.Tracks[0].Solo);

        cmd.Undo(seq);
        Assert.False(seq.Tracks[0].Solo);
    }

    [Fact]
    public void ToggleTrackLockCommand()
    {
        var seq = MakeSequence();
        var cmd = new ToggleTrackLockCommand { TrackId = seq.Tracks[0].Id };

        cmd.Execute(seq);
        Assert.True(seq.Tracks[0].Locked);

        cmd.Undo(seq);
        Assert.False(seq.Tracks[0].Locked);
    }

    [Fact]
    public void AddMarkerCommand()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "Chapter 1", Time = MediaTime.FromSeconds(10) };
        var cmd = new AddMarkerCommand { Marker = marker };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Single(seq.Markers);
        Assert.Equal("Chapter 1", seq.Markers[0].Label);
    }

    [Fact]
    public void AddMarkerCommand_Undo()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "Chapter 1", Time = MediaTime.FromSeconds(10) };
        var cmd = new AddMarkerCommand { Marker = marker };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Empty(seq.Markers);
    }

    [Fact]
    public void EditMarkerCommand()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "Old", Time = MediaTime.FromSeconds(5) };
        seq.Markers.Add(marker);
        var cmd = new EditMarkerCommand { MarkerId = marker.Id, NewLabel = "New", NewTime = MediaTime.FromSeconds(10) };

        cmd.Execute(seq);

        Assert.Equal("New", marker.Label);
        Assert.Equal(10, marker.Time.Seconds);
    }

    [Fact]
    public void EditMarkerCommand_Undo()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "Old", Time = MediaTime.FromSeconds(5) };
        seq.Markers.Add(marker);
        var cmd = new EditMarkerCommand { MarkerId = marker.Id, NewLabel = "New", NewTime = MediaTime.FromSeconds(10) };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal("Old", marker.Label);
        Assert.Equal(5, marker.Time.Seconds);
    }

    [Fact]
    public void DeleteMarkerCommand()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "M1", Time = MediaTime.FromSeconds(5) };
        seq.Markers.Add(marker);
        var cmd = new DeleteMarkerCommand { MarkerId = marker.Id };

        cmd.Execute(seq);

        Assert.Empty(seq.Markers);
    }

    [Fact]
    public void DeleteMarkerCommand_Undo()
    {
        var seq = MakeSequence();
        var marker = new Marker { Label = "M1", Time = MediaTime.FromSeconds(5) };
        seq.Markers.Add(marker);
        var cmd = new DeleteMarkerCommand { MarkerId = marker.Id };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Single(seq.Markers);
    }

    [Fact]
    public void ClearMarkersCommand()
    {
        var seq = MakeSequence();
        seq.Markers.Add(new Marker { Label = "M1", Time = MediaTime.FromSeconds(1) });
        seq.Markers.Add(new Marker { Label = "M2", Time = MediaTime.FromSeconds(2) });
        var cmd = new ClearMarkersCommand();

        cmd.Execute(seq);

        Assert.Empty(seq.Markers);
    }

    [Fact]
    public void ClearMarkersCommand_Undo()
    {
        var seq = MakeSequence();
        seq.Markers.Add(new Marker { Label = "M1", Time = MediaTime.FromSeconds(1) });
        var cmd = new ClearMarkersCommand();

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Single(seq.Markers);
    }

    [Fact]
    public void RippleDeleteClipCommand_NoRipple()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        seq.Tracks[0].Items.Add(clip);
        var cmd = new RippleDeleteClipCommand { ItemId = clip.Id, Ripple = new RippleState() };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Empty(seq.Tracks[0].Items);
    }

    [Fact]
    public void RippleDeleteClipCommand_Ripple()
    {
        var seq = MakeSequence();
        var clip1 = MakeClip();
        var clip2 = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.FromSeconds(20),
            Duration = MediaTime.FromSeconds(5),
        };
        seq.Tracks[0].Items.Add(clip1);
        seq.Tracks[0].Items.Add(clip2);
        var cmd = new RippleDeleteClipCommand { ItemId = clip1.Id, Ripple = new RippleState { Enabled = true } };

        cmd.Execute(seq);

        Assert.Single(seq.Tracks[0].Items);
        Assert.Equal(10, seq.Tracks[0].Items[0].TimelineStart.Seconds);
    }

    [Fact]
    public void RippleDeleteClipCommand_Undo()
    {
        var seq = MakeSequence();
        var clip1 = MakeClip();
        var clip2 = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.FromSeconds(20),
            Duration = MediaTime.FromSeconds(5),
        };
        seq.Tracks[0].Items.Add(clip1);
        seq.Tracks[0].Items.Add(clip2);
        var cmd = new RippleDeleteClipCommand { ItemId = clip1.Id, Ripple = new RippleState { Enabled = true } };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal(2, seq.Tracks[0].Items.Count);
        Assert.Equal(20, seq.Tracks[0].Items[1].TimelineStart.Seconds);
    }

    [Fact]
    public void SetTextContentCommand()
    {
        var seq = MakeSequence();
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(5),
            TextContent = "Hello",
        };
        seq.Tracks[0].Items.Add(text);
        var cmd = new SetTextContentCommand { ItemId = text.Id, NewText = "World" };

        cmd.Execute(seq);

        Assert.Equal("World", text.TextContent);
    }

    [Fact]
    public void SetTextContentCommand_Undo()
    {
        var seq = MakeSequence();
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(5),
            TextContent = "Hello",
        };
        seq.Tracks[0].Items.Add(text);
        var cmd = new SetTextContentCommand { ItemId = text.Id, NewText = "World" };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal("Hello", text.TextContent);
    }

    [Fact]
    public void SetTextPropertiesCommand()
    {
        var seq = MakeSequence();
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(5),
        };
        seq.Tracks[0].Items.Add(text);
        var cmd = new SetTextPropertiesCommand
        {
            ItemId = text.Id,
            FontFamily = "Arial",
            FontSize = 72,
            FontBold = true,
            FillColor = "#FF0000",
        };

        cmd.Execute(seq);

        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal(72, text.FontSize);
        Assert.True(text.FontBold);
        Assert.Equal("#FF0000", text.FillColor);
    }

    [Fact]
    public void SetTextPropertiesCommand_Undo()
    {
        var seq = MakeSequence();
        var text = new TimelineItem
        {
            Kind = ItemKind.Text,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(5),
            FontSize = 48,
        };
        seq.Tracks[0].Items.Add(text);
        var cmd = new SetTextPropertiesCommand
        {
            ItemId = text.Id,
            FontSize = 72,
        };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Equal(48, text.FontSize);
    }

    [Fact]
    public void AddEffectCommand()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        seq.Tracks[0].Items.Add(clip);
        var cmd = new AddEffectCommand { ItemId = clip.Id, EffectTypeId = "blur" };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        Assert.Single(clip.Effects);
        Assert.Equal("blur", clip.Effects[0].EffectTypeId);
    }

    [Fact]
    public void AddEffectCommand_Undo()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        seq.Tracks[0].Items.Add(clip);
        var cmd = new AddEffectCommand { ItemId = clip.Id, EffectTypeId = "blur" };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Empty(clip.Effects);
    }

    [Fact]
    public void RemoveEffectCommand()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        var effect = new EffectInstance { EffectTypeId = "blur" };
        clip.Effects.Add(effect);
        seq.Tracks[0].Items.Add(clip);
        var cmd = new RemoveEffectCommand { ItemId = clip.Id, EffectInstanceId = effect.Id };

        cmd.Execute(seq);

        Assert.Empty(clip.Effects);
    }

    [Fact]
    public void RemoveEffectCommand_Undo()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        var effect = new EffectInstance { EffectTypeId = "blur" };
        clip.Effects.Add(effect);
        seq.Tracks[0].Items.Add(clip);
        var cmd = new RemoveEffectCommand { ItemId = clip.Id, EffectInstanceId = effect.Id };

        cmd.Execute(seq);
        cmd.Undo(seq);

        Assert.Single(clip.Effects);
    }

    [Fact]
    public void RemoveEffectCommand_Undo_restores_original_order()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        var first = new EffectInstance { EffectTypeId = "first" };
        var middle = new EffectInstance { EffectTypeId = "middle" };
        var last = new EffectInstance { EffectTypeId = "last" };
        clip.Effects.AddRange([first, middle, last]);
        seq.Tracks[0].Items.Add(clip);
        var cmd = new RemoveEffectCommand { ItemId = clip.Id, EffectInstanceId = middle.Id };

        Assert.True(cmd.Execute(seq).Success);
        Assert.True(cmd.Undo(seq).Success);

        Assert.Equal(["first", "middle", "last"], clip.Effects.Select(effect => effect.EffectTypeId));
    }

    [Fact]
    public void ReorderEffectCommand()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        var e1 = new EffectInstance { EffectTypeId = "blur" };
        var e2 = new EffectInstance { EffectTypeId = "mono" };
        clip.Effects.Add(e1);
        clip.Effects.Add(e2);
        seq.Tracks[0].Items.Add(clip);
        var cmd = new ReorderEffectCommand { ItemId = clip.Id, EffectInstanceId = e2.Id, NewIndex = 0 };

        cmd.Execute(seq);

        Assert.Equal("mono", clip.Effects[0].EffectTypeId);
        Assert.Equal("blur", clip.Effects[1].EffectTypeId);
    }

    [Fact]
    public void ApplyTransitionCommand()
    {
        var seq = MakeSequence();
        var left = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(5),
        };
        var right = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.FromSeconds(5),
            Duration = MediaTime.FromSeconds(5),
        };
        seq.Tracks[0].Items.Add(left);
        seq.Tracks[0].Items.Add(right);
        var cmd = new ApplyTransitionCommand { LeftItemId = left.Id, RightItemId = right.Id };

        var result = cmd.Execute(seq);

        Assert.True(result.Success);
        var transition = Assert.Single(seq.Transitions);
        Assert.Equal(left.Id, transition.LeftItemId);
        Assert.Equal(right.Id, transition.RightItemId);
        Assert.Equal(TransitionKind.CrossDissolve, transition.Kind);
        Assert.Equal(TransitionAudioMode.None, transition.AudioMode);

        var undo = cmd.Undo(seq);

        Assert.True(undo.Success);
        Assert.Empty(seq.Transitions);
    }

    [Fact]
    public void ApplyTransitionCommand_rejects_audio_track_pair()
    {
        var seq = new Sequence();
        var track = new Track { Kind = TrackKind.Audio };
        var left = new TimelineItem { Kind = ItemKind.Clip, TimelineStart = MediaTime.Zero, Duration = MediaTime.FromSeconds(2) };
        var right = new TimelineItem { Kind = ItemKind.Clip, TimelineStart = MediaTime.FromSeconds(2), Duration = MediaTime.FromSeconds(2) };
        track.Items.AddRange([left, right]);
        seq.Tracks.Add(track);

        var result = new ApplyTransitionCommand { LeftItemId = left.Id, RightItemId = right.Id }.Execute(seq);

        Assert.False(result.Success);
        Assert.Contains("video or overlay", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(seq.Transitions);
    }

    [Fact]
    public void ApplyTransitionCommand_rejects_locked_track()
    {
        var seq = MakeSequence();
        var left = new TimelineItem { TimelineStart = MediaTime.Zero, Duration = MediaTime.FromSeconds(2) };
        var right = new TimelineItem { TimelineStart = MediaTime.FromSeconds(2), Duration = MediaTime.FromSeconds(2) };
        seq.Tracks[0].Items.AddRange([left, right]);
        seq.Tracks[0].Locked = true;

        var result = new ApplyTransitionCommand { LeftItemId = left.Id, RightItemId = right.Id }.Execute(seq);

        Assert.False(result.Success);
        Assert.Empty(seq.Transitions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void ApplyTransitionCommand_rejects_non_positive_duration(double seconds)
    {
        var seq = MakeSequence();
        var left = new TimelineItem { TimelineStart = MediaTime.Zero, Duration = MediaTime.FromSeconds(2) };
        var right = new TimelineItem { TimelineStart = MediaTime.FromSeconds(2), Duration = MediaTime.FromSeconds(2) };
        seq.Tracks[0].Items.AddRange([left, right]);

        var result = new ApplyTransitionCommand
        {
            LeftItemId = left.Id,
            RightItemId = right.Id,
            Duration = MediaTime.FromSeconds(seconds),
        }.Execute(seq);

        Assert.False(result.Success);
        Assert.Empty(seq.Transitions);
    }

    [Fact]
    public void SnapTarget_FindSnap_ReturnsTarget()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        seq.Tracks[0].Items.Add(clip);
        var targets = SnapTarget.FromSequence(seq);

        var snap = SnapTarget.FindSnap(MediaTime.FromSeconds(5.2), targets);

        Assert.NotNull(snap);
        Assert.Equal(5, snap.Value.Seconds, 1);
    }

    [Fact]
    public void SnapTarget_FindSnap_ReturnsNullWhenFar()
    {
        var seq = MakeSequence();
        var clip = MakeClip();
        clip.TimelineStart = MediaTime.FromSeconds(5);
        seq.Tracks[0].Items.Add(clip);
        var targets = SnapTarget.FromSequence(seq);

        var snap = SnapTarget.FindSnap(MediaTime.FromSeconds(20), targets);

        Assert.Null(snap);
    }

    [Fact]
    public void SnapTarget_FromSequence_IncludesMarkers()
    {
        var seq = MakeSequence();
        seq.Markers.Add(new Marker { Label = "M1", Time = MediaTime.FromSeconds(10) });
        var targets = SnapTarget.FromSequence(seq);

        Assert.Contains(targets, t => t.Label.Contains("M1"));
    }

    [Fact]
    public void Keyframe_GetValueAt_NoKeyframes()
    {
        var prop = new AnimatedProperty { PropertyName = "opacity", DefaultValue = 1.0 };
        var value = prop.GetValueAt(MediaTime.FromSeconds(5));
        Assert.Equal(1.0, value);
    }

    [Fact]
    public void Keyframe_GetValueAt_Linear()
    {
        var prop = new AnimatedProperty { PropertyName = "opacity", DefaultValue = 1.0 };
        prop.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(0), Value = 0 });
        prop.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(10), Value = 1 });

        Assert.Equal(0, prop.GetValueAt(MediaTime.FromSeconds(0)));
        Assert.Equal(0.5, prop.GetValueAt(MediaTime.FromSeconds(5)), 3);
        Assert.Equal(1, prop.GetValueAt(MediaTime.FromSeconds(10)));
    }

    [Fact]
    public void Keyframe_GetValueAt_Hold()
    {
        var prop = new AnimatedProperty { PropertyName = "opacity" };
        prop.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(0), Value = 0, Interpolation = InterpolationType.Hold });
        prop.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(10), Value = 1 });

        Assert.Equal(0, prop.GetValueAt(MediaTime.FromSeconds(5)));
    }

    [Fact]
    public void Keyframe_GetValueAt_BezierUsesControlPoints()
    {
        var channel = new AnimationChannel { PropertyName = AnimationPropertyNames.PositionX };
        channel.Keyframes.Add(new Keyframe
        {
            Time = MediaTime.Zero,
            Value = 0,
            Interpolation = InterpolationType.Bezier,
            OutTangentX = 0.1,
            OutTangentY = 0.9,
        });
        channel.Keyframes.Add(new Keyframe
        {
            Time = MediaTime.FromSeconds(1),
            Value = 100,
            InTangentX = 0.9,
            InTangentY = 1,
        });

        var midpoint = channel.GetValueAt(MediaTime.FromSeconds(0.5));

        Assert.True(midpoint > 50);
    }

    [Fact]
    public void TimelineItem_ResolvesMultipleAnimationChannels()
    {
        var item = new TimelineItem();
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = 10,
        });
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.Opacity,
            DefaultValue = 0.5,
        });

        Assert.Equal(10, item.GetAnimatedValue(AnimationPropertyNames.PositionX, MediaTime.Zero, 0));
        Assert.Equal(0.5, item.GetAnimatedValue(AnimationPropertyNames.Opacity, MediaTime.Zero, 1));
    }

    [Fact]
    public void Keyframe_GetValueAt_BeforeFirst()
    {
        var prop = new AnimatedProperty { PropertyName = "opacity" };
        prop.Keyframes.Add(new Keyframe { Time = MediaTime.FromSeconds(5), Value = 1 });

        Assert.Equal(1, prop.GetValueAt(MediaTime.FromSeconds(0)));
    }

    [Fact]
    public void EffectRegistry_GetReturnsNullForUnknown()
    {
        var registry = new Infrastructure.EffectRegistry();
        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void EffectRegistry_GetReturnsDefinition()
    {
        var registry = new Infrastructure.EffectRegistry();
        var def = registry.Get("blur");
        Assert.NotNull(def);
        Assert.Equal("Blur", def.Name);
        Assert.Equal("distortion", def.Category);
    }

    [Fact]
    public void EffectRegistry_GetByCategory()
    {
        var registry = new Infrastructure.EffectRegistry();
        var results = registry.GetByCategory("color");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("color", r.Category));
    }

    [Fact]
    public void CachePolicy_Defaults()
    {
        var policy = new CachePolicy();
        Assert.Equal(500_000_000, policy.ThumbnailCacheSizeBytes);
        Assert.Equal(TimeSpan.FromDays(7), policy.CacheEvictionAge);
    }

    [Fact]
    public void SpeedCurve_MapsSegmentedSourceTime()
    {
        var curve = new SpeedCurve { ConstantSpeed = 1.0 };
        curve.Segments.Add(new SpeedSegment
        {
            SourceStart = MediaTime.FromSeconds(0),
            SourceEnd = MediaTime.FromSeconds(5),
            Speed = 0.5,
        });
        curve.Segments.Add(new SpeedSegment
        {
            SourceStart = MediaTime.FromSeconds(5),
            SourceEnd = MediaTime.FromSeconds(10),
            Speed = 2.0,
        });

        Assert.Equal(10, curve.MapSourceToTimeline(5), 3);
        Assert.Equal(12.5, curve.MapSourceToTimeline(10), 3);
        Assert.Equal(14.5, curve.MapSourceToTimeline(12), 3);
    }

    [Fact]
    public void RippleState_EnabledDefault()
    {
        var state = new RippleState();
        Assert.False(state.Enabled);
    }
}
