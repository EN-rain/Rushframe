using System.Globalization;
using System.IO;
using System.Text.Json;
using Rushframe.Application;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop.Controllers;

internal sealed record AgentEditBuildResult(bool Success, IEditCommand? Command, string Summary, string? Error)
{
    public static AgentEditBuildResult Ok(IEditCommand command, string summary) => new(true, command, summary, null);
    public static AgentEditBuildResult Fail(string error) => new(false, null, string.Empty, error);
}

/// <summary>
/// Compiles the stable Rushframe agent-edit protocol into the same undoable
/// domain commands used by the manual editor. No agent operation receives a raw
/// FFmpeg escape hatch and no operation mutates source media.
/// </summary>
internal sealed class AgentEditCommandFactory
{
    public static readonly IReadOnlyList<string> SupportedActions =
    [
        "add_text", "add_caption", "add_clip", "add_music", "move_clip", "trim_clip",
        "split_clip", "delete_clip", "ripple_delete_clip", "duplicate_clip",
        "set_transform", "set_item_properties", "set_text_content", "set_text_properties",
        "add_transition", "add_effect", "remove_effect", "update_effect", "reorder_effect",
        "set_animation_channels", "set_masks", "set_chroma_key",
        "add_marker", "edit_marker", "delete_marker", "clear_markers",
        "add_track", "delete_track", "duplicate_track", "rename_track", "reorder_track",
        "toggle_track_mute", "toggle_track_solo", "toggle_track_lock", "update_sequence",
        "add_captions_from_transcript", "create_clip_from_transcript",
        "assemble_best_moments", "use_best_take", "remove_silence",
    ];

    public AgentEditBuildResult Build(Project project, Sequence sequence, JsonElement payload, string action, double playheadSeconds)
    {
        try
        {
            return action.Trim().ToLowerInvariant() switch
            {
                "add_text" or "add_caption" => BuildAddText(project, sequence, payload, playheadSeconds),
                "add_clip" or "add_music" => BuildAddMedia(project, sequence, payload, playheadSeconds),
                "move_clip" => BuildMove(sequence, payload),
                "trim_clip" => BuildTrim(sequence, payload),
                "split_clip" => BuildSplit(sequence, payload),
                "delete_clip" => BuildDelete(sequence, payload, ripple: false),
                "ripple_delete_clip" => BuildDelete(sequence, payload, ripple: true),
                "duplicate_clip" => BuildDuplicate(sequence, payload),
                "set_transform" => BuildTransform(sequence, payload),
                "set_item_properties" => BuildItemProperties(sequence, payload),
                "set_text_content" => BuildTextContent(sequence, payload),
                "set_text_properties" => BuildTextProperties(project, sequence, payload),
                "add_transition" => BuildTransition(payload),
                "add_effect" => BuildAddEffect(payload),
                "remove_effect" => BuildRemoveEffect(payload),
                "update_effect" => BuildUpdateEffect(payload),
                "reorder_effect" => BuildReorderEffect(payload),
                "set_animation_channels" => BuildAnimationChannels(sequence, payload),
                "set_masks" => BuildMasks(sequence, payload),
                "set_chroma_key" => BuildChromaKey(sequence, payload),
                "add_marker" => BuildAddMarker(payload, playheadSeconds),
                "edit_marker" => BuildEditMarker(payload),
                "delete_marker" => BuildDeleteMarker(payload),
                "clear_markers" => AgentEditBuildResult.Ok(new ClearMarkersCommand(), "Clear timeline markers"),
                "add_track" => BuildAddTrack(payload),
                "delete_track" => BuildDeleteTrack(payload),
                "duplicate_track" => BuildDuplicateTrack(payload),
                "rename_track" => BuildRenameTrack(payload),
                "reorder_track" => BuildReorderTrack(payload),
                "toggle_track_mute" => BuildTrackToggle(payload, "mute"),
                "toggle_track_solo" => BuildTrackToggle(payload, "solo"),
                "toggle_track_lock" => BuildTrackToggle(payload, "lock"),
                "update_sequence" => BuildUpdateSequence(sequence, payload),
                "add_captions_from_transcript" => BuildCaptionsFromTranscript(project, sequence, payload, playheadSeconds),
                "create_clip_from_transcript" => BuildClipFromTranscript(project, sequence, payload, playheadSeconds),
                "assemble_best_moments" => BuildBestMoments(project, sequence, payload, playheadSeconds),
                "use_best_take" => BuildBestTake(project, sequence, payload, playheadSeconds),
                "remove_silence" => BuildRemoveSilence(project, sequence, payload),
                _ => AgentEditBuildResult.Fail($"Unsupported action: {action}"),
            };
        }
        catch (Exception ex)
        {
            return AgentEditBuildResult.Fail(ex.Message);
        }
    }

    private static AgentEditBuildResult BuildAddText(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var (track, addTrackCommand) = ResolveOrPrepareTrack(sequence, payload, TrackKind.Text);
        if (track == null) return AgentEditBuildResult.Fail("The requested text track is missing or locked.");
        var text = AgentPayloadReader.ReadString(payload, "text");
        if (string.IsNullOrWhiteSpace(text)) return AgentEditBuildResult.Fail("Missing text");
        var start = AgentPayloadReader.ReadSeconds(payload, "start", playheadSeconds);
        var duration = AgentPayloadReader.ReadSeconds(payload, "duration", 3);
        var item = CreateTextItem(project, payload, text, start, duration);
        var command = addTrackCommand == null
            ? (IEditCommand)new AddClipCommand { TrackId = track.Id, Item = item }
            : new CompositeEditCommand("Add text track and text", [addTrackCommand, new AddClipCommand { TrackId = track.Id, Item = item }]);
        return AgentEditBuildResult.Ok(command, $"Add text at {start:0.##}s for {duration:0.##}s");
    }

    private static TimelineItem CreateTextItem(Project project, JsonElement payload, string text, double start, double duration) => new()
    {
        Kind = ItemKind.Text,
        TimelineStart = MediaTime.FromSeconds(Math.Max(0, start)),
        Duration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
        SourceDuration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
        TextContent = text,
        FontFamily = ResolveAgentFontFamily(project, AgentPayloadReader.ReadString(payload, "font_family") ?? "Arial"),
        FontSize = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "font_size", 48), 1, 1000),
        FontBold = AgentPayloadReader.ReadBool(payload, "font_bold", true),
        FontAlign = AgentPayloadReader.ReadString(payload, "font_align") ?? "center",
        FillColor = AgentPayloadReader.ReadString(payload, "fill_color") ?? "#FFFFFF",
        OutlineColor = AgentPayloadReader.ReadString(payload, "outline_color") ?? "#000000",
        OutlineWidth = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "outline_width", 2), 0, 100),
        ShadowColor = AgentPayloadReader.ReadString(payload, "shadow_color") ?? "#000000",
        ShadowOpacity = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "shadow_opacity", 0.5), 0, 1),
        Transform = new Transform2D
        {
            PositionX = AgentPayloadReader.ReadSeconds(payload, "x", 0),
            PositionY = AgentPayloadReader.ReadSeconds(payload, "y", 0),
            ScaleX = AgentPayloadReader.ReadSeconds(payload, "scale_x", 1),
            ScaleY = AgentPayloadReader.ReadSeconds(payload, "scale_y", 1),
            RotationDegrees = AgentPayloadReader.ReadSeconds(payload, "rotation", 0),
        },
    };

    private static AgentEditBuildResult BuildAddMedia(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var assetId = AgentPayloadReader.ParseMediaAssetId(AgentPayloadReader.ReadRequiredString(payload, "media_asset_id"));
        var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
        if (asset == null) return AgentEditBuildResult.Fail("Media asset not found");
        if (asset.IsOffline || !File.Exists(asset.OriginalPath)) return AgentEditBuildResult.Fail("Media asset is offline");
        var defaultTrackKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : asset.Kind == MediaKind.Image ? TrackKind.Overlay : TrackKind.Video;
        var (track, addTrackCommand) = ResolveOrPrepareTrack(sequence, payload, defaultTrackKind);
        if (track == null) return AgentEditBuildResult.Fail($"The requested {defaultTrackKind} track is missing or locked.");
        var start = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "start", playheadSeconds));
        var sourceStart = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "source_start", 0));
        var available = Math.Max(0.1, asset.Duration.Seconds - sourceStart);
        var duration = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "duration", available), 0.1, available);
        var kind = asset.Kind == MediaKind.Image ? ItemKind.Image : ItemKind.Clip;
        var item = new TimelineItem
        {
            Kind = kind,
            MediaAssetId = asset.Id,
            TimelineStart = MediaTime.FromSeconds(start),
            Duration = MediaTime.FromSeconds(duration),
            SourceStart = MediaTime.FromSeconds(sourceStart),
            SourceDuration = MediaTime.FromSeconds(duration),
            FadeInDuration = MediaTime.FromSeconds(Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fade_in", 0), 0, duration)),
            FadeOutDuration = MediaTime.FromSeconds(Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fade_out", 0), 0, duration)),
            Volume = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "volume", 1), 0, 4),
        };
        var command = addTrackCommand == null
            ? (IEditCommand)new AddClipCommand { TrackId = track.Id, Item = item }
            : new CompositeEditCommand($"Add {defaultTrackKind} track and media", [addTrackCommand, new AddClipCommand { TrackId = track.Id, Item = item }]);
        return AgentEditBuildResult.Ok(command, $"Add {Path.GetFileName(asset.OriginalPath)} at {start:0.##}s");
    }

    private static AgentEditBuildResult BuildMove(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        TrackId? targetTrackId = AgentPayloadReader.ReadString(payload, "track_id") is { Length: > 0 } trackText
            ? AgentPayloadReader.ParseTrackId(trackText)
            : null;
        var command = new MoveClipCommand
        {
            ItemId = itemId,
            TargetTrackId = targetTrackId,
            NewTimelineStart = AgentPayloadReader.HasProperty(payload, "start")
                ? MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "start", 0)))
                : null,
            NewIndex = AgentPayloadReader.ReadNullableInt(payload, "index"),
        };
        return FindItem(sequence, itemId).Item != null
            ? AgentEditBuildResult.Ok(command, $"Move clip {itemId}")
            : AgentEditBuildResult.Fail("Clip not found");
    }

    private static AgentEditBuildResult BuildTrim(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (track, item) = FindItem(sequence, itemId);
        if (track == null || item == null) return AgentEditBuildResult.Fail("Clip not found");
        var command = new TrimClipCommand
        {
            TrackId = track.Id,
            ItemId = itemId,
            NewStart = AgentPayloadReader.HasProperty(payload, "start")
                ? MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "start", 0)))
                : null,
            NewDuration = AgentPayloadReader.HasProperty(payload, "duration")
                ? MediaTime.FromSeconds(Math.Max(0.05, AgentPayloadReader.ReadSeconds(payload, "duration", item.Duration.Seconds)))
                : null,
            NewSourceStart = AgentPayloadReader.HasProperty(payload, "source_start")
                ? MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "source_start", item.SourceStart.Seconds)))
                : null,
        };
        return AgentEditBuildResult.Ok(command, $"Trim clip {itemId}");
    }

    private static AgentEditBuildResult BuildSplit(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (track, item) = FindItem(sequence, itemId);
        if (track == null || item == null) return AgentEditBuildResult.Fail("Clip not found");
        var splitTime = AgentPayloadReader.ReadSeconds(payload, "time", item.TimelineStart.Seconds + item.Duration.Seconds / 2);
        return AgentEditBuildResult.Ok(
            new SplitClipCommand { TrackId = track.Id, ItemId = itemId, SplitTime = MediaTime.FromSeconds(splitTime) },
            $"Split clip {itemId} at {splitTime:0.##}s");
    }

    private static AgentEditBuildResult BuildDelete(Sequence sequence, JsonElement payload, bool ripple)
    {
        var itemId = ReadItemId(payload);
        if (FindItem(sequence, itemId).Item == null) return AgentEditBuildResult.Fail("Clip not found");
        IEditCommand command = ripple
            ? new RippleDeleteClipCommand { ItemId = itemId }
            : new DeleteClipCommand { ItemId = itemId };
        return AgentEditBuildResult.Ok(command, ripple ? $"Ripple delete {itemId}" : $"Delete {itemId}");
    }

    private static AgentEditBuildResult BuildDuplicate(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        if (FindItem(sequence, itemId).Item == null) return AgentEditBuildResult.Fail("Clip not found");
        return AgentEditBuildResult.Ok(new DuplicateClipCommand { ItemId = itemId }, $"Duplicate clip {itemId}");
    }

    private static AgentEditBuildResult BuildTransform(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (_, item) = FindItem(sequence, itemId);
        if (item == null) return AgentEditBuildResult.Fail("Clip not found");
        var transform = new Transform2D
        {
            PositionX = AgentPayloadReader.ReadSeconds(payload, "x", item.Transform.PositionX),
            PositionY = AgentPayloadReader.ReadSeconds(payload, "y", item.Transform.PositionY),
            ScaleX = Math.Max(0.001, AgentPayloadReader.ReadSeconds(payload, "scale_x", item.Transform.ScaleX)),
            ScaleY = Math.Max(0.001, AgentPayloadReader.ReadSeconds(payload, "scale_y", item.Transform.ScaleY)),
            RotationDegrees = AgentPayloadReader.ReadSeconds(payload, "rotation", item.Transform.RotationDegrees),
            AnchorX = AgentPayloadReader.ReadSeconds(payload, "anchor_x", item.Transform.AnchorX),
            AnchorY = AgentPayloadReader.ReadSeconds(payload, "anchor_y", item.Transform.AnchorY),
        };
        return AgentEditBuildResult.Ok(new UpdateTransformCommand { ItemId = itemId, NewTransform = transform }, $"Transform clip {itemId}");
    }

    private static AgentEditBuildResult BuildItemProperties(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (_, item) = FindItem(sequence, itemId);
        if (item == null) return AgentEditBuildResult.Fail("Clip not found");
        var commands = new List<IEditCommand>();

        AddDoubleProperty(commands, payload, "opacity", itemId, nameof(TimelineItem.Opacity), i => i.Opacity, (i, v) => i.Opacity = Math.Clamp(v, 0, 1));
        AddDoubleProperty(commands, payload, "volume", itemId, nameof(TimelineItem.Volume), i => i.Volume, (i, v) => i.Volume = Math.Clamp(v, 0, 4));
        AddDoubleProperty(commands, payload, "pan", itemId, nameof(TimelineItem.Pan), i => i.Pan, (i, v) => i.Pan = Math.Clamp(v, -1, 1));
        AddDoubleProperty(commands, payload, "crop_left", itemId, nameof(TimelineItem.CropLeft), i => i.CropLeft, (i, v) => i.CropLeft = Math.Clamp(v, 0, 1));
        AddDoubleProperty(commands, payload, "crop_top", itemId, nameof(TimelineItem.CropTop), i => i.CropTop, (i, v) => i.CropTop = Math.Clamp(v, 0, 1));
        AddDoubleProperty(commands, payload, "crop_right", itemId, nameof(TimelineItem.CropRight), i => i.CropRight, (i, v) => i.CropRight = Math.Clamp(v, 0, 1));
        AddDoubleProperty(commands, payload, "crop_bottom", itemId, nameof(TimelineItem.CropBottom), i => i.CropBottom, (i, v) => i.CropBottom = Math.Clamp(v, 0, 1));
        AddBoolProperty(commands, payload, "muted", itemId, nameof(TimelineItem.Muted), i => i.Muted, (i, v) => i.Muted = v);
        AddBoolProperty(commands, payload, "reversed", itemId, nameof(TimelineItem.Reversed), i => i.Reversed, (i, v) => i.Reversed = v);

        if (AgentPayloadReader.HasProperty(payload, "fade_in"))
        {
            var value = MediaTime.FromSeconds(Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fade_in", 0), 0, item.Duration.Seconds));
            commands.Add(SetValue(itemId, nameof(TimelineItem.FadeInDuration), value, i => i.FadeInDuration, (i, v) => i.FadeInDuration = (MediaTime)v!));
        }
        if (AgentPayloadReader.HasProperty(payload, "fade_out"))
        {
            var value = MediaTime.FromSeconds(Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fade_out", 0), 0, item.Duration.Seconds));
            commands.Add(SetValue(itemId, nameof(TimelineItem.FadeOutDuration), value, i => i.FadeOutDuration, (i, v) => i.FadeOutDuration = (MediaTime)v!));
        }
        if (AgentPayloadReader.HasProperty(payload, "speed"))
        {
            var speed = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "speed", 1), 0.05, 100);
            commands.Add(SetValue(itemId, nameof(TimelineItem.SpeedCurve), new SpeedCurve { ConstantSpeed = speed, PreservePitch = true }, i => i.SpeedCurve, (i, v) => i.SpeedCurve = (SpeedCurve?)v));
        }
        if (AgentPayloadReader.ReadString(payload, "blend_mode") is { Length: > 0 } blendText
            && Enum.TryParse<BlendMode>(blendText, true, out var blendMode))
        {
            commands.Add(SetValue(itemId, nameof(TimelineItem.BlendMode), blendMode, i => i.BlendMode, (i, v) => i.BlendMode = (BlendMode)v!));
        }
        if (AgentPayloadReader.TryGetProperty(payload, "color", out var colorElement))
        {
            var current = item.ColorCorrection ?? new ColorCorrection();
            var color = new ColorCorrection
            {
                Brightness = AgentPayloadReader.ReadSeconds(colorElement, "brightness", current.Brightness),
                Contrast = AgentPayloadReader.ReadSeconds(colorElement, "contrast", current.Contrast),
                Saturation = AgentPayloadReader.ReadSeconds(colorElement, "saturation", current.Saturation),
                Exposure = AgentPayloadReader.ReadSeconds(colorElement, "exposure", current.Exposure),
                Highlights = AgentPayloadReader.ReadSeconds(colorElement, "highlights", current.Highlights),
                Shadows = AgentPayloadReader.ReadSeconds(colorElement, "shadows", current.Shadows),
                Whites = AgentPayloadReader.ReadSeconds(colorElement, "whites", current.Whites),
                Blacks = AgentPayloadReader.ReadSeconds(colorElement, "blacks", current.Blacks),
                Tint = AgentPayloadReader.ReadSeconds(colorElement, "tint", current.Tint),
                BlackAndWhite = AgentPayloadReader.ReadBool(colorElement, "black_and_white", current.BlackAndWhite),
            };
            commands.Add(SetValue(itemId, nameof(TimelineItem.ColorCorrection), color, i => i.ColorCorrection, (i, v) => i.ColorCorrection = (ColorCorrection?)v));
        }
        if (AgentPayloadReader.TryGetProperty(payload, "stabilization", out var stabilizationElement))
        {
            var current = item.Stabilization ?? new StabilizationSettings();
            var stabilization = new StabilizationSettings
            {
                Enabled = AgentPayloadReader.ReadBool(stabilizationElement, "enabled", current.Enabled),
                Strength = Math.Clamp(AgentPayloadReader.ReadSeconds(stabilizationElement, "strength", current.Strength), 0, 1),
                CropZoomCompensation = AgentPayloadReader.ReadBool(stabilizationElement, "crop_zoom_compensation", current.CropZoomCompensation),
                AnalysisComplete = current.AnalysisComplete,
            };
            commands.Add(SetValue(itemId, nameof(TimelineItem.Stabilization), stabilization, i => i.Stabilization, (i, v) => i.Stabilization = (StabilizationSettings?)v));
        }

        return commands.Count == 0
            ? AgentEditBuildResult.Fail("No supported item properties were provided")
            : AgentEditBuildResult.Ok(new CompositeEditCommand("Set item properties", commands), $"Update {commands.Count} properties on {itemId}");
    }

    private static AgentEditBuildResult BuildTextContent(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (_, item) = FindItem(sequence, itemId);
        if (item?.Kind != ItemKind.Text) return AgentEditBuildResult.Fail("Text item not found");
        return AgentEditBuildResult.Ok(
            new SetTextContentCommand { ItemId = itemId, NewText = AgentPayloadReader.ReadRequiredString(payload, "text") },
            $"Update text {itemId}");
    }

    private static AgentEditBuildResult BuildTextProperties(Project project, Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (_, item) = FindItem(sequence, itemId);
        if (item?.Kind != ItemKind.Text) return AgentEditBuildResult.Fail("Text item not found");
        var command = new SetTextPropertiesCommand
        {
            ItemId = itemId,
            FontFamily = AgentPayloadReader.ReadString(payload, "font_family") is { } fontFamily
                ? ResolveAgentFontFamily(project, fontFamily)
                : null,
            FontSize = AgentPayloadReader.ReadNullableDouble(payload, "font_size"),
            FontBold = AgentPayloadReader.ReadNullableBool(payload, "font_bold"),
            FontAlign = AgentPayloadReader.ReadString(payload, "font_align"),
            FillColor = AgentPayloadReader.ReadString(payload, "fill_color"),
            OutlineColor = AgentPayloadReader.ReadString(payload, "outline_color"),
            OutlineWidth = AgentPayloadReader.ReadNullableDouble(payload, "outline_width"),
            ShadowColor = AgentPayloadReader.ReadString(payload, "shadow_color"),
            ShadowOffsetX = AgentPayloadReader.ReadNullableDouble(payload, "shadow_offset_x"),
            ShadowOffsetY = AgentPayloadReader.ReadNullableDouble(payload, "shadow_offset_y"),
            ShadowBlur = AgentPayloadReader.ReadNullableDouble(payload, "shadow_blur"),
            ShadowOpacity = AgentPayloadReader.ReadNullableDouble(payload, "shadow_opacity"),
        };
        return AgentEditBuildResult.Ok(command, $"Style text {itemId}");
    }

    private static AgentEditBuildResult BuildTransition(JsonElement payload)
    {
        var left = AgentPayloadReader.ParseTimelineItemId(AgentPayloadReader.ReadRequiredString(payload, "left_item_id"));
        var right = AgentPayloadReader.ParseTimelineItemId(AgentPayloadReader.ReadRequiredString(payload, "right_item_id"));
        var kindText = AgentPayloadReader.ReadString(payload, "kind") ?? nameof(TransitionKind.CrossDissolve);
        if (!Enum.TryParse<TransitionKind>(kindText, true, out var kind)) return AgentEditBuildResult.Fail($"Unknown transition: {kindText}");
        var duration = AgentPayloadReader.ReadSeconds(payload, "duration", 0.5);
        var alignment = AgentPayloadReader.ReadSeconds(payload, "alignment", 0.5);
        return AgentEditBuildResult.Ok(
            new ApplyTransitionCommand
            {
                LeftItemId = left,
                RightItemId = right,
                Kind = kind,
                Duration = MediaTime.FromSeconds(Math.Max(0.05, duration)),
                Alignment = Math.Clamp(alignment, 0, 1),
            },
            $"Apply {kind} transition");
    }

    private static AgentEditBuildResult BuildAddEffect(JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var effectTypeId = AgentPayloadReader.ReadRequiredString(payload, "effect_type_id");
        return AgentEditBuildResult.Ok(
            new AddEffectCommand { ItemId = itemId, EffectTypeId = effectTypeId, Parameters = AgentPayloadReader.ReadObject(payload, "parameters") },
            $"Add effect {effectTypeId}");
    }

    private static AgentEditBuildResult BuildRemoveEffect(JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var effectId = AgentPayloadReader.ParseEffectInstanceId(AgentPayloadReader.ReadRequiredString(payload, "effect_id"));
        return AgentEditBuildResult.Ok(new RemoveEffectCommand { ItemId = itemId, EffectInstanceId = effectId }, $"Remove effect {effectId}");
    }

    private static AgentEditBuildResult BuildUpdateEffect(JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var effectId = AgentPayloadReader.ParseEffectInstanceId(AgentPayloadReader.ReadRequiredString(payload, "effect_id"));
        return AgentEditBuildResult.Ok(
            new UpdateEffectCommand
            {
                ItemId = itemId,
                EffectInstanceId = effectId,
                Enabled = AgentPayloadReader.ReadBool(payload, "enabled", true),
                Parameters = AgentPayloadReader.ReadObject(payload, "parameters"),
            },
            $"Update effect {effectId}");
    }

    private static AgentEditBuildResult BuildReorderEffect(JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var effectId = AgentPayloadReader.ParseEffectInstanceId(AgentPayloadReader.ReadRequiredString(payload, "effect_id"));
        return AgentEditBuildResult.Ok(
            new ReorderEffectCommand { ItemId = itemId, EffectInstanceId = effectId, NewIndex = AgentPayloadReader.ReadInt(payload, "index", 0) },
            $"Reorder effect {effectId}");
    }

    private static AgentEditBuildResult BuildAnimationChannels(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        if (FindItem(sequence, itemId).Item == null) return AgentEditBuildResult.Fail("Clip not found");
        if (!AgentPayloadReader.TryGetProperty(payload, "channels", out var channelsElement) || channelsElement.ValueKind != JsonValueKind.Array)
            return AgentEditBuildResult.Fail("Missing channels array");
        var channels = JsonSerializer.Deserialize<List<AnimationChannel>>(channelsElement.GetRawText(), AgentPayloadReader.JsonOptions) ?? [];
        foreach (var channel in channels) channel.NormalizeKeyframes();
        return AgentEditBuildResult.Ok(
            new UpdateAnimationChannelsCommand { ItemId = itemId, NewChannels = channels },
            $"Update {channels.Count} animation channels on {itemId}");
    }

    private static AgentEditBuildResult BuildMasks(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        if (FindItem(sequence, itemId).Item == null) return AgentEditBuildResult.Fail("Clip not found");
        if (!AgentPayloadReader.TryGetProperty(payload, "masks", out var masksElement) || masksElement.ValueKind != JsonValueKind.Array)
            return AgentEditBuildResult.Fail("Missing masks array");
        var masks = JsonSerializer.Deserialize<List<Mask>>(masksElement.GetRawText(), AgentPayloadReader.JsonOptions) ?? [];
        return AgentEditBuildResult.Ok(
            SetValue(
                itemId,
                nameof(TimelineItem.Masks),
                masks,
                item => Clone(item.Masks),
                (item, value) =>
                {
                    item.Masks.Clear();
                    item.Masks.AddRange(Clone((IEnumerable<Mask>)value!));
                }),
            $"Replace masks on {itemId}");
    }

    private static AgentEditBuildResult BuildChromaKey(Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        if (FindItem(sequence, itemId).Item == null) return AgentEditBuildResult.Fail("Clip not found");
        ChromaKey? chroma = null;
        if (!AgentPayloadReader.ReadBool(payload, "clear", false))
        {
            chroma = new ChromaKey
            {
                Color = AgentPayloadReader.ReadString(payload, "color") ?? "#00FF00",
                Similarity = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "similarity", 0.1), 0, 1),
                Intensity = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "intensity", 0.1), 0, 1),
                EdgeSoftness = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "edge_softness", 0.05), 0, 1),
                SpillSuppression = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "spill_suppression", 0.1), 0, 1),
                ShadowSuppression = AgentPayloadReader.ReadBool(payload, "shadow_suppression", false),
            };
        }
        return AgentEditBuildResult.Ok(
            SetValue(itemId, nameof(TimelineItem.ChromaKey), chroma, i => Clone(i.ChromaKey), (i, v) => i.ChromaKey = Clone((ChromaKey?)v)),
            chroma == null ? $"Clear chroma key on {itemId}" : $"Set chroma key on {itemId}");
    }

    private static AgentEditBuildResult BuildAddMarker(JsonElement payload, double playheadSeconds)
    {
        var time = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "time", playheadSeconds));
        var marker = new Marker
        {
            Label = AgentPayloadReader.ReadString(payload, "label") ?? "Marker",
            Time = MediaTime.FromSeconds(time),
            Duration = MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "duration", 0))),
            Note = AgentPayloadReader.ReadString(payload, "note"),
            Color = AgentPayloadReader.ReadString(payload, "color"),
        };
        return AgentEditBuildResult.Ok(new AddMarkerCommand { Marker = marker }, $"Add marker at {time:0.##}s");
    }

    private static AgentEditBuildResult BuildEditMarker(JsonElement payload)
    {
        var markerId = AgentPayloadReader.ParseMarkerId(AgentPayloadReader.ReadRequiredString(payload, "marker_id"));
        return AgentEditBuildResult.Ok(
            new EditMarkerCommand
            {
                MarkerId = markerId,
                NewLabel = AgentPayloadReader.ReadString(payload, "label") ?? "Marker",
                NewTime = MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "time", 0))),
                NewDuration = MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "duration", 0))),
                NewNote = AgentPayloadReader.ReadString(payload, "note"),
                NewColor = AgentPayloadReader.ReadString(payload, "color"),
            },
            $"Edit marker {markerId}");
    }

    private static AgentEditBuildResult BuildDeleteMarker(JsonElement payload)
    {
        var markerId = AgentPayloadReader.ParseMarkerId(AgentPayloadReader.ReadRequiredString(payload, "marker_id"));
        return AgentEditBuildResult.Ok(new DeleteMarkerCommand { MarkerId = markerId }, $"Delete marker {markerId}");
    }

    private static AgentEditBuildResult BuildAddTrack(JsonElement payload)
    {
        var kindText = AgentPayloadReader.ReadString(payload, "kind") ?? nameof(TrackKind.Video);
        if (!Enum.TryParse<TrackKind>(kindText, true, out var kind)) return AgentEditBuildResult.Fail($"Unknown track kind: {kindText}");
        return AgentEditBuildResult.Ok(
            new AddTrackCommand { TrackKind = kind, InsertAt = AgentPayloadReader.ReadNullableInt(payload, "index") },
            $"Add {kind} track");
    }

    private static AgentEditBuildResult BuildDeleteTrack(JsonElement payload)
    {
        var id = ReadTrackId(payload);
        return AgentEditBuildResult.Ok(new DeleteTrackCommand { TrackId = id }, $"Delete track {id}");
    }

    private static AgentEditBuildResult BuildDuplicateTrack(JsonElement payload)
    {
        var id = ReadTrackId(payload);
        return AgentEditBuildResult.Ok(new DuplicateTrackCommand { TrackId = id }, $"Duplicate track {id}");
    }

    private static AgentEditBuildResult BuildRenameTrack(JsonElement payload)
    {
        var id = ReadTrackId(payload);
        return AgentEditBuildResult.Ok(
            new RenameTrackCommand { TrackId = id, NewName = AgentPayloadReader.ReadRequiredString(payload, "name") },
            $"Rename track {id}");
    }

    private static AgentEditBuildResult BuildReorderTrack(JsonElement payload)
    {
        var id = ReadTrackId(payload);
        return AgentEditBuildResult.Ok(
            new ReorderTrackCommand { TrackId = id, NewIndex = AgentPayloadReader.ReadInt(payload, "index", 0) },
            $"Reorder track {id}");
    }

    private static AgentEditBuildResult BuildTrackToggle(JsonElement payload, string property)
    {
        var id = ReadTrackId(payload);
        IEditCommand command = property switch
        {
            "mute" => new ToggleTrackMuteCommand { TrackId = id },
            "solo" => new ToggleTrackSoloCommand { TrackId = id },
            _ => new ToggleTrackLockCommand { TrackId = id },
        };
        return AgentEditBuildResult.Ok(command, $"Toggle track {property} on {id}");
    }

    private static AgentEditBuildResult BuildUpdateSequence(Sequence sequence, JsonElement payload)
    {
        var frameRate = AgentPayloadReader.TryGetProperty(payload, "frame_rate", out var fpsElement)
            ? ParseFrameRate(fpsElement, sequence.FrameRate)
            : sequence.FrameRate;
        var background = AgentPayloadReader.TryGetProperty(payload, "background", out var backgroundElement)
            ? JsonSerializer.Deserialize<CanvasBackground>(backgroundElement.GetRawText(), AgentPayloadReader.JsonOptions) ?? sequence.Background
            : sequence.Background;
        var guides = AgentPayloadReader.TryGetProperty(payload, "layout_guides", out var guidesElement)
            ? JsonSerializer.Deserialize<List<LayoutGuide>>(guidesElement.GetRawText(), AgentPayloadReader.JsonOptions) ?? sequence.LayoutGuides
            : sequence.LayoutGuides;
        return AgentEditBuildResult.Ok(
            new UpdateSequenceSettingsCommand
            {
                SequenceId = sequence.Id,
                Width = AgentPayloadReader.ReadInt(payload, "width", sequence.Width),
                Height = AgentPayloadReader.ReadInt(payload, "height", sequence.Height),
                FrameRate = frameRate,
                Background = background,
                LayoutGuides = guides,
            },
            "Update sequence canvas settings");
    }

    private static AgentEditBuildResult BuildCaptionsFromTranscript(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var analysis = ResolveAnalysis(project, payload);
        if (analysis == null) return AgentEditBuildResult.Fail("No imported media intelligence analysis was found for the requested asset");
        var (track, addTrackCommand) = ResolveOrPrepareTrack(sequence, payload, TrackKind.Text);
        if (track == null) return AgentEditBuildResult.Fail("The requested text track is missing or locked");
        var sourceStart = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "source_start", 0));
        var sourceEnd = AgentPayloadReader.ReadSeconds(payload, "source_end", analysis.Metadata.Duration.Seconds);
        var timelineStart = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "timeline_start", playheadSeconds));
        var wordsPerChunk = Math.Clamp(AgentPayloadReader.ReadInt(payload, "words_per_chunk", project.TranscriptEditPolicy.CaptionWordsPerChunk), 1, 12);
        var uppercase = AgentPayloadReader.ReadBool(payload, "uppercase", false);
        var words = analysis.Transcript
            .SelectMany(segment => segment.Words.Count > 0
                ? segment.Words
                : [new MediaIntelligenceWord { Start = segment.Start, End = segment.End, Text = segment.Text, Confidence = segment.Confidence }])
            .Where(word => word.End.Seconds > sourceStart && word.Start.Seconds < sourceEnd && !string.IsNullOrWhiteSpace(word.Text))
            .OrderBy(word => word.Start.Seconds)
            .ToArray();
        if (words.Length == 0) return AgentEditBuildResult.Fail("No transcript words exist in the requested range");

        var commands = new List<IEditCommand>();
        if (addTrackCommand != null) commands.Add(addTrackCommand);
        for (var index = 0; index < words.Length; index += wordsPerChunk)
        {
            var chunk = words.Skip(index).Take(wordsPerChunk).ToArray();
            var start = chunk[0].Start.Seconds;
            var end = chunk[^1].End.Seconds;
            var duration = Math.Clamp(
                end - start,
                project.TranscriptEditPolicy.CaptionMinimumDurationSeconds,
                project.TranscriptEditPolicy.CaptionMaximumDurationSeconds);
            var text = string.Join(" ", chunk.Select(word => word.Text.Trim()));
            if (uppercase) text = text.ToUpperInvariant();
            var captionPayload = AgentPayloadReader.WithOverrides(payload, new Dictionary<string, object?>
            {
                ["font_size"] = AgentPayloadReader.ReadSeconds(payload, "font_size", 54),
                ["font_bold"] = AgentPayloadReader.ReadBool(payload, "font_bold", true),
                ["outline_width"] = AgentPayloadReader.ReadSeconds(payload, "outline_width", 3),
            });
            var item = CreateTextItem(project, captionPayload, text, timelineStart + (start - sourceStart), duration);
            item.MediaIntelligenceSourceAssetId = analysis.MediaAssetId;
            commands.Add(new AddClipCommand { TrackId = track.Id, Item = item });
        }

        return AgentEditBuildResult.Ok(
            new CompositeEditCommand("Create transcript captions", commands),
            $"Create {words.Length / wordsPerChunk + (words.Length % wordsPerChunk == 0 ? 0 : 1)} transcript caption clips");
    }

    private static AgentEditBuildResult BuildClipFromTranscript(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var analysis = ResolveAnalysis(project, payload);
        if (analysis == null) return AgentEditBuildResult.Fail("Media analysis not found");
        var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == analysis.MediaAssetId);
        if (asset == null || asset.IsOffline || !File.Exists(asset.OriginalPath)) return AgentEditBuildResult.Fail("Analyzed source media is unavailable");
        var targetKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;
        var (track, addTrackCommand) = ResolveOrPrepareTrack(sequence, payload, targetKind);
        if (track == null) return AgentEditBuildResult.Fail("The requested compatible track is missing or locked");

        var (sourceStart, sourceEnd) = ResolveTranscriptRange(analysis, payload);
        var policy = project.TranscriptEditPolicy;
        sourceStart = Math.Max(0, sourceStart - policy.WordPaddingBeforeSeconds);
        sourceEnd = Math.Min(asset.Duration.Seconds, sourceEnd + policy.WordPaddingAfterSeconds);
        var duration = Math.Max(0.1, sourceEnd - sourceStart);
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            MediaIntelligenceSourceAssetId = asset.Id,
            TimelineStart = MediaTime.FromSeconds(Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "timeline_start", playheadSeconds))),
            SourceStart = MediaTime.FromSeconds(sourceStart),
            SourceDuration = MediaTime.FromSeconds(duration),
            Duration = MediaTime.FromSeconds(duration),
            FadeInDuration = MediaTime.FromSeconds(Math.Min(policy.GeneratedCutFadeSeconds, duration / 2)),
            FadeOutDuration = MediaTime.FromSeconds(Math.Min(policy.GeneratedCutFadeSeconds, duration / 2)),
        };
        var command = addTrackCommand == null
            ? (IEditCommand)new AddClipCommand { TrackId = track.Id, Item = item }
            : new CompositeEditCommand("Add track and transcript clip", [addTrackCommand, new AddClipCommand { TrackId = track.Id, Item = item }]);
        return AgentEditBuildResult.Ok(command, $"Create transcript clip {sourceStart:0.##}-{sourceEnd:0.##}s");
    }

    private static AgentEditBuildResult BuildBestMoments(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var analysis = ResolveAnalysis(project, payload);
        if (analysis == null) return AgentEditBuildResult.Fail("Media analysis not found");
        var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == analysis.MediaAssetId);
        if (asset == null || asset.IsOffline || !File.Exists(asset.OriginalPath)) return AgentEditBuildResult.Fail("Analyzed source media is unavailable");
        var targetKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;
        var (track, addTrackCommand) = ResolveOrPrepareTrack(sequence, payload, targetKind);
        if (track == null) return AgentEditBuildResult.Fail("The requested compatible track is missing or locked");
        var count = Math.Clamp(AgentPayloadReader.ReadInt(payload, "count", 5), 1, 50);
        var maximumDuration = Math.Max(0.1, AgentPayloadReader.ReadSeconds(payload, "maximum_duration", 30));
        var role = AgentPayloadReader.ReadString(payload, "role");
        var candidates = analysis.Moments
            .Where(moment => string.IsNullOrWhiteSpace(role) || moment.EditingRoles.Any(value => value.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(moment => moment.Scores.Overall)
            .ThenByDescending(moment => moment.Confidence)
            .Take(count * 3)
            .ToArray();
        if (candidates.Length == 0) return AgentEditBuildResult.Fail("No editing moments match the request");

        var commands = new List<IEditCommand>();
        if (addTrackCommand != null) commands.Add(addTrackCommand);
        var cursor = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "timeline_start", playheadSeconds));
        var total = 0.0;
        foreach (var moment in candidates)
        {
            var duration = moment.End.Seconds - moment.Start.Seconds;
            if (duration <= 0.05 || total + duration > maximumDuration + 0.001) continue;
            var item = new TimelineItem
            {
                Kind = ItemKind.Clip,
                MediaAssetId = asset.Id,
                MediaIntelligenceSourceAssetId = asset.Id,
                TimelineStart = MediaTime.FromSeconds(cursor),
                SourceStart = moment.Start,
                SourceDuration = MediaTime.FromSeconds(duration),
                Duration = MediaTime.FromSeconds(duration),
                FadeInDuration = MediaTime.FromSeconds(Math.Min(project.TranscriptEditPolicy.GeneratedCutFadeSeconds, duration / 2)),
                FadeOutDuration = MediaTime.FromSeconds(Math.Min(project.TranscriptEditPolicy.GeneratedCutFadeSeconds, duration / 2)),
            };
            commands.Add(new AddClipCommand { TrackId = track.Id, Item = item });
            cursor += duration;
            total += duration;
            if (commands.Count - (addTrackCommand == null ? 0 : 1) >= count) break;
        }
        var clipCount = commands.Count - (addTrackCommand == null ? 0 : 1);
        return clipCount == 0
            ? AgentEditBuildResult.Fail("No moments fit the requested duration")
            : AgentEditBuildResult.Ok(new CompositeEditCommand("Assemble best moments", commands), $"Assemble {clipCount} moments ({total:0.##}s)");
    }

    private static AgentEditBuildResult BuildBestTake(Project project, Sequence sequence, JsonElement payload, double playheadSeconds)
    {
        var analysis = ResolveAnalysis(project, payload);
        if (analysis == null) return AgentEditBuildResult.Fail("Media analysis not found");
        var groupId = AgentPayloadReader.ReadRequiredString(payload, "group_id");
        var group = analysis.DuplicateTakeGroups.FirstOrDefault(candidate => candidate.GroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase));
        if (group == null) return AgentEditBuildResult.Fail("Duplicate-take group not found");
        var candidate = group.Candidates
            .Where(value => value.Recommended)
            .OrderByDescending(value => value.Score)
            .FirstOrDefault()
            ?? group.Candidates.OrderByDescending(value => value.Score).FirstOrDefault();
        if (candidate == null) return AgentEditBuildResult.Fail("Duplicate-take group has no candidates");
        var moment = analysis.Moments.FirstOrDefault(value => value.MomentId == candidate.MomentId);
        if (moment == null) return AgentEditBuildResult.Fail("Recommended take moment is unavailable");
        var augmented = AgentPayloadReader.WithOverrides(payload, new Dictionary<string, object?>
        {
            ["source_start"] = moment.Start.Seconds,
            ["source_end"] = moment.End.Seconds,
            ["timeline_start"] = AgentPayloadReader.ReadSeconds(payload, "timeline_start", playheadSeconds),
        });
        return BuildClipFromTranscript(project, sequence, augmented, playheadSeconds) with
        {
            Summary = $"Use best take from {groupId} (score {candidate.Score:0.###})",
        };
    }

    private static AgentEditBuildResult BuildRemoveSilence(Project project, Sequence sequence, JsonElement payload)
    {
        var itemId = ReadItemId(payload);
        var (track, item) = FindItem(sequence, itemId);
        if (track == null || item == null) return AgentEditBuildResult.Fail("Clip not found");
        if (!item.MediaAssetId.HasValue) return AgentEditBuildResult.Fail("Clip has no source media");
        if (item.SpeedCurve is { ConstantSpeed: not 1 } || Math.Abs(item.Speed - 1) > 0.0001 || item.Reversed)
            return AgentEditBuildResult.Fail("Silence removal currently requires forward 1x playback");
        var analysis = project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == item.MediaAssetId.Value);
        if (analysis == null) return AgentEditBuildResult.Fail("No media analysis exists for this clip");
        var threshold = Math.Max(0.05, AgentPayloadReader.ReadSeconds(payload, "minimum_silence", project.TranscriptEditPolicy.MinimumSilenceCutSeconds));
        var clipSourceStart = item.SourceStart.Seconds;
        var clipSourceEnd = clipSourceStart + item.SourceDuration.Seconds;
        var silences = analysis.Audio.Silence
            .Where(range => range.Duration.Seconds >= threshold && range.End.Seconds > clipSourceStart && range.Start.Seconds < clipSourceEnd)
            .Select(range => (Start: Math.Max(range.Start.Seconds, clipSourceStart), End: Math.Min(range.End.Seconds, clipSourceEnd)))
            .Where(range => range.End - range.Start >= threshold)
            .OrderBy(range => range.Start)
            .ToArray();
        if (silences.Length == 0) return AgentEditBuildResult.Fail("No removable silence ranges were found in this clip");

        var retained = new List<(double Start, double End)>();
        var cursor = clipSourceStart;
        foreach (var silence in silences)
        {
            if (silence.Start > cursor + 0.04) retained.Add((cursor, silence.Start));
            cursor = Math.Max(cursor, silence.End);
        }
        if (cursor < clipSourceEnd - 0.04) retained.Add((cursor, clipSourceEnd));
        if (retained.Count == 0) return AgentEditBuildResult.Fail("Silence removal would delete the entire clip");

        var replacementItems = track.Items
            .Where(candidate => candidate.Id != itemId)
            .Select(candidate => TimelineItemCloner.Clone(candidate, preserveId: true))
            .ToList();
        var outputCursor = item.TimelineStart.Seconds;
        foreach (var range in retained)
        {
            var duration = range.End - range.Start;
            var clone = TimelineItemCloner.Clone(item, MediaTime.FromSeconds(outputCursor));
            clone.SourceStart = MediaTime.FromSeconds(range.Start);
            clone.SourceDuration = MediaTime.FromSeconds(duration);
            clone.Duration = MediaTime.FromSeconds(duration);
            clone.FadeInDuration = MediaTime.FromSeconds(Math.Min(project.TranscriptEditPolicy.GeneratedCutFadeSeconds, duration / 2));
            clone.FadeOutDuration = MediaTime.FromSeconds(Math.Min(project.TranscriptEditPolicy.GeneratedCutFadeSeconds, duration / 2));
            clone.Locked = false;
            replacementItems.Add(clone);
            outputCursor += duration;
        }
        var removedDuration = item.Duration.Seconds - retained.Sum(range => range.End - range.Start);
        var ripple = AgentPayloadReader.ReadBool(payload, "ripple", true);
        if (ripple && removedDuration > 0)
        {
            foreach (var later in replacementItems.Where(candidate => candidate.TimelineStart.Seconds >= item.TimelineEnd.Seconds - 0.0001))
                later.TimelineStart = MediaTime.FromSeconds(Math.Max(0, later.TimelineStart.Seconds - removedDuration));
        }
        replacementItems.Sort((left, right) => left.TimelineStart.CompareTo(right.TimelineStart));
        return AgentEditBuildResult.Ok(
            new ReplaceTrackItemsCommand
            {
                TrackId = track.Id,
                NewItems = replacementItems,
                Description = "Remove analyzed silence",
            },
            $"Remove {silences.Length} silence ranges ({removedDuration:0.##}s)");
    }

    private static (double Start, double End) ResolveTranscriptRange(MediaIntelligenceAnalysis analysis, JsonElement payload)
    {
        if (AgentPayloadReader.ReadString(payload, "segment_id") is { Length: > 0 } segmentId)
        {
            var segment = analysis.Transcript.FirstOrDefault(value => value.SegmentId.Equals(segmentId, StringComparison.OrdinalIgnoreCase));
            if (segment == null) throw new InvalidOperationException("Transcript segment not found");
            return (segment.Start.Seconds, segment.End.Seconds);
        }
        var start = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "source_start", 0));
        var end = AgentPayloadReader.ReadSeconds(payload, "source_end", analysis.Metadata.Duration.Seconds);
        if (end <= start) throw new InvalidOperationException("source_end must be greater than source_start");
        return (start, end);
    }

    private static MediaIntelligenceAnalysis? ResolveAnalysis(Project project, JsonElement payload)
    {
        if (AgentPayloadReader.ReadString(payload, "media_asset_id") is { Length: > 0 } assetText)
        {
            var assetId = AgentPayloadReader.ParseMediaAssetId(assetText);
            return project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == assetId);
        }
        return project.MediaIntelligence.Count == 1 ? project.MediaIntelligence[0] : null;
    }

    private static FrameRate ParseFrameRate(JsonElement element, FrameRate fallback)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var fps)) return FrameRate.FromDouble(fps);
        if (element.ValueKind == JsonValueKind.Object)
        {
            var numerator = AgentPayloadReader.ReadInt(element, "numerator", fallback.Numerator);
            var denominator = AgentPayloadReader.ReadInt(element, "denominator", fallback.Denominator);
            return new FrameRate(numerator, denominator);
        }
        return fallback;
    }

    private static void AddDoubleProperty(
        ICollection<IEditCommand> commands,
        JsonElement payload,
        string jsonName,
        TimelineItemId itemId,
        string propertyName,
        Func<TimelineItem, double> getter,
        Action<TimelineItem, double> setter)
    {
        if (!AgentPayloadReader.HasProperty(payload, jsonName)) return;
        var value = AgentPayloadReader.ReadSeconds(payload, jsonName, 0);
        commands.Add(SetValue(itemId, propertyName, value, item => getter(item), (item, raw) => setter(item, (double)raw!)));
    }

    private static void AddBoolProperty(
        ICollection<IEditCommand> commands,
        JsonElement payload,
        string jsonName,
        TimelineItemId itemId,
        string propertyName,
        Func<TimelineItem, bool> getter,
        Action<TimelineItem, bool> setter)
    {
        if (!AgentPayloadReader.HasProperty(payload, jsonName)) return;
        var value = AgentPayloadReader.ReadBool(payload, jsonName, false);
        commands.Add(SetValue(itemId, propertyName, value, item => getter(item), (item, raw) => setter(item, (bool)raw!)));
    }

    private static SetPropertyCommand SetValue(
        TimelineItemId itemId,
        string propertyName,
        object? newValue,
        Func<TimelineItem, object?> getter,
        Action<TimelineItem, object?> setter) => new()
    {
        ItemId = itemId,
        PropertyName = propertyName,
        NewValue = newValue,
        Getter = getter,
        Setter = setter,
    };

    private static (Track? Track, IEditCommand? AddTrackCommand) ResolveOrPrepareTrack(
        Sequence sequence,
        JsonElement payload,
        TrackKind fallbackKind)
    {
        var requestedTrackId = AgentPayloadReader.ReadString(payload, "track_id");
        if (!string.IsNullOrWhiteSpace(requestedTrackId))
        {
            var requested = sequence.Tracks.FirstOrDefault(track => track.Id == AgentPayloadReader.ParseTrackId(requestedTrackId));
            return requested is { Locked: false } ? (requested, null) : (null, null);
        }

        var existing = sequence.Tracks.FirstOrDefault(track => track.Kind == fallbackKind && !track.Locked);
        if (existing != null) return (existing, null);
        var prepared = new Track
        {
            Kind = fallbackKind,
            Name = fallbackKind switch
            {
                TrackKind.Video => "V1",
                TrackKind.Audio => "A1",
                TrackKind.Music => "Music 1",
                TrackKind.Voice => "Voice 1",
                TrackKind.Text => "Captions",
                TrackKind.Overlay => "Overlay 1",
                _ => fallbackKind.ToString(),
            },
            Order = sequence.Tracks.Count,
        };
        return (prepared, new AddPreparedTrackCommand { Track = prepared });
    }

    private static Track? ResolveTrack(Sequence sequence, JsonElement payload, TrackKind fallbackKind)
    {
        var trackIdText = AgentPayloadReader.ReadString(payload, "track_id");
        if (!string.IsNullOrWhiteSpace(trackIdText))
        {
            var trackId = AgentPayloadReader.ParseTrackId(trackIdText);
            var requested = sequence.Tracks.FirstOrDefault(track => track.Id == trackId);
            return requested is { Locked: false } ? requested : null;
        }
        return sequence.Tracks.FirstOrDefault(track => track.Kind == fallbackKind && !track.Locked);
    }

    private static (Track? Track, TimelineItem? Item) FindItem(Sequence sequence, TimelineItemId itemId)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(candidate => candidate.Id == itemId);
            if (item != null) return (track, item);
        }
        return (null, null);
    }

    private static TimelineItemId ReadItemId(JsonElement payload) =>
        AgentPayloadReader.ParseTimelineItemId(AgentPayloadReader.ReadRequiredString(payload, "item_id"));

    private static TrackId ReadTrackId(JsonElement payload) =>
        AgentPayloadReader.ParseTrackId(AgentPayloadReader.ReadRequiredString(payload, "track_id"));

    private static string ResolveAgentFontFamily(Project project, string requested)
    {
        var fontFamily = string.IsNullOrWhiteSpace(requested) ? "Arial" : requested.Trim();
        if (!Path.IsPathFullyQualified(fontFamily) && !File.Exists(fontFamily)) return fontFamily;

        var fullPath = Path.GetFullPath(fontFamily);
        var registered = project.MediaLibrary.FirstOrDefault(asset =>
            asset.Kind == MediaKind.Font
            && !asset.IsOffline
            && File.Exists(asset.OriginalPath)
            && string.Equals(Path.GetFullPath(asset.OriginalPath), fullPath, StringComparison.OrdinalIgnoreCase));
        return registered?.OriginalPath
            ?? throw new InvalidOperationException("Font paths must reference a registered local project font asset.");
    }

    private static List<T> Clone<T>(IEnumerable<T> value) =>
        JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, AgentPayloadReader.JsonOptions), AgentPayloadReader.JsonOptions) ?? [];

    private static T? Clone<T>(T? value)
    {
        if (value == null) return default;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AgentPayloadReader.JsonOptions), AgentPayloadReader.JsonOptions);
    }
}

internal static class AgentPayloadReader
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool HasProperty(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out _);

    public static bool TryGetProperty(JsonElement payload, string name, out JsonElement value)
    {
        value = default;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out value);
    }

    public static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    public static string ReadRequiredString(JsonElement payload, string name) =>
        ReadString(payload, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing {name}");

    public static double ReadSeconds(JsonElement payload, string name, double fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        var parsed = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var textNumber)
                ? textNumber
                : fallback;
        if (!double.IsFinite(parsed)) throw new InvalidOperationException($"{name} must be a finite number");
        return parsed;
    }

    public static double? ReadNullableDouble(JsonElement payload, string name) =>
        HasProperty(payload, name) ? ReadSeconds(payload, name, 0) : null;

    public static int ReadInt(JsonElement payload, string name, int fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    public static int? ReadNullableInt(JsonElement payload, string name) => HasProperty(payload, name) ? ReadInt(payload, name, 0) : null;

    public static long? ReadLong(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    public static bool ReadBool(JsonElement payload, string name, bool fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback,
        };
    }

    public static bool? ReadNullableBool(JsonElement payload, string name) => HasProperty(payload, name) ? ReadBool(payload, name, false) : null;

    public static Dictionary<string, object> ReadObject(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return [];
        return JsonSerializer.Deserialize<Dictionary<string, object>>(value.GetRawText(), JsonOptions) ?? [];
    }

    public static JsonElement WithOverrides(JsonElement payload, IReadOnlyDictionary<string, object?> overrides)
    {
        var values = payload.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(payload.GetRawText(), JsonOptions) ?? []
            : [];
        foreach (var pair in overrides) values[pair.Key] = pair.Value;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values, JsonOptions));
        return document.RootElement.Clone();
    }

    public static TimelineItemId ParseTimelineItemId(string value) => new(Guid.Parse(value));
    public static TrackId ParseTrackId(string value) => new(Guid.Parse(value));
    public static MediaAssetId ParseMediaAssetId(string value) => new(Guid.Parse(value));
    public static EffectInstanceId ParseEffectInstanceId(string value) => new(Guid.Parse(value));
    public static MarkerId ParseMarkerId(string value) => new(Guid.Parse(value));
}
