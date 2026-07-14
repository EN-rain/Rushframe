using Rushframe.Domain;

namespace Rushframe.Desktop.Timeline;

/// <summary>
/// Revision-scoped derived timeline state used by rendering, hit testing, snapping and preview.
/// Rebuilds only when the owning project revision or sequence changes.
/// </summary>
public sealed class TimelineSceneIndex
{
    private Sequence? _sequence;
    private long _revision = long.MinValue;
    private TrackScene[] _tracks = [];
    private Marker[] _markers = [];
    private double[] _snapPoints = [];
    private TransitionSelection[] _transitionSlots = [];
    private Dictionary<TimelineItemId, int> _trackByItem = [];

    public double DurationSeconds { get; private set; } = 60;
    public int TrackCount => _tracks.Length;
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<TransitionSelection> TransitionSlots => _transitionSlots;

    public void Ensure(Sequence sequence, long revision)
    {
        if (ReferenceEquals(_sequence, sequence) && _revision == revision) return;
        Rebuild(sequence, revision);
    }

    public void Invalidate()
    {
        _sequence = null;
        _revision = long.MinValue;
    }

    public IReadOnlyList<TimelineItem> GetTrackItems(int trackIndex) =>
        trackIndex >= 0 && trackIndex < _tracks.Length ? _tracks[trackIndex].Items : [];

    public int FindFirstPotentiallyVisibleItem(int trackIndex, double visibleStartSeconds)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Length) return 0;
        var track = _tracks[trackIndex];
        var low = 0;
        var high = track.Items.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (track.MaxEndPrefix[middle] < visibleStartSeconds) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    public TimelineItem? HitTestItem(int trackIndex, double timeSeconds)
    {
        if (trackIndex < 0 || trackIndex >= _tracks.Length) return null;
        var items = _tracks[trackIndex].Items;
        if (items.Length == 0) return null;
        var first = FindFirstPotentiallyVisibleItem(trackIndex, timeSeconds);
        TimelineItem? match = null;
        for (var index = first; index < items.Length; index++)
        {
            var item = items[index];
            if (item.TimelineStart.Seconds > timeSeconds) break;
            if (item.TimelineEnd.Seconds >= timeSeconds) match = item;
        }
        return match;
    }

    public int GetTrackIndex(TimelineItemId itemId) =>
        _trackByItem.TryGetValue(itemId, out var index) ? index : -1;

    public Marker? FindNearestMarker(double timeSeconds, double thresholdSeconds)
    {
        if (_markers.Length == 0) return null;
        var low = 0;
        var high = _markers.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_markers[middle].Time.Seconds < timeSeconds) low = middle + 1;
            else high = middle;
        }

        Marker? best = null;
        var bestDistance = thresholdSeconds + double.Epsilon;
        for (var index = Math.Max(0, low - 1); index <= Math.Min(_markers.Length - 1, low); index++)
        {
            var distance = Math.Abs(_markers[index].Time.Seconds - timeSeconds);
            if (distance <= thresholdSeconds && distance < bestDistance)
            {
                best = _markers[index];
                bestDistance = distance;
            }
        }
        return best;
    }

    public double FindNearestSnapPoint(double timeSeconds, double thresholdSeconds, TimelineItemId excludeId)
    {
        if (_snapPoints.Length == 0) return timeSeconds;
        var low = 0;
        var high = _snapPoints.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_snapPoints[middle] < timeSeconds) low = middle + 1;
            else high = middle;
        }

        var best = timeSeconds;
        var bestDistance = thresholdSeconds + double.Epsilon;
        var start = Math.Max(0, low - 4);
        var end = Math.Min(_snapPoints.Length - 1, low + 4);
        for (var index = start; index <= end; index++)
        {
            var point = _snapPoints[index];
            var distance = Math.Abs(point - timeSeconds);
            if (distance <= thresholdSeconds && distance < bestDistance)
            {
                best = point;
                bestDistance = distance;
            }
        }

        // Excluding one item is intentionally handled by evaluating its own boundaries last.
        // This keeps the index compact and the common path allocation-free.
        if (_sequence != null)
        {
            foreach (var track in _sequence.Tracks)
            {
                var excluded = track.Items.FirstOrDefault(item => item.Id == excludeId);
                if (excluded == null) continue;
                if (Math.Abs(best - excluded.TimelineStart.Seconds) < 0.000001
                    || Math.Abs(best - excluded.TimelineEnd.Seconds) < 0.000001)
                    return timeSeconds;
                break;
            }
        }
        return best;
    }

    private void Rebuild(Sequence sequence, long revision)
    {
        _sequence = sequence;
        _revision = revision;
        _tracks = new TrackScene[sequence.Tracks.Count];
        _trackByItem = new Dictionary<TimelineItemId, int>();
        var duration = 0.0;
        var snapPoints = new List<double> { 0 };

        for (var trackIndex = 0; trackIndex < sequence.Tracks.Count; trackIndex++)
        {
            var items = sequence.Tracks[trackIndex].Items
                .OrderBy(item => item.TimelineStart.Ticks)
                .ThenBy(item => item.Id.Value)
                .ToArray();
            _tracks[trackIndex] = new TrackScene(items);
            foreach (var item in items)
            {
                _trackByItem[item.Id] = trackIndex;
                duration = Math.Max(duration, item.TimelineEnd.Seconds);
                snapPoints.Add(item.TimelineStart.Seconds);
                snapPoints.Add(item.TimelineEnd.Seconds);
            }
        }

        _markers = sequence.Markers.OrderBy(marker => marker.Time.Ticks).ToArray();
        foreach (var marker in _markers) snapPoints.Add(marker.Time.Seconds);
        _snapPoints = snapPoints.Distinct().OrderBy(value => value).ToArray();
        DurationSeconds = Math.Max(duration + 10, 60);

        var transitionLookup = sequence.Transitions.ToDictionary(
            transition => (transition.LeftItemId, transition.RightItemId));
        var slots = new List<TransitionSelection>();
        for (var trackIndex = 0; trackIndex < _tracks.Length; trackIndex++)
        {
            var items = _tracks[trackIndex].Items;
            for (var index = 0; index < items.Length - 1; index++)
            {
                var left = items[index];
                var right = items[index + 1];
                var gap = right.TimelineStart.Seconds - left.TimelineEnd.Seconds;
                if (Math.Abs(gap) > 0.05) continue;
                transitionLookup.TryGetValue((left.Id, right.Id), out var transition);
                slots.Add(new TransitionSelection(transition, left, right, trackIndex));
            }
        }
        _transitionSlots = slots.ToArray();
    }

    private sealed class TrackScene
    {
        public TrackScene(TimelineItem[] items)
        {
            Items = items;
            MaxEndPrefix = new double[items.Length];
            var maximum = double.MinValue;
            for (var index = 0; index < items.Length; index++)
            {
                maximum = Math.Max(maximum, items[index].TimelineEnd.Seconds);
                MaxEndPrefix[index] = maximum;
            }
        }

        public TimelineItem[] Items { get; }
        public double[] MaxEndPrefix { get; }
    }
}
