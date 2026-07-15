using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Editing;

/// <summary>
/// In-memory safety snapshot used only to restore a sequence when an edit command
/// reports failure or throws after changing state. Successful edits keep the
/// original object graph. Failure restoration reuses the original track, item,
/// and marker instances by stable ID so UI-held references do not become stale.
/// </summary>
internal sealed class SequenceStateSnapshot
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new MediaTimeConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    private readonly string _json;
    private readonly Sequence _snapshot;
    private readonly IReadOnlyDictionary<TrackId, Track> _trackReferences;
    private readonly IReadOnlyDictionary<TimelineItemId, TimelineItem> _itemReferences;
    private readonly IReadOnlyDictionary<MarkerId, Marker> _markerReferences;

    private SequenceStateSnapshot(
        string json,
        Sequence snapshot,
        IReadOnlyDictionary<TrackId, Track> trackReferences,
        IReadOnlyDictionary<TimelineItemId, TimelineItem> itemReferences,
        IReadOnlyDictionary<MarkerId, Marker> markerReferences)
    {
        _json = json;
        _snapshot = snapshot;
        _trackReferences = trackReferences;
        _itemReferences = itemReferences;
        _markerReferences = markerReferences;
    }

    public static SequenceStateSnapshot Capture(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var trackReferences = sequence.Tracks.ToDictionary(track => track.Id);
        var itemReferences = sequence.Tracks
            .SelectMany(track => track.Items)
            .ToDictionary(item => item.Id);
        var markerReferences = sequence.Markers.ToDictionary(marker => marker.Id);
        var json = JsonSerializer.Serialize(sequence, Options);
        var snapshot = JsonSerializer.Deserialize<Sequence>(json, Options)
                       ?? throw new InvalidOperationException("Could not capture sequence state.");
        return new SequenceStateSnapshot(
            json,
            snapshot,
            trackReferences,
            itemReferences,
            markerReferences);
    }

    public bool Matches(Sequence sequence) =>
        string.Equals(_json, JsonSerializer.Serialize(sequence, Options), StringComparison.Ordinal);

    public void Restore(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        sequence.Name = _snapshot.Name;
        sequence.Width = _snapshot.Width;
        sequence.Height = _snapshot.Height;
        sequence.FrameRate = _snapshot.FrameRate;
        sequence.Background = _snapshot.Background;

        sequence.LayoutGuides.Clear();
        sequence.LayoutGuides.AddRange(_snapshot.LayoutGuides);

        var restoredTracks = new List<Track>(_snapshot.Tracks.Count);
        foreach (var sourceTrack in _snapshot.Tracks)
        {
            var track = _trackReferences.TryGetValue(sourceTrack.Id, out var originalTrack)
                && originalTrack.Kind == sourceTrack.Kind
                    ? originalTrack
                    : sourceTrack;
            track.Name = sourceTrack.Name;
            track.Order = sourceTrack.Order;
            track.Muted = sourceTrack.Muted;
            track.Solo = sourceTrack.Solo;
            track.Locked = sourceTrack.Locked;
            track.Hidden = sourceTrack.Hidden;

            var restoredItems = new List<TimelineItem>(sourceTrack.Items.Count);
            foreach (var sourceItem in sourceTrack.Items)
            {
                if (_itemReferences.TryGetValue(sourceItem.Id, out var originalItem)
                    && originalItem.Kind == sourceItem.Kind
                    && originalItem.MediaAssetId == sourceItem.MediaAssetId)
                {
                    CopyItemState(sourceItem, originalItem);
                    restoredItems.Add(originalItem);
                }
                else
                {
                    restoredItems.Add(sourceItem);
                }
            }
            track.Items.Clear();
            track.Items.AddRange(restoredItems);
            restoredTracks.Add(track);
        }
        sequence.Tracks.Clear();
        sequence.Tracks.AddRange(restoredTracks);

        var restoredMarkers = new List<Marker>(_snapshot.Markers.Count);
        foreach (var sourceMarker in _snapshot.Markers)
        {
            if (_markerReferences.TryGetValue(sourceMarker.Id, out var originalMarker))
            {
                originalMarker.Label = sourceMarker.Label;
                originalMarker.Time = sourceMarker.Time;
                originalMarker.Note = sourceMarker.Note;
                originalMarker.Color = sourceMarker.Color;
                originalMarker.Duration = sourceMarker.Duration;
                originalMarker.DurationInFrames = sourceMarker.DurationInFrames;
                originalMarker.MediaIntelligenceSourceAssetId = sourceMarker.MediaIntelligenceSourceAssetId;
                restoredMarkers.Add(originalMarker);
            }
            else
            {
                restoredMarkers.Add(sourceMarker);
            }
        }
        sequence.Markers.Clear();
        sequence.Markers.AddRange(restoredMarkers);

        sequence.Transitions.Clear();
        sequence.Transitions.AddRange(_snapshot.Transitions);
    }

    private static void CopyItemState(TimelineItem source, TimelineItem target)
    {
        target.TimelineStart = source.TimelineStart;
        target.Duration = source.Duration;
        target.SourceStart = source.SourceStart;
        target.SourceDuration = source.SourceDuration;
        target.Speed = source.Speed;
        target.Reversed = source.Reversed;
        target.Volume = source.Volume;
        target.Muted = source.Muted;
        target.Transform.PositionX = source.Transform.PositionX;
        target.Transform.PositionY = source.Transform.PositionY;
        target.Transform.ScaleX = source.Transform.ScaleX;
        target.Transform.ScaleY = source.Transform.ScaleY;
        target.Transform.RotationDegrees = source.Transform.RotationDegrees;
        target.Transform.AnchorX = source.Transform.AnchorX;
        target.Transform.AnchorY = source.Transform.AnchorY;
        target.Opacity = source.Opacity;
        target.Locked = source.Locked;
        target.TextContent = source.TextContent;
        target.StickerId = source.StickerId;
        target.GraphicDefinitionId = source.GraphicDefinitionId;
        target.FontFamily = source.FontFamily;
        target.FontSize = source.FontSize;
        target.FontBold = source.FontBold;
        target.FontAlign = source.FontAlign;
        target.FillColor = source.FillColor;
        target.OutlineColor = source.OutlineColor;
        target.OutlineWidth = source.OutlineWidth;
        target.ShadowColor = source.ShadowColor;
        target.ShadowOffsetX = source.ShadowOffsetX;
        target.ShadowOffsetY = source.ShadowOffsetY;
        target.ShadowBlur = source.ShadowBlur;
        target.ShadowOpacity = source.ShadowOpacity;
        target.FadeInDuration = source.FadeInDuration;
        target.FadeOutDuration = source.FadeOutDuration;
        target.VisualTransitionIn = source.VisualTransitionIn;
        target.VisualTransitionInDuration = source.VisualTransitionInDuration;
        target.VisualTransitionOut = source.VisualTransitionOut;
        target.VisualTransitionOutDuration = source.VisualTransitionOutDuration;
        target.Pan = source.Pan;
        target.CropLeft = source.CropLeft;
        target.CropTop = source.CropTop;
        target.CropRight = source.CropRight;
        target.CropBottom = source.CropBottom;
        target.BlendMode = source.BlendMode;
        target.ColorCorrection = source.ColorCorrection;
        target.SpeedCurve = source.SpeedCurve;
        target.Stabilization = source.Stabilization;
        target.AnimatedProperty = source.AnimatedProperty;
        target.ChromaKey = source.ChromaKey;
        target.MediaIntelligenceSourceAssetId = source.MediaIntelligenceSourceAssetId;
        target.Masks.Clear();
        target.Masks.AddRange(source.Masks);
        target.Effects.Clear();
        target.Effects.AddRange(source.Effects);
        target.AnimationChannels.Clear();
        target.AnimationChannels.AddRange(source.AnimationChannels);
        target.InvalidateAnimationChannelCache();
    }
}
