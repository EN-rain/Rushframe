namespace Rushframe.Domain.Serialization;

internal static class ProjectInvariantNormalizer
{
    private const int MaximumCanvasDimension = 16_384;

    public static Project Normalize(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        RequireIdentity(project.Id.Value, "project");
        if (project.Revision < 0) project.Revision = 0;
        if (string.IsNullOrWhiteSpace(project.Name)) project.Name = "Untitled";
        if (project.Sequences.Count == 0) project.Sequences.Add(new Sequence());

        ValidateMediaLibrary(project);
        ValidateTaskIds(project);
        ValidateSequences(project);
        project.Workflow ??= new ProductionWorkflow();
        project.Workflow.EnsureDefaults();
        project.TranscriptEditPolicy ??= new TranscriptEditPolicy();
        project.EditingBrief ??= new EditingBrief();
        project.EditingBrief.Normalize();
        NormalizeMediaRelationships(project);
        return project;
    }

    private static void ValidateMediaLibrary(Project project)
    {
        var ids = new HashSet<MediaAssetId>();
        foreach (var asset in project.MediaLibrary)
        {
            RequireIdentity(asset.Id.Value, "media asset");
            if (!ids.Add(asset.Id)) throw new InvalidDataException($"Duplicate media asset ID {asset.Id}.");
            if (asset.Duration < MediaTime.Zero)
                throw new InvalidDataException($"Media asset {asset.Id} has a negative duration.");
            if (asset.PixelWidth < 0 || asset.PixelHeight < 0)
                throw new InvalidDataException($"Media asset {asset.Id} has invalid dimensions.");
        }
    }

    private static void ValidateTaskIds(Project project)
    {
        var ids = new HashSet<Guid>();
        foreach (var task in project.Tasks)
        {
            RequireIdentity(task.Id, "campaign task");
            if (!ids.Add(task.Id)) throw new InvalidDataException($"Duplicate campaign task ID {task.Id}.");
            if (string.IsNullOrWhiteSpace(task.Title)) task.Title = "Untitled task";
        }
    }

    private static void NormalizeMediaRelationships(Project project)
    {
        if (project.MediaRelationships.Count == 0 && project.MediaIntelligence.Count > 0)
            project.MediaRelationships.AddRange(MediaRelationshipBuilder.Build(project.MediaIntelligence));

        var validAssets = project.MediaIntelligence.Select(value => value.MediaAssetId).ToHashSet();
        project.MediaRelationships.RemoveAll(value =>
            !validAssets.Contains(value.Source.MediaAssetId)
            || !validAssets.Contains(value.Target.MediaAssetId)
            || string.IsNullOrWhiteSpace(value.Source.MomentId)
            || string.IsNullOrWhiteSpace(value.Target.MomentId));
        foreach (var relationship in project.MediaRelationships)
        {
            relationship.Score = Math.Clamp(double.IsFinite(relationship.Score) ? relationship.Score : 0, 0, 1);
            relationship.Reason = relationship.Reason?.Trim() ?? string.Empty;
        }
    }

    private static void ValidateSequences(Project project)
    {
        var sequenceIds = new HashSet<SequenceId>();
        var mediaIds = project.MediaLibrary.Select(asset => asset.Id).ToHashSet();
        foreach (var sequence in project.Sequences)
        {
            RequireIdentity(sequence.Id.Value, "sequence");
            if (!sequenceIds.Add(sequence.Id)) throw new InvalidDataException($"Duplicate sequence ID {sequence.Id}.");
            sequence.Name = string.IsNullOrWhiteSpace(sequence.Name) ? "Sequence" : sequence.Name.Trim();
            sequence.Width = Math.Clamp(sequence.Width, 2, MaximumCanvasDimension);
            sequence.Height = Math.Clamp(sequence.Height, 2, MaximumCanvasDimension);
            if (sequence.FrameRate.Numerator <= 0 || sequence.FrameRate.Denominator <= 0)
                sequence.FrameRate = FrameRate.Fps30;

            ValidateTracks(sequence, mediaIds);
            ValidateMarkers(sequence);
            ValidateTransitions(sequence);
        }
    }

    private static void ValidateTracks(Sequence sequence, HashSet<MediaAssetId> mediaIds)
    {
        var trackIds = new HashSet<TrackId>();
        var itemIds = new HashSet<TimelineItemId>();
        foreach (var track in sequence.Tracks)
        {
            RequireIdentity(track.Id.Value, "track");
            if (!trackIds.Add(track.Id)) throw new InvalidDataException($"Duplicate track ID {track.Id}.");
            if (string.IsNullOrWhiteSpace(track.Name)) track.Name = track.Kind.ToString();
            foreach (var item in track.Items)
            {
                RequireIdentity(item.Id.Value, "timeline item");
                if (!itemIds.Add(item.Id)) throw new InvalidDataException($"Duplicate timeline item ID {item.Id}.");
                if (!TrackCompatibility.IsItemCompatibleWithTrack(item.Kind, track.Kind))
                    throw new InvalidDataException($"{item.Kind} item {item.Id} is incompatible with {track.Kind} track {track.Id}.");
                if (item.MediaAssetId is { } mediaId && !mediaIds.Contains(mediaId))
                    throw new InvalidDataException($"Timeline item {item.Id} references missing media asset {mediaId}.");
                NormalizeItem(item, sequence.FrameRate);
            }
            track.Items.Sort(static (left, right) =>
            {
                var comparison = left.TimelineStart.CompareTo(right.TimelineStart);
                return comparison != 0 ? comparison : left.Id.Value.CompareTo(right.Id.Value);
            });
        }
        TrackOrdering.Normalize(sequence);
    }

    private static void NormalizeItem(TimelineItem item, FrameRate frameRate)
    {
        var minimumDuration = frameRate.FrameDuration;
        item.TimelineStart = Max(item.TimelineStart, MediaTime.Zero);
        item.Duration = item.Duration > MediaTime.Zero ? item.Duration : minimumDuration;
        item.SourceStart = Max(item.SourceStart, MediaTime.Zero);
        item.SourceDuration = item.SourceDuration > MediaTime.Zero
            ? item.SourceDuration
            : MediaTime.FromSeconds(Math.Max(item.Duration.Seconds * NormalizeFinite(item.Speed, 1), minimumDuration.Seconds));
        item.Speed = Math.Clamp(NormalizeFinite(item.Speed, 1), 0.1, 100);
        item.Volume = Math.Clamp(NormalizeFinite(item.Volume, 1), 0, 4);
        item.Opacity = Math.Clamp(NormalizeFinite(item.Opacity, 1), 0, 1);
        item.Pan = Math.Clamp(NormalizeFinite(item.Pan, 0), -1, 1);
        item.FontSize = Math.Clamp(NormalizeFinite(item.FontSize, 48), 1, 1_000);
        item.OutlineWidth = Math.Clamp(NormalizeFinite(item.OutlineWidth, 0), 0, 100);
        item.ShadowOpacity = Math.Clamp(NormalizeFinite(item.ShadowOpacity, 0.5), 0, 1);
        item.ShadowBlur = Math.Clamp(NormalizeFinite(item.ShadowBlur, 4), 0, 100);
        item.FadeInDuration = ClampTime(item.FadeInDuration, MediaTime.Zero, item.Duration);
        item.FadeOutDuration = ClampTime(item.FadeOutDuration, MediaTime.Zero, item.Duration);
        item.VisualTransitionInDuration = ClampTime(item.VisualTransitionInDuration, MediaTime.Zero, item.Duration);
        item.VisualTransitionOutDuration = ClampTime(item.VisualTransitionOutDuration, MediaTime.Zero, item.Duration);
        if (!Enum.IsDefined(item.VisualTransitionIn)) item.VisualTransitionIn = ItemTransitionKind.None;
        if (!Enum.IsDefined(item.VisualTransitionOut)) item.VisualTransitionOut = ItemTransitionKind.None;

        NormalizeTransform(item.Transform);
        NormalizeCrop(item);
        NormalizeAnimations(item, item.Duration);
        NormalizeMasks(item.Masks);
        NormalizeColor(item.ColorCorrection);
        NormalizeSpeedCurve(item.SpeedCurve);
        NormalizeChromaKey(item.ChromaKey);
        item.InvalidateAnimationChannelCache();
    }

    private static void NormalizeTransform(Transform2D transform)
    {
        transform.PositionX = NormalizeFinite(transform.PositionX, 0);
        transform.PositionY = NormalizeFinite(transform.PositionY, 0);
        transform.ScaleX = Math.Clamp(NormalizeFinite(transform.ScaleX, 1), 0.001, 1_000);
        transform.ScaleY = Math.Clamp(NormalizeFinite(transform.ScaleY, 1), 0.001, 1_000);
        transform.RotationDegrees = NormalizeFinite(transform.RotationDegrees, 0) % 360;
        transform.AnchorX = NormalizeFinite(transform.AnchorX, 0);
        transform.AnchorY = NormalizeFinite(transform.AnchorY, 0);
    }

    private static void NormalizeCrop(TimelineItem item)
    {
        item.CropLeft = Math.Clamp(NormalizeFinite(item.CropLeft, 0), 0, 0.999);
        item.CropRight = Math.Clamp(NormalizeFinite(item.CropRight, 0), 0, 0.999);
        item.CropTop = Math.Clamp(NormalizeFinite(item.CropTop, 0), 0, 0.999);
        item.CropBottom = Math.Clamp(NormalizeFinite(item.CropBottom, 0), 0, 0.999);
        var left = item.CropLeft;
        var right = item.CropRight;
        NormalizeCropPair(ref left, ref right);
        item.CropLeft = left;
        item.CropRight = right;
        var top = item.CropTop;
        var bottom = item.CropBottom;
        NormalizeCropPair(ref top, ref bottom);
        item.CropTop = top;
        item.CropBottom = bottom;
    }

    private static void NormalizeCropPair(ref double first, ref double second)
    {
        var total = first + second;
        if (total < 0.999) return;
        var scale = 0.998 / Math.Max(total, double.Epsilon);
        first *= scale;
        second *= scale;
    }

    private static void NormalizeAnimations(TimelineItem item, MediaTime duration)
    {
        IEnumerable<AnimationChannel> channels = item.AnimationChannels;
        if (item.AnimatedProperty != null)
            channels = channels.Append(item.AnimatedProperty);
        foreach (var channel in channels)
        {
            if (string.IsNullOrWhiteSpace(channel.PropertyName))
                throw new InvalidDataException($"Timeline item {item.Id} contains an animation channel without a property name.");
            channel.DefaultValue = NormalizeFinite(channel.DefaultValue, 0);
            var keyframeIds = new HashSet<KeyframeId>();
            foreach (var keyframe in channel.Keyframes)
            {
                RequireIdentity(keyframe.Id.Value, "keyframe");
                if (!keyframeIds.Add(keyframe.Id))
                    throw new InvalidDataException($"Duplicate keyframe ID {keyframe.Id} on timeline item {item.Id}.");
                keyframe.Time = ClampTime(keyframe.Time, MediaTime.Zero, duration);
                keyframe.Value = NormalizeFinite(keyframe.Value, channel.DefaultValue);
                keyframe.InTangentX = Math.Clamp(NormalizeFinite(keyframe.InTangentX, 0.75), 0, 1);
                keyframe.OutTangentX = Math.Clamp(NormalizeFinite(keyframe.OutTangentX, 0.25), 0, 1);
                keyframe.InTangentY = NormalizeFinite(keyframe.InTangentY, 0.75);
                keyframe.OutTangentY = NormalizeFinite(keyframe.OutTangentY, 0.25);
            }
            channel.NormalizeKeyframes();
        }
    }

    private static void NormalizeMasks(IEnumerable<Mask> masks)
    {
        foreach (var mask in masks)
        {
            mask.PositionX = NormalizeFinite(mask.PositionX, 0);
            mask.PositionY = NormalizeFinite(mask.PositionY, 0);
            mask.ScaleX = Math.Clamp(NormalizeFinite(mask.ScaleX, 1), 0.001, 1_000);
            mask.ScaleY = Math.Clamp(NormalizeFinite(mask.ScaleY, 1), 0.001, 1_000);
            mask.RotationDegrees = NormalizeFinite(mask.RotationDegrees, 0) % 360;
            mask.Feather = Math.Clamp(NormalizeFinite(mask.Feather, 0), 0, 10_000);
            mask.Expansion = Math.Clamp(NormalizeFinite(mask.Expansion, 0), -10_000, 10_000);
            mask.PolygonSides = Math.Clamp(mask.PolygonSides, 3, 256);
            foreach (var point in mask.Points)
            {
                point.X = NormalizeFinite(point.X, 0);
                point.Y = NormalizeFinite(point.Y, 0);
                point.InHandleX = NormalizeFinite(point.InHandleX, 0);
                point.InHandleY = NormalizeFinite(point.InHandleY, 0);
                point.OutHandleX = NormalizeFinite(point.OutHandleX, 0);
                point.OutHandleY = NormalizeFinite(point.OutHandleY, 0);
            }
        }
    }

    private static void NormalizeColor(ColorCorrection? color)
    {
        if (color == null) return;
        color.Brightness = Math.Clamp(NormalizeFinite(color.Brightness, 0), -1, 1);
        color.Contrast = Math.Clamp(NormalizeFinite(color.Contrast, 0), -1, 10);
        color.Saturation = Math.Clamp(NormalizeFinite(color.Saturation, 1), 0, 10);
        color.Exposure = Math.Clamp(NormalizeFinite(color.Exposure, 0), -10, 10);
        color.Highlights = Math.Clamp(NormalizeFinite(color.Highlights, 0), -1, 1);
        color.Shadows = Math.Clamp(NormalizeFinite(color.Shadows, 0), -1, 1);
        color.Whites = Math.Clamp(NormalizeFinite(color.Whites, 0), -1, 1);
        color.Blacks = Math.Clamp(NormalizeFinite(color.Blacks, 0), -1, 1);
        color.Tint = Math.Clamp(NormalizeFinite(color.Tint, 0), -1, 1);
    }

    private static void NormalizeSpeedCurve(SpeedCurve? curve)
    {
        if (curve == null) return;
        curve.ConstantSpeed = Math.Clamp(NormalizeFinite(curve.ConstantSpeed, 1), 0.1, 100);
        curve.Segments.RemoveAll(segment =>
            segment.SourceStart < MediaTime.Zero
            || segment.SourceEnd <= segment.SourceStart
            || !double.IsFinite(segment.Speed));
        foreach (var segment in curve.Segments)
            segment.Speed = Math.Clamp(segment.Speed, 0.1, 100);
        curve.Segments.Sort(static (left, right) => left.SourceStart.CompareTo(right.SourceStart));
    }

    private static void NormalizeChromaKey(ChromaKey? chroma)
    {
        if (chroma == null) return;
        chroma.Similarity = Math.Clamp(NormalizeFinite(chroma.Similarity, 0.1), 0, 1);
        chroma.Intensity = Math.Clamp(NormalizeFinite(chroma.Intensity, 0.1), 0, 1);
        chroma.EdgeSoftness = Math.Clamp(NormalizeFinite(chroma.EdgeSoftness, 0.05), 0, 1);
        chroma.SpillSuppression = Math.Clamp(NormalizeFinite(chroma.SpillSuppression, 0.1), 0, 1);
    }

    private static void ValidateMarkers(Sequence sequence)
    {
        var ids = new HashSet<MarkerId>();
        foreach (var marker in sequence.Markers)
        {
            RequireIdentity(marker.Id.Value, "marker");
            if (!ids.Add(marker.Id)) throw new InvalidDataException($"Duplicate marker ID {marker.Id}.");
            marker.Label = string.IsNullOrWhiteSpace(marker.Label) ? "Marker" : marker.Label.Trim();
            marker.Time = Max(marker.Time, MediaTime.Zero);
            marker.Duration = Max(marker.Duration, MediaTime.Zero);
            marker.DurationInFrames = Math.Max(0, marker.DurationInFrames);
        }
        sequence.Markers.Sort(static (left, right) => left.Time.CompareTo(right.Time));
    }

    private static void ValidateTransitions(Sequence sequence)
    {
        var itemLocations = sequence.Tracks
            .SelectMany(track => track.Items.Select(item => (track, item)))
            .ToDictionary(pair => pair.item.Id);
        sequence.Transitions.RemoveAll(transition =>
            transition.LeftItemId == transition.RightItemId
            || !itemLocations.TryGetValue(transition.LeftItemId, out var leftLocation)
            || !itemLocations.TryGetValue(transition.RightItemId, out var rightLocation)
            || leftLocation.track.Id != rightLocation.track.Id
            || leftLocation.track.Kind is not (TrackKind.Video or TrackKind.Overlay)
            || leftLocation.item.Kind is not (ItemKind.Clip or ItemKind.Image)
            || rightLocation.item.Kind is not (ItemKind.Clip or ItemKind.Image));
        var items = itemLocations.ToDictionary(pair => pair.Key, pair => pair.Value.item);
        foreach (var transition in sequence.Transitions)
        {
            var left = items[transition.LeftItemId];
            var right = items[transition.RightItemId];
            var maximum = Math.Min(left.Duration.Seconds, right.Duration.Seconds);
            transition.Duration = MediaTime.FromSeconds(Math.Clamp(
                NormalizeFinite(transition.Duration.Seconds, 0),
                0,
                maximum));
            transition.Alignment = Math.Clamp(NormalizeFinite(transition.Alignment, 0.5), 0, 1);
        }
    }

    private static void RequireIdentity(Guid value, string kind)
    {
        if (value == Guid.Empty) throw new InvalidDataException($"A {kind} has an empty ID.");
    }

    private static double NormalizeFinite(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static MediaTime Max(MediaTime value, MediaTime minimum) =>
        value < minimum ? minimum : value;

    private static MediaTime ClampTime(MediaTime value, MediaTime minimum, MediaTime maximum) =>
        MediaTime.FromTicks(Math.Clamp(value.Ticks, minimum.Ticks, maximum.Ticks));
}
