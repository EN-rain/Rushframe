namespace Rushframe.Domain;

public sealed class Sequence
{
    private FrameRate _frameRate = FrameRate.Fps30;
    private MediaTime _cachedDuration;
    private long _observedTimingVersion = long.MinValue;
    private int _cachedTrackCount = -1;
    private int _cachedItemCount = -1;

    public SequenceId Id { get; init; } = SequenceId.New();
    public string Name { get; set; } = "Main";
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;

    /// <summary>
    /// Legacy-friendly floating point access. The canonical value is <see cref="FrameRate"/>.
    /// Keeping this property preserves existing project files that contain an fps number.
    /// </summary>
    public double Fps
    {
        get => _frameRate.Value;
        set => _frameRate = FrameRate.FromDouble(value);
    }

    public FrameRate FrameRate
    {
        get => _frameRate;
        set => _frameRate = value.Numerator > 0 && value.Denominator > 0 ? value : FrameRate.Fps30;
    }

    public CanvasBackground Background { get; set; } = new();
    public List<LayoutGuide> LayoutGuides { get; init; } = [];
    public List<Track> Tracks { get; init; } = [];
    public List<Marker> Markers { get; init; } = [];
    public List<Transition> Transitions { get; init; } = [];

    public MediaTime Duration
    {
        get
        {
            var timingVersion = TimelineItem.GlobalTimingMutationVersion;
            var itemCount = 0;
            foreach (var track in Tracks) itemCount += track.Items.Count;
            if (_observedTimingVersion == timingVersion
                && _cachedTrackCount == Tracks.Count
                && _cachedItemCount == itemCount)
                return _cachedDuration;

            var max = MediaTime.Zero;
            foreach (var track in Tracks)
            foreach (var item in track.Items)
            {
                var end = item.TimelineEnd;
                if (end > max) max = end;
            }
            _cachedDuration = max;
            _observedTimingVersion = timingVersion;
            _cachedTrackCount = Tracks.Count;
            _cachedItemCount = itemCount;
            return max;
        }
    }
}
