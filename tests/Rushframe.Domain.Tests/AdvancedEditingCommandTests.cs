using Rushframe.Domain.Editing;

namespace Rushframe.Domain.Tests;

public sealed class AdvancedEditingCommandTests
{
    [Fact]
    public void Update_transform_command_is_undoable()
    {
        var sequence = new Sequence();
        var item = new TimelineItem { Kind = ItemKind.Image, Duration = MediaTime.FromSeconds(2) };
        item.Transform.PositionX = 10;
        sequence.Tracks.Add(new Track { Kind = TrackKind.Overlay, Items = { item } });
        var stack = new UndoRedoStack();

        var result = stack.Execute(sequence, new UpdateTransformCommand
        {
            ItemId = item.Id,
            NewTransform = new Transform2D
            {
                PositionX = 80,
                PositionY = 40,
                ScaleX = 0.5,
                ScaleY = 0.7,
                RotationDegrees = 25,
            },
        });

        Assert.True(result.Success);
        Assert.Equal(80, item.Transform.PositionX);
        Assert.Equal(0.7, item.Transform.ScaleY);
        Assert.True(stack.Undo(sequence).Success);
        Assert.Equal(10, item.Transform.PositionX);
        Assert.Equal(1, item.Transform.ScaleY);
    }

    [Fact]
    public void Update_animation_channels_command_replaces_and_restores_channels()
    {
        var sequence = new Sequence();
        var item = new TimelineItem { Kind = ItemKind.Image, Duration = MediaTime.FromSeconds(2) };
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.Opacity,
            DefaultValue = 1,
        });
        sequence.Tracks.Add(new Track { Kind = TrackKind.Overlay, Items = { item } });
        var stack = new UndoRedoStack();

        var result = stack.Execute(sequence, new UpdateAnimationChannelsCommand
        {
            ItemId = item.Id,
            NewChannels =
            [
                new AnimationChannel
                {
                    PropertyName = AnimationPropertyNames.PositionX,
                    DefaultValue = 12,
                    Keyframes =
                    {
                        new Keyframe { Time = MediaTime.Zero, Value = 0 },
                        new Keyframe { Time = MediaTime.FromSeconds(2), Value = 100 },
                    },
                },
            ],
        });

        Assert.True(result.Success);
        Assert.Equal(AnimationPropertyNames.PositionX, item.AnimationChannels.Single().PropertyName);
        Assert.Equal(MediaTime.FromSeconds(2), item.AnimationChannels.Single().Keyframes[1].Time);
        Assert.True(stack.Undo(sequence).Success);
        Assert.Equal(AnimationPropertyNames.Opacity, item.AnimationChannels.Single().PropertyName);
    }

    [Fact]
    public void Update_sequence_settings_command_is_undoable()
    {
        var sequence = new Sequence { Width = 1080, Height = 1920, FrameRate = FrameRate.Fps30 };
        var stack = new UndoRedoStack();

        var result = stack.Execute(sequence, new UpdateSequenceSettingsCommand
        {
            SequenceId = sequence.Id,
            Width = 1920,
            Height = 1080,
            FrameRate = FrameRate.Fps23_976,
            Background = new CanvasBackground
            {
                Kind = CanvasBackgroundKind.LinearGradient,
                PrimaryColor = "#112233",
                SecondaryColor = "#445566",
            },
            LayoutGuides =
            [
                new LayoutGuide { Kind = LayoutGuideKind.YouTubeShorts, Name = "Shorts" },
            ],
        });

        Assert.True(result.Success);
        Assert.Equal(1920, sequence.Width);
        Assert.Equal(FrameRate.Fps23_976, sequence.FrameRate);
        Assert.Single(sequence.LayoutGuides);
        Assert.True(stack.Undo(sequence).Success);
        Assert.Equal(1080, sequence.Width);
        Assert.Equal(FrameRate.Fps30, sequence.FrameRate);
        Assert.Empty(sequence.LayoutGuides);
    }

    [Fact]
    public void Edit_marker_command_tracks_note_color_and_duration()
    {
        var sequence = new Sequence();
        var marker = new Marker
        {
            Label = "Before",
            Note = "Old note",
            Time = MediaTime.FromSeconds(1),
            Duration = MediaTime.FromSeconds(0.5),
            Color = "#ffffff",
        };
        sequence.Markers.Add(marker);
        var stack = new UndoRedoStack();

        var result = stack.Execute(sequence, new EditMarkerCommand
        {
            MarkerId = marker.Id,
            NewLabel = "Hook",
            NewNote = "Strong opening",
            NewTime = MediaTime.FromSeconds(2),
            NewDuration = MediaTime.FromSeconds(1.25),
            NewColor = "#ffcc00",
        });

        Assert.True(result.Success);
        Assert.Equal("Strong opening", marker.Note);
        Assert.Equal(1.25, marker.Duration.Seconds, 3);
        Assert.True(stack.Undo(sequence).Success);
        Assert.Equal("Old note", marker.Note);
        Assert.Equal(0.5, marker.Duration.Seconds, 3);
    }

    [Fact]
    public void Add_effect_redo_restores_same_identity_order_and_enabled_state()
    {
        var sequence = new Sequence();
        var item = new TimelineItem { Kind = ItemKind.Image, Duration = MediaTime.FromSeconds(2) };
        item.Effects.Add(new EffectInstance { EffectTypeId = "first" });
        sequence.Tracks.Add(new Track { Kind = TrackKind.Overlay, Items = { item } });
        var stack = new UndoRedoStack();
        var command = new AddEffectCommand
        {
            ItemId = item.Id,
            EffectTypeId = "blur",
            Enabled = false,
            Parameters = new Dictionary<string, object> { ["strength"] = 3d },
        };

        Assert.True(stack.Execute(sequence, command).Success);
        var added = item.Effects[1];
        var addedId = added.Id;
        Assert.False(added.Enabled);

        Assert.True(stack.Undo(sequence).Success);
        Assert.Single(item.Effects);
        Assert.True(stack.Redo(sequence).Success);

        Assert.Equal(2, item.Effects.Count);
        Assert.Equal(addedId, item.Effects[1].Id);
        Assert.False(item.Effects[1].Enabled);
        Assert.Equal(3d, item.Effects[1].Parameters["strength"]);
    }

    [Fact]
    public void Text_layout_metrics_include_multiline_outline_and_shadow_bounds()
    {
        var item = new TimelineItem
        {
            Kind = ItemKind.Text,
            TextContent = "Long line\nShort",
            FontSize = 40,
            OutlineWidth = 2,
            ShadowOffsetX = 6,
            ShadowOffsetY = -4,
            ShadowBlur = 5,
        };

        var measured = TextLayoutMetrics.Measure(item);

        Assert.True(measured.Width > 40 * "Long line".Length * 0.62);
        Assert.True(measured.Height > 40 * 1.4 * 2);
    }

    [Fact]
    public void Project_revision_is_monotonic()
    {
        var project = new Project();

        var first = project.IncrementRevision();
        var second = project.IncrementRevision();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, project.Revision);
    }
}
