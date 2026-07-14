namespace Rushframe.Domain.Editing;

public sealed class ApplyMediaIntelligenceCommand : IEditCommand
{
    public string Description => "Apply media intelligence to timeline";

    public required TimelineItemId TargetItemId { get; init; }
    public required MediaIntelligenceAnalysis Analysis { get; init; }
    public bool AddSceneMarkers { get; init; } = true;
    public bool AddCaptionClips { get; init; } = true;
    public int CreatedMarkerCount => _createdMarkers.Count;
    public int CreatedCaptionCount => _createdItems.Count;

    private readonly List<Marker> _removedMarkers = [];
    private readonly List<(Track Track, TimelineItem Item)> _removedItems = [];
    private readonly List<Marker> _createdMarkers = [];
    private readonly List<TimelineItem> _createdItems = [];
    private Track? _createdTrack;

    public EditResult Execute(Sequence sequence)
    {
        var targetTrack = sequence.Tracks.FirstOrDefault(track => track.Items.Any(item => item.Id == TargetItemId));
        var target = targetTrack?.Items.FirstOrDefault(item => item.Id == TargetItemId);
        if (target == null)
            return EditResult.Fail("Target timeline item was not found");
        if (targetTrack!.Locked)
            return EditResult.Fail("Track is locked");
        if (target.Locked)
            return EditResult.Fail("Item is locked");
        if (sequence.Tracks.Any(track => track.Locked && track.Items.Any(item =>
                item.MediaIntelligenceSourceAssetId == Analysis.MediaAssetId)))
            return EditResult.Fail("Generated media-intelligence content is on a locked track");
        if (sequence.Tracks.SelectMany(track => track.Items).Any(item =>
                item.Locked && item.MediaIntelligenceSourceAssetId == Analysis.MediaAssetId))
            return EditResult.Fail("Generated media-intelligence content is locked");
        if (target.MediaAssetId != Analysis.MediaAssetId)
            return EditResult.Fail("Analysis does not belong to the selected timeline item");

        var existingCaptionTrack = AddCaptionClips
            ? sequence.Tracks.FirstOrDefault(track =>
                track.Kind == TrackKind.Text && string.Equals(track.Name, "AI Captions", StringComparison.OrdinalIgnoreCase))
            : null;
        if (existingCaptionTrack?.Locked == true)
            return EditResult.Fail("Caption track is locked");

        RemovePreviousGeneratedContent(sequence);

        var speed = Math.Max(0.001, target.SpeedCurve?.ConstantSpeed ?? target.Speed);
        var sourceStart = target.SourceStart.Seconds;
        var sourceEnd = sourceStart + target.Duration.Seconds * speed;

        if (AddSceneMarkers)
        {
            var sceneNumber = 1;
            foreach (var scene in Analysis.Scenes.OrderBy(scene => scene.Start.Seconds))
            {
                if (scene.Start.Seconds < sourceStart || scene.Start.Seconds > sourceEnd)
                    continue;

                var timelineTime = target.TimelineStart.Seconds + ((scene.Start.Seconds - sourceStart) / speed);
                var label = string.IsNullOrWhiteSpace(scene.Description)
                    ? $"Scene {sceneNumber}"
                    : TrimLabel(scene.Description!, 80);
                var marker = new Marker
                {
                    Label = label,
                    Time = MediaTime.FromSeconds(timelineTime),
                    Color = "#7C5CFF",
                    MediaIntelligenceSourceAssetId = Analysis.MediaAssetId,
                };
                sequence.Markers.Add(marker);
                _createdMarkers.Add(marker);
                sceneNumber++;
            }
        }

        if (AddCaptionClips)
        {
            var captionTrack = existingCaptionTrack;
            if (captionTrack == null)
            {
                captionTrack = new Track
                {
                    Kind = TrackKind.Text,
                    Name = "AI Captions",
                    Order = sequence.Tracks.Count,
                };
                sequence.Tracks.Add(captionTrack);
                _createdTrack = captionTrack;
            }

            foreach (var segment in Analysis.Transcript.OrderBy(segment => segment.Start.Seconds))
            {
                var clippedStart = Math.Max(segment.Start.Seconds, sourceStart);
                var clippedEnd = Math.Min(segment.End.Seconds, sourceEnd);
                if (clippedEnd <= clippedStart || string.IsNullOrWhiteSpace(segment.Text))
                    continue;

                var item = new TimelineItem
                {
                    Kind = ItemKind.Text,
                    TimelineStart = MediaTime.FromSeconds(target.TimelineStart.Seconds + ((clippedStart - sourceStart) / speed)),
                    Duration = MediaTime.FromSeconds((clippedEnd - clippedStart) / speed),
                    SourceDuration = MediaTime.FromSeconds((clippedEnd - clippedStart) / speed),
                    TextContent = segment.Text.Trim(),
                    FontFamily = "Segoe UI",
                    FontSize = 42,
                    FontBold = true,
                    FontAlign = "center",
                    FillColor = "#FFFFFF",
                    OutlineColor = "#000000",
                    OutlineWidth = 2,
                    ShadowColor = "#000000",
                    ShadowOpacity = 0.65,
                    MediaIntelligenceSourceAssetId = Analysis.MediaAssetId,
                };
                captionTrack.Items.Add(item);
                _createdItems.Add(item);
            }
        }

        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var marker in _createdMarkers)
            sequence.Markers.Remove(marker);

        foreach (var item in _createdItems)
        {
            foreach (var track in sequence.Tracks)
                track.Items.Remove(item);
        }

        if (_createdTrack != null && _createdTrack.Items.Count == 0)
            sequence.Tracks.Remove(_createdTrack);

        foreach (var marker in _removedMarkers)
            sequence.Markers.Add(marker);
        foreach (var (track, item) in _removedItems)
        {
            if (!sequence.Tracks.Contains(track))
                sequence.Tracks.Add(track);
            track.Items.Add(item);
        }

        _createdMarkers.Clear();
        _createdItems.Clear();
        _removedMarkers.Clear();
        _removedItems.Clear();
        _createdTrack = null;
        return EditResult.Ok();
    }

    private void RemovePreviousGeneratedContent(Sequence sequence)
    {
        foreach (var marker in sequence.Markers
                     .Where(marker => marker.MediaIntelligenceSourceAssetId == Analysis.MediaAssetId)
                     .ToList())
        {
            _removedMarkers.Add(marker);
            sequence.Markers.Remove(marker);
        }

        foreach (var track in sequence.Tracks.ToList())
        {
            foreach (var item in track.Items
                         .Where(item => item.MediaIntelligenceSourceAssetId == Analysis.MediaAssetId)
                         .ToList())
            {
                _removedItems.Add((track, item));
                track.Items.Remove(item);
            }
        }
    }

    private static string TrimLabel(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
