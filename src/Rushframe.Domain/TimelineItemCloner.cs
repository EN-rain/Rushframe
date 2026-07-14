using System.Text.Json;

namespace Rushframe.Domain;

public static class TimelineItemCloner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static TimelineItem Clone(TimelineItem source, MediaTime? timelineStart = null, bool preserveId = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clone = new TimelineItem
        {
            Id = preserveId ? source.Id : TimelineItemId.New(),
            Kind = source.Kind,
            MediaAssetId = source.MediaAssetId,
            TimelineStart = timelineStart ?? source.TimelineStart,
            Duration = source.Duration,
            SourceStart = source.SourceStart,
            SourceDuration = source.SourceDuration,
            Speed = source.Speed,
            Reversed = source.Reversed,
            Volume = source.Volume,
            Muted = source.Muted,
            Transform = CloneValue(source.Transform) ?? new Transform2D(),
            Opacity = source.Opacity,
            Locked = source.Locked,
            TextContent = source.TextContent,
            StickerId = source.StickerId,
            GraphicDefinitionId = source.GraphicDefinitionId,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontBold = source.FontBold,
            FontAlign = source.FontAlign,
            FillColor = source.FillColor,
            OutlineColor = source.OutlineColor,
            OutlineWidth = source.OutlineWidth,
            ShadowColor = source.ShadowColor,
            ShadowOffsetX = source.ShadowOffsetX,
            ShadowOffsetY = source.ShadowOffsetY,
            ShadowBlur = source.ShadowBlur,
            ShadowOpacity = source.ShadowOpacity,
            FadeInDuration = source.FadeInDuration,
            FadeOutDuration = source.FadeOutDuration,
            Pan = source.Pan,
            CropLeft = source.CropLeft,
            CropTop = source.CropTop,
            CropRight = source.CropRight,
            CropBottom = source.CropBottom,
            BlendMode = source.BlendMode,
            ColorCorrection = CloneValue(source.ColorCorrection),
            SpeedCurve = CloneValue(source.SpeedCurve),
            Stabilization = CloneValue(source.Stabilization),
            AnimatedProperty = CloneValue(source.AnimatedProperty),
            ChromaKey = CloneValue(source.ChromaKey),
            MediaIntelligenceSourceAssetId = source.MediaIntelligenceSourceAssetId,
        };

        clone.Masks.AddRange(CloneValue(source.Masks) ?? []);
        clone.Effects.AddRange(CloneValue(source.Effects) ?? []);
        clone.AnimationChannels.AddRange(CloneValue(source.AnimationChannels) ?? []);
        return clone;
    }

    private static T? CloneValue<T>(T? value)
    {
        if (value is null) return default;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions);
    }
}
