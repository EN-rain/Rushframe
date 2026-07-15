using System.Globalization;
using Rushframe.Domain;
using Rushframe.Domain.Serialization;

namespace Rushframe.Desktop.Services;

internal static class VariantRenderContextService
{
    public static (Project Project, Sequence Sequence) Create(Project sourceProject, ExportVariant variant)
    {
        ArgumentNullException.ThrowIfNull(sourceProject);
        ArgumentNullException.ThrowIfNull(variant);

        var clone = ProjectSerializer.CreateSnapshot(sourceProject);
        var sequence = variant.SequenceId is { } sequenceId
            ? clone.Sequences.FirstOrDefault(candidate => candidate.Id == sequenceId)
            : clone.MainSequence;
        if (sequence == null) throw new InvalidOperationException("Variant sequence is unavailable");

        var sourceWidth = sequence.Width;
        var sourceHeight = sequence.Height;
        sequence.Width = variant.Width;
        sequence.Height = variant.Height;
        if (variant.FrameRate.HasValue) sequence.FrameRate = variant.FrameRate.Value;

        foreach (var trackOverride in variant.TrackOverrides)
        {
            var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == trackOverride.TrackId);
            if (track == null) continue;
            if (trackOverride.Hidden.HasValue) track.Hidden = trackOverride.Hidden.Value;
            if (trackOverride.Muted.HasValue) track.Muted = trackOverride.Muted.Value;
            if (trackOverride.Solo.HasValue) track.Solo = trackOverride.Solo.Value;
        }

        CenterPrimaryVideoForPortrait(
            sequence,
            sourceWidth,
            sourceHeight,
            variant.Width,
            variant.Height,
            variant.ItemOverrides
                .Where(value => value.PositionX.HasValue || value.PositionY.HasValue)
                .Select(value => value.ItemId)
                .ToHashSet(),
            ReadBool(variant, "autoCenterPortrait", fallback: true));

        foreach (var itemOverride in variant.ItemOverrides)
        {
            var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == itemOverride.ItemId));
            var item = track?.Items.FirstOrDefault(candidate => candidate.Id == itemOverride.ItemId);
            if (track == null || item == null) continue;
            if (itemOverride.Hidden)
            {
                track.Items.Remove(item);
                continue;
            }
            if (itemOverride.PositionX.HasValue) item.Transform.PositionX = itemOverride.PositionX.Value;
            if (itemOverride.PositionY.HasValue) item.Transform.PositionY = itemOverride.PositionY.Value;
            if (itemOverride.ScaleX.HasValue) item.Transform.ScaleX = Math.Max(0.001, itemOverride.ScaleX.Value);
            if (itemOverride.ScaleY.HasValue) item.Transform.ScaleY = Math.Max(0.001, itemOverride.ScaleY.Value);
            if (itemOverride.RotationDegrees.HasValue) item.Transform.RotationDegrees = itemOverride.RotationDegrees.Value;
            if (itemOverride.Opacity.HasValue) item.Opacity = Math.Clamp(itemOverride.Opacity.Value, 0, 1);
            if (itemOverride.Volume.HasValue) item.Volume = Math.Clamp(itemOverride.Volume.Value, 0, 4);
            if (itemOverride.Pan.HasValue) item.Pan = Math.Clamp(itemOverride.Pan.Value, -1, 1);
            if (itemOverride.FontSize.HasValue && item.Kind == ItemKind.Text) item.FontSize = Math.Clamp(itemOverride.FontSize.Value, 1, 1000);
            if (itemOverride.TextContent != null && item.Kind == ItemKind.Text) item.TextContent = itemOverride.TextContent;
        }

        if (TryReadDouble(variant, "captionScale", out var captionScale))
        {
            foreach (var textItem in sequence.Tracks.SelectMany(track => track.Items).Where(item => item.Kind == ItemKind.Text))
                textItem.FontSize = Math.Clamp(textItem.FontSize * captionScale, 1, 1000);
        }
        if (TryReadDouble(variant, "captionYOffset", out var captionOffset))
        {
            foreach (var textItem in sequence.Tracks.SelectMany(track => track.Items).Where(item => item.Kind == ItemKind.Text))
                textItem.Transform.PositionY += captionOffset;
        }
        if (variant.Overrides.TryGetValue("backgroundColor", out var backgroundColor)
            && !string.IsNullOrWhiteSpace(backgroundColor))
        {
            sequence.Background = new CanvasBackground
            {
                Kind = CanvasBackgroundKind.Solid,
                PrimaryColor = backgroundColor,
                SecondaryColor = backgroundColor,
            };
        }

        return (clone, sequence);
    }

    public static void CenterPrimaryVideoForPortrait(
        Sequence sequence,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        IReadOnlySet<TimelineItemId>? explicitlyPositioned = null,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var switchedToPortrait = outputHeight > outputWidth && sourceWidth >= sourceHeight;
        if (!switchedToPortrait || !enabled) return;
        explicitlyPositioned ??= new HashSet<TimelineItemId>();

        foreach (var item in sequence.Tracks
                     .Where(track => track.Kind == TrackKind.Video && !track.Hidden)
                     .SelectMany(track => track.Items)
                     .Where(item => item.Kind is ItemKind.Clip or ItemKind.Image))
        {
            if (explicitlyPositioned.Contains(item.Id)) continue;
            if (item.GetAnimationChannel(AnimationPropertyNames.PositionX) != null
                || item.GetAnimationChannel(AnimationPropertyNames.PositionY) != null)
                continue;

            item.Transform.PositionX = 0;
            item.Transform.PositionY = 0;
        }
    }

    private static bool TryReadDouble(ExportVariant variant, string key, out double value)
    {
        value = default;
        return variant.Overrides.TryGetValue(key, out var text)
               && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool ReadBool(ExportVariant variant, string key, bool fallback) =>
        variant.Overrides.TryGetValue(key, out var text) && bool.TryParse(text, out var value)
            ? value
            : fallback;
}
