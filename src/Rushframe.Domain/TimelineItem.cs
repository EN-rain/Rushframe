namespace Rushframe.Domain;

public enum ItemKind { Clip, Text, Image, Sticker, AdjustmentLayer }

public sealed class TimelineItem
{
    private static long _globalTimingMutationVersion;
    private Dictionary<string, AnimationChannel>? _animationChannelsByName;
    private MediaTime _timelineStart;
    private MediaTime _duration;

    public TimelineItemId Id { get; init; } = TimelineItemId.New();
    public ItemKind Kind { get; init; }
    public MediaAssetId? MediaAssetId { get; init; }
    public MediaTime TimelineStart
    {
        get => _timelineStart;
        set
        {
            if (_timelineStart == value) return;
            _timelineStart = value;
            Interlocked.Increment(ref _globalTimingMutationVersion);
        }
    }
    public MediaTime Duration
    {
        get => _duration;
        set
        {
            if (_duration == value) return;
            _duration = value;
            Interlocked.Increment(ref _globalTimingMutationVersion);
        }
    }
    public MediaTime SourceStart { get; set; }
    public MediaTime SourceDuration { get; set; }
    public double Speed { get; set; } = 1.0;
    public bool Reversed { get; set; }
    public double Volume { get; set; } = 1.0;
    public bool Muted { get; set; }
    public Transform2D Transform { get; init; } = new();
    public double Opacity { get; set; } = 1.0;
    public bool Locked { get; set; }

    public string? TextContent { get; set; }
    public string? StickerId { get; set; }
    public string? GraphicDefinitionId { get; set; }
    public string? FontFamily { get; set; }
    public double FontSize { get; set; } = 48;
    public bool FontBold { get; set; }
    public string FontAlign { get; set; } = "center";
    public string? FillColor { get; set; }
    public string? OutlineColor { get; set; }
    public double OutlineWidth { get; set; }
    public string? ShadowColor { get; set; }
    public double ShadowOffsetX { get; set; } = 2;
    public double ShadowOffsetY { get; set; } = 2;
    public double ShadowBlur { get; set; } = 4;
    public double ShadowOpacity { get; set; } = 0.5;

    public MediaTime FadeInDuration { get; set; }
    public MediaTime FadeOutDuration { get; set; }
    public double Pan { get; set; }
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }

    public BlendMode BlendMode { get; set; }
    public List<Mask> Masks { get; init; } = [];
    public ColorCorrection? ColorCorrection { get; set; }
    public SpeedCurve? SpeedCurve { get; set; }
    public StabilizationSettings? Stabilization { get; set; }
    public List<EffectInstance> Effects { get; init; } = [];
    /// <summary>Legacy single animation channel. New projects should use AnimationChannels.</summary>
    public AnimatedProperty? AnimatedProperty { get; set; }
    public List<AnimationChannel> AnimationChannels { get; init; } = [];
    public ChromaKey? ChromaKey { get; set; }
    public MediaAssetId? MediaIntelligenceSourceAssetId { get; set; }

    internal static long GlobalTimingMutationVersion => Interlocked.Read(ref _globalTimingMutationVersion);

    public MediaTime SourceEnd => MediaTime.FromSeconds(SourceStart.Seconds + (SourceDuration.Seconds / Speed));
    public MediaTime TimelineEnd => TimelineStart.Add(Duration);

    public AnimationChannel? GetAnimationChannel(string propertyName)
    {
        _animationChannelsByName ??= BuildAnimationChannelLookup();
        return _animationChannelsByName.TryGetValue(propertyName, out var channel) ? channel : null;
    }

    public void InvalidateAnimationChannelCache()
    {
        _animationChannelsByName = null;
        foreach (var channel in AnimationChannels) channel.InvalidateLookupCache();
        AnimatedProperty?.InvalidateLookupCache();
    }

    public double GetAnimatedValue(string propertyName, MediaTime localTime, double fallback) =>
        GetAnimationChannel(propertyName)?.GetValueAt(localTime) ?? fallback;

    private Dictionary<string, AnimationChannel> BuildAnimationChannelLookup()
    {
        var lookup = new Dictionary<string, AnimationChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in AnimationChannels)
        {
            channel.NormalizeKeyframes();
            lookup[channel.PropertyName] = channel;
        }
        if (AnimatedProperty != null)
        {
            AnimatedProperty.NormalizeKeyframes();
            lookup.TryAdd(AnimatedProperty.PropertyName, AnimatedProperty);
        }
        return lookup;
    }
}

public sealed class Transform2D
{
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double RotationDegrees { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
}

public sealed class ChromaKey
{
    public string? Color { get; set; }
    public double Similarity { get; set; } = 0.1;
    public double Intensity { get; set; } = 0.1;
    public double EdgeSoftness { get; set; } = 0.05;
    public double SpillSuppression { get; set; } = 0.1;
    public bool ShadowSuppression { get; set; }
}
