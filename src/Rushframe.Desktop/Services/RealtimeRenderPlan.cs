using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

/// <summary>
/// Revision-scoped, allocation-free query plan for real-time WPF preview.
/// </summary>
public sealed class RealtimeRenderPlan
{
    private readonly IntervalIndex<VisualEntry> _visuals;
    private readonly IntervalIndex<AudioEntry> _audio;
    private readonly Dictionary<MediaAssetId, MediaAsset> _mediaById;
    private readonly Dictionary<TimelineItemId, double> _itemStartById;

    private RealtimeRenderPlan(
        long revision,
        Sequence sequence,
        VisualEntry[] visuals,
        AudioEntry[] audio,
        Dictionary<MediaAssetId, MediaAsset> mediaById,
        Dictionary<TimelineItemId, double> itemStartById,
        int maxConcurrentMediaPlayers)
    {
        Revision = revision;
        Sequence = sequence;
        _visuals = new IntervalIndex<VisualEntry>(visuals, static entry => entry.ActiveStart, static entry => entry.ActiveEnd);
        _audio = new IntervalIndex<AudioEntry>(audio, static entry => entry.ActiveStart, static entry => entry.ActiveEnd);
        _mediaById = mediaById;
        _itemStartById = itemStartById;
        MaxConcurrentMediaPlayers = maxConcurrentMediaPlayers;
        DurationSeconds = Math.Max(0, sequence.Tracks.SelectMany(track => track.Items)
            .Select(item => item.TimelineEnd.Seconds)
            .DefaultIfEmpty(0)
            .Max());
    }

    public long Revision { get; }
    public Sequence Sequence { get; }
    public int MaxConcurrentMediaPlayers { get; }
    public double DurationSeconds { get; }

    public static RealtimeRenderPlan Build(Project project, Sequence sequence)
    {
        var mediaById = project.MediaLibrary.ToDictionary(asset => asset.Id);
        var incomingByItem = new Dictionary<TimelineItemId, Transition>();
        var outgoingByItem = new Dictionary<TimelineItemId, Transition>();
        var itemStartById = sequence.Tracks
            .SelectMany(track => track.Items)
            .ToDictionary(item => item.Id, item => item.TimelineStart.Seconds);
        foreach (var transition in sequence.Transitions)
        {
            incomingByItem[transition.RightItemId] = transition;
            outgoingByItem[transition.LeftItemId] = transition;
        }

        var visuals = new List<VisualEntry>();
        var audio = new List<AudioEntry>();
        var hasSoloTracks = sequence.Tracks.Any(track => track.Solo && !track.Hidden);
        foreach (var track in sequence.Tracks)
        {
            if (track.Hidden || hasSoloTracks && !track.Solo) continue;
            foreach (var item in track.Items)
            {
                incomingByItem.TryGetValue(item.Id, out var incoming);
                outgoingByItem.TryGetValue(item.Id, out var outgoing);
                var (activeStart, activeEnd) = GetActiveInterval(sequence, item, incoming, outgoing);
                if (track.Kind is TrackKind.Video or TrackKind.Overlay or TrackKind.Text
                    && item.Kind is ItemKind.Clip or ItemKind.Image or ItemKind.Text or ItemKind.Sticker)
                {
                    mediaById.TryGetValue(item.MediaAssetId ?? default, out var media);
                    visuals.Add(new VisualEntry(track, item, media, incoming, outgoing, activeStart, activeEnd));
                }

                if (!track.Muted
                    && track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice
                    && !item.Muted
                    && item.MediaAssetId is { } audioAssetId
                    && mediaById.TryGetValue(audioAssetId, out var audioMedia))
                {
                    audio.Add(new AudioEntry(track, item, audioMedia, activeStart, activeEnd));
                }
            }
        }

        var mediaIntervals = visuals
            .Where(entry => entry.Media?.Kind == MediaKind.Video)
            .Select(entry => (entry.ActiveStart, entry.ActiveEnd))
            .Concat(audio.Select(entry => (entry.ActiveStart, entry.ActiveEnd)))
            .ToArray();

        return new RealtimeRenderPlan(
            project.Revision,
            sequence,
            visuals.OrderBy(entry => entry.ActiveStart).ThenBy(entry => sequence.Tracks.IndexOf(entry.Track)).ToArray(),
            audio.OrderBy(entry => entry.ActiveStart).ThenBy(entry => sequence.Tracks.IndexOf(entry.Track)).ToArray(),
            mediaById,
            itemStartById,
            ComputeMaxConcurrency(mediaIntervals));
    }

    public bool TryGetMedia(MediaAssetId id, out MediaAsset media) => _mediaById.TryGetValue(id, out media!);

    public void CollectActiveVisuals(double seconds, List<VisualEntry> destination) =>
        _visuals.Collect(seconds, destination);

    public void CollectWarmVisuals(double seconds, double lookAheadSeconds, List<VisualEntry> destination) =>
        _visuals.CollectWindow(seconds, seconds + Math.Max(0, lookAheadSeconds), destination);

    public void CollectActiveAudio(double seconds, List<AudioEntry> destination) =>
        _audio.Collect(seconds, destination);

    public RealtimePresentation GetPresentation(VisualEntry entry, double timelineSeconds)
    {
        var item = entry.Item;
        var active = timelineSeconds >= item.TimelineStart.Seconds && timelineSeconds <= item.TimelineEnd.Seconds;
        var opacity = 1.0;
        var offsetX = 0.0;
        var offsetY = 0.0;
        var scale = 1.0;

        if (entry.Incoming is { } incoming)
        {
            var duration = Math.Max(0.001, incoming.Duration.Seconds);
            var start = item.TimelineStart.Seconds - duration * Math.Clamp(incoming.Alignment, 0, 1);
            var end = start + duration;
            if (timelineSeconds >= start && timelineSeconds <= end)
            {
                active = true;
                var progress = Math.Clamp((timelineSeconds - start) / duration, 0, 1);
                switch (incoming.Kind)
                {
                    case TransitionKind.CrossDissolve: opacity *= progress; break;
                    case TransitionKind.Slide: offsetX += Sequence.Width * (1 - progress); break;
                    case TransitionKind.Zoom: scale *= 0.82 + (0.18 * progress); opacity *= progress; break;
                }
            }
        }

        ApplyItemTransitionPresentation(item, timelineSeconds, ref active, ref opacity, ref offsetX, ref offsetY, ref scale);

        if (entry.Outgoing is { } outgoing)
        {
            var cut = _itemStartById.TryGetValue(outgoing.RightItemId, out var rightStart)
                ? rightStart
                : item.TimelineEnd.Seconds;
            var duration = Math.Max(0.001, outgoing.Duration.Seconds);
            var start = cut - duration * Math.Clamp(outgoing.Alignment, 0, 1);
            var end = start + duration;
            if (timelineSeconds >= start && timelineSeconds <= end)
            {
                active = true;
                var progress = Math.Clamp((timelineSeconds - start) / duration, 0, 1);
                switch (outgoing.Kind)
                {
                    case TransitionKind.CrossDissolve: opacity *= 1 - progress; break;
                    case TransitionKind.Slide: offsetX -= Sequence.Width * progress; break;
                    case TransitionKind.Zoom: scale *= 1 + (0.12 * progress); opacity *= 1 - progress; break;
                }
            }
        }

        return new RealtimePresentation(active, opacity, offsetX, offsetY, scale);
    }

    private void ApplyItemTransitionPresentation(
        TimelineItem item,
        double timelineSeconds,
        ref bool active,
        ref double opacity,
        ref double offsetX,
        ref double offsetY,
        ref double scale)
    {
        var local = timelineSeconds - item.TimelineStart.Seconds;
        if (item.VisualTransitionIn != ItemTransitionKind.None && item.VisualTransitionInDuration.Seconds > 0 && local >= 0 && local <= item.VisualTransitionInDuration.Seconds)
        {
            active = true;
            var progress = Math.Clamp(local / item.VisualTransitionInDuration.Seconds, 0, 1);
            ApplyItemTransition(item.VisualTransitionIn, 1 - progress, progress, ref opacity, ref offsetX, ref offsetY, ref scale);
        }
        var remaining = item.Duration.Seconds - local;
        if (item.VisualTransitionOut != ItemTransitionKind.None && item.VisualTransitionOutDuration.Seconds > 0 && remaining >= 0 && remaining <= item.VisualTransitionOutDuration.Seconds)
        {
            active = true;
            var progress = Math.Clamp(remaining / item.VisualTransitionOutDuration.Seconds, 0, 1);
            ApplyItemTransition(item.VisualTransitionOut, 1 - progress, progress, ref opacity, ref offsetX, ref offsetY, ref scale);
        }
    }

    private void ApplyItemTransition(
        ItemTransitionKind kind,
        double movement,
        double visibility,
        ref double opacity,
        ref double offsetX,
        ref double offsetY,
        ref double scale)
    {
        switch (kind)
        {
            case ItemTransitionKind.Fade: opacity *= visibility; break;
            case ItemTransitionKind.SlideLeft: offsetX -= Sequence.Width * movement; break;
            case ItemTransitionKind.SlideRight: offsetX += Sequence.Width * movement; break;
            case ItemTransitionKind.SlideUp: offsetY -= Sequence.Height * movement; break;
            case ItemTransitionKind.SlideDown: offsetY += Sequence.Height * movement; break;
            case ItemTransitionKind.ZoomIn: scale *= 0.72 + 0.28 * visibility; opacity *= visibility; break;
            case ItemTransitionKind.ZoomOut: scale *= 1.28 - 0.28 * visibility; opacity *= visibility; break;
            case ItemTransitionKind.Pop: scale *= 0.68 + 0.44 * visibility - 0.12 * visibility * visibility; opacity *= visibility; break;
            case ItemTransitionKind.SpinClockwise:
            case ItemTransitionKind.SpinCounterClockwise:
            case ItemTransitionKind.WipeLeft:
            case ItemTransitionKind.WipeRight:
                opacity *= visibility;
                break;
        }
    }

    private static (double Start, double End) GetActiveInterval(
        Sequence sequence,
        TimelineItem item,
        Transition? incoming,
        Transition? outgoing)
    {
        var start = item.TimelineStart.Seconds;
        var end = item.TimelineEnd.Seconds;
        if (incoming != null)
        {
            var duration = Math.Max(0.001, incoming.Duration.Seconds);
            start = Math.Min(start, item.TimelineStart.Seconds - duration * Math.Clamp(incoming.Alignment, 0, 1));
        }
        if (outgoing != null)
        {
            var right = sequence.Tracks.SelectMany(track => track.Items)
                .FirstOrDefault(candidate => candidate.Id == outgoing.RightItemId);
            var cut = right?.TimelineStart.Seconds ?? item.TimelineEnd.Seconds;
            var duration = Math.Max(0.001, outgoing.Duration.Seconds);
            end = Math.Max(end, cut + duration * (1 - Math.Clamp(outgoing.Alignment, 0, 1)));
        }
        return (start, end);
    }

    private static int ComputeMaxConcurrency((double Start, double End)[] intervals)
    {
        if (intervals.Length == 0) return 0;
        var events = new (double Time, int Delta)[intervals.Length * 2];
        for (var index = 0; index < intervals.Length; index++)
        {
            events[index * 2] = (intervals[index].Start, 1);
            events[(index * 2) + 1] = (intervals[index].End, -1);
        }
        Array.Sort(events, static (left, right) =>
        {
            var result = left.Time.CompareTo(right.Time);
            return result != 0 ? result : left.Delta.CompareTo(right.Delta);
        });
        var current = 0;
        var maximum = 0;
        foreach (var entry in events)
        {
            current += entry.Delta;
            maximum = Math.Max(maximum, current);
        }
        return maximum;
    }

    public sealed record VisualEntry(
        Track Track,
        TimelineItem Item,
        MediaAsset? Media,
        Transition? Incoming,
        Transition? Outgoing,
        double ActiveStart,
        double ActiveEnd);

    public sealed record AudioEntry(
        Track Track,
        TimelineItem Item,
        MediaAsset Media,
        double ActiveStart,
        double ActiveEnd);

    public readonly record struct RealtimePresentation(
        bool IsActive,
        double Opacity,
        double OffsetX,
        double OffsetY,
        double ScaleMultiplier);

    private sealed class IntervalIndex<T>
    {
        private readonly T[] _entries;
        private readonly double[] _starts;
        private readonly double[] _maxEndPrefix;
        private readonly Func<T, double> _end;

        public IntervalIndex(T[] entries, Func<T, double> start, Func<T, double> end)
        {
            _entries = entries;
            _end = end;
            _starts = new double[entries.Length];
            _maxEndPrefix = new double[entries.Length];
            var maximum = double.MinValue;
            for (var index = 0; index < entries.Length; index++)
            {
                _starts[index] = start(entries[index]);
                maximum = Math.Max(maximum, end(entries[index]));
                _maxEndPrefix[index] = maximum;
            }
        }

        public void Collect(double seconds, List<T> destination) =>
            CollectWindow(seconds, seconds, destination);

        public void CollectWindow(double startSeconds, double endSeconds, List<T> destination)
        {
            destination.Clear();
            if (_entries.Length == 0) return;
            var low = 0;
            var high = _entries.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (_maxEndPrefix[middle] < startSeconds) low = middle + 1;
                else high = middle;
            }
            for (var index = low; index < _entries.Length; index++)
            {
                if (_starts[index] > endSeconds) break;
                if (_end(_entries[index]) >= startSeconds) destination.Add(_entries[index]);
            }
        }
    }
}
