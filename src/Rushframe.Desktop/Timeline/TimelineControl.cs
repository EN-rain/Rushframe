using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Viewport;
using Rushframe.Domain;

namespace Rushframe.Desktop.Timeline;

public sealed partial class TimelineControl : FrameworkElement
{
    private static readonly SolidColorBrush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(10, 7, 16)));
    private static readonly SolidColorBrush HeaderBackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(13, 9, 21)));
    private static readonly SolidColorBrush RulerBackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(17, 12, 26)));
    private static readonly SolidColorBrush BorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(53, 42, 73)));
    private static readonly SolidColorBrush RulerTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(153, 137, 174)));
    private static readonly Pen BorderPen = Freeze(new Pen(BorderBrush, 0.75));
    private static readonly Pen RulerGridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(70, 89, 69, 114)), 0.6));
    private static readonly Pen TrackBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(45, 35, 61)), 0.6));
    private static readonly Pen SelectedClipBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(179, 139, 255)), 1.8));
    private static readonly Pen ClipBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(74, 55, 104)), 0.8));
    private static readonly Pen ClipHighlightPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(105, 220, 205, 255)), 0.7));
    private static readonly Pen WaveformPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(175, 190, 164, 255)), 0.75));
    private static readonly Pen AnimationLanePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(120, 235, 220, 255)), 0.75));
    private static readonly Pen FadePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1));
    private static readonly SolidColorBrush AnimationKeyBrush = Freeze(new SolidColorBrush(Color.FromRgb(226, 202, 255)));
    private static readonly SolidColorBrush TrackEvenBrush = Freeze(new SolidColorBrush(Color.FromRgb(17, 12, 26)));
    private static readonly SolidColorBrush TrackOddBrush = Freeze(new SolidColorBrush(Color.FromRgb(13, 9, 21)));
    private static readonly Typeface RulerTypeface = new("Cascadia Mono");
    private static readonly Typeface UiTypeface = new("Segoe UI");
    private static readonly Dictionary<(ItemKind Kind, bool Audio), Brush> ClipBrushes = BuildClipBrushes();

    private readonly TimelineViewport _viewport = new();
    private readonly TimelineSceneIndex _sceneIndex = new();
    private readonly Dictionary<WaveformDrawingKey, DrawingImage> _waveformDrawingCache = [];
    private readonly EditorPerformanceTelemetry _telemetry = EditorPerformanceTelemetry.Shared;

    public Sequence? Sequence
    {
        get => _sequence;
        set
        {
            if (ReferenceEquals(_sequence, value)) return;
            _sequence = value;
            _sceneIndex.Invalidate();
            ClearSelection();
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Sequence? _sequence;
    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isDraggingClip;
    private bool _isTrimming;
    private TimelineItem? _dragItem;
    private int _dragTrackIndex;
    private MediaTime _dragStartTime;
    private MediaTime _dragOrigDuration;
    private MediaTime _dragOrigSourceStart;
    private double _dragOrigVolume;
    private double _dragOriginMouseX;
    private DragMode _dragMode;
    private MediaTime _playheadTime;
    private TimelineItem? _selectedItem;
    private Transition? _selectedTransition;
    private int _selectedTrackIndex;
    private bool _dragFromPlayhead;
    private long _projectRevision;

    private enum DragMode { None, Move, TrimLeft, TrimRight, Volume }

    public MediaTime PlayheadTime
    {
        get => _playheadTime;
        set
        {
            if (_playheadTime == value) return;
            _playheadTime = value;
            PlayheadVisualChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public long ProjectRevision
    {
        get => _projectRevision;
        set
        {
            if (_projectRevision == value) return;
            _projectRevision = value;
            _sceneIndex.Invalidate();
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public double PlayheadPixelX => _viewport.TimeToPixel(_playheadTime);
    public double TrackHeaderWidth => _viewport.TrackHeaderWidth;
    public double RulerHeight => _viewport.RulerHeight;
    public double VisibleStartSeconds => _viewport.TimeAtLeftEdgeSeconds;
    public double VisibleEndSeconds => _viewport.TimeAtLeftEdgeSeconds + _viewport.VisibleDurationSeconds;
    public TimelineItem? SelectedItem => _selectedItem;
    public IReadOnlyList<TimelineItem> SelectedItems => ResolveSelectedItems();
    public Transition? SelectedTransition => _selectedTransition;
    public int SelectedTrackIndex => _selectedTrackIndex;
    public bool SnapEnabled { get; set; } = true;
    public Func<MediaAssetId, string?>? AssetNameResolver { get; set; }
    public Func<MediaAssetId, IReadOnlyList<float>?>? AssetWaveformResolver { get; set; }
    public ContextMenu? ClipContextMenu { get; set; }
    public ContextMenu? TrackHeaderContextMenu { get; set; }

    public event EventHandler<TimelineItem?>? ClipSelected;
    public event EventHandler<TransitionSelection?>? TransitionSelected;
    public event EventHandler? PlayheadMoved;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler<Marker>? MarkerEditRequested;
    public event EventHandler<int>? TrackHeaderContextRequested;
    public event EventHandler? DeleteSelectedClipRequested;
    public event EventHandler<ClipMoveRequestedEventArgs>? ClipMoveRequested;
    public event EventHandler<ClipTrimRequestedEventArgs>? ClipTrimRequested;
    public event EventHandler<ClipVolumeRequestedEventArgs>? ClipVolumeRequested;
    public event EventHandler<GroupMoveRequestedEventArgs>? GroupMoveRequested;
    public event EventHandler<GroupTrimRequestedEventArgs>? GroupTrimRequested;
    public event EventHandler<IReadOnlyList<TimelineItem>>? SelectionChanged;
    public event EventHandler<double>? ZoomScaleChanged;
    public event EventHandler? PlayheadVisualChanged;
    public event EventHandler? ViewportChanged;

    private const double TrimEdgeThreshold = 8;

    static TimelineControl()
    {
        FocusableProperty.OverrideMetadata(typeof(TimelineControl), new FrameworkPropertyMetadata(true));
    }

    public TimelineControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _viewport.TrackHeaderWidth = 190;
        _viewport.TrackHeight = 46;
        _viewport.RulerHeight = 48;
    }

    public void ClearSelection()
    {
        var hadClip = _selectedItem != null;
        var hadTransition = _selectedTransition != null;
        _selectedItem = null;
        _selectedItemIds.Clear();
        _groupDragSnapshots.Clear();
        _selectedTransition = null;
        _selectedTrackIndex = -1;
        _dragItem = null;
        _dragMode = DragMode.None;
        _isDraggingClip = false;
        _isTrimming = false;
        if (hadClip) ClipSelected?.Invoke(this, null);
        if (hadTransition) TransitionSelected?.Invoke(this, null);
        InvalidateVisual();
    }

    public void ScrollToTime(MediaTime time)
    {
        var targetPixel = _viewport.TimeToPixel(time);
        if (targetPixel < _viewport.TrackHeaderWidth || targetPixel > _viewport.ViewportWidth)
        {
            var contentWidth = Math.Max(0, _viewport.ViewportWidth - _viewport.TrackHeaderWidth);
            _viewport.HorizontalOffset = Math.Clamp(
                time.Seconds * _viewport.PixelsPerSecond - contentWidth / 2,
                0,
                _viewport.GetMaxHorizontalOffset());
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ScrollBy(double deltaPixels)
    {
        _viewport.HorizontalOffset = Math.Clamp(
            _viewport.HorizontalOffset + deltaPixels,
            0,
            _viewport.GetMaxHorizontalOffset());
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetZoomScale(double scale)
    {
        SyncViewportSize();
        _viewport.SetZoomScale(scale, Math.Max(_viewport.TrackHeaderWidth, RenderSize.Width / 2));
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectItem(TimelineItem? item, int trackIndex)
    {
        _selectedItem = item;
        _selectedItemIds.Clear();
        if (item != null) _selectedItemIds.Add(item.Id);
        _selectedTransition = null;
        _selectedTrackIndex = item == null ? -1 : trackIndex;
        InvalidateVisual();
    }

    public void SelectTransition(Transition? transition, int trackIndex)
    {
        _selectedItem = null;
        _selectedItemIds.Clear();
        _selectedTransition = transition;
        _selectedTrackIndex = trackIndex;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        using var measurement = _telemetry.MeasureTimelineRender();
        SyncViewportSize();
        DrawBackground(dc);
        if (Sequence == null) return;
        EnsureSceneIndex();

        DrawRuler(dc);
        DrawTrackHeaders(dc);

        var contentClip = GetContentClipRect(includeRuler: true);
        if (contentClip.Width > 0 && contentClip.Height > 0)
        {
            dc.PushClip(new RectangleGeometry(contentClip));
            var firstTrack = Math.Max(0, _viewport.YToTrackIndex(_viewport.RulerHeight));
            var lastTrack = Math.Min(Sequence.Tracks.Count - 1, _viewport.YToTrackIndex(RenderSize.Height) + 1);
            for (var index = firstTrack; index <= lastTrack; index++)
                DrawTrack(dc, Sequence.Tracks[index], index);

            DrawTransitionHandles(dc);
            DrawMarkers(dc);
            DrawDraggedClipGhost(dc);
            DrawActiveSnapGuide(dc);
            DrawSelectionBox(dc);
            dc.Pop();
        }
    }

    private void EnsureSceneIndex()
    {
        if (Sequence == null) return;
        _sceneIndex.Ensure(Sequence, _projectRevision);
        _viewport.SequenceDurationSeconds = _sceneIndex.DurationSeconds;
        _viewport.TrackCount = _sceneIndex.TrackCount;
        _viewport.HorizontalOffset = Math.Clamp(_viewport.HorizontalOffset, 0, _viewport.GetMaxHorizontalOffset());
        _viewport.VerticalOffset = Math.Clamp(_viewport.VerticalOffset, 0, _viewport.GetMaxVerticalOffset());
    }

    private void DrawBackground(DrawingContext dc)
    {
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
    }

    private void DrawRuler(DrawingContext dc)
    {
        var headerRect = new Rect(0, 0, _viewport.TrackHeaderWidth, _viewport.RulerHeight);
        var rulerRect = new Rect(_viewport.TrackHeaderWidth, 0,
            Math.Max(0, RenderSize.Width - _viewport.TrackHeaderWidth), _viewport.RulerHeight);
        dc.DrawRectangle(HeaderBackgroundBrush, BorderPen, headerRect);
        dc.DrawRectangle(RulerBackgroundBrush, BorderPen, rulerRect);

        var step = Math.Max(1, (int)Math.Ceiling(90 / Math.Max(1, _viewport.PixelsPerSecond)));
        var startSec = (int)_viewport.TimeAtLeftEdgeSeconds;
        startSec = startSec / step * step;

        var rulerClip = GetContentClipRect(includeRuler: true);
        if (rulerClip.Width <= 0 || rulerClip.Height <= 0) return;

        dc.PushClip(new RectangleGeometry(rulerClip));
        for (var s = startSec; s <= _viewport.TimeAtLeftEdgeSeconds + _viewport.VisibleDurationSeconds + step; s += step)
        {
            var x = _viewport.TimeToPixel(MediaTime.FromSeconds(s));
            if (x < _viewport.TrackHeaderWidth || x > RenderSize.Width) continue;

            var ft = new FormattedText(
                FormatRulerTime(s),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                RulerTypeface,
                9,
                RulerTextBrush,
                1.25);
            dc.DrawText(ft, new Point(x + 5, 18));

            dc.DrawLine(
                RulerGridPen,
                new Point(x, _viewport.RulerHeight - 8),
                new Point(x, RenderSize.Height));
        }
        dc.Pop();
    }

    private static string FormatRulerTime(int seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}:00"
            : $"00:{time.Minutes:00}:{time.Seconds:00}:00";
    }

    private void DrawTrackHeaders(DrawingContext dc)
    {
        var trackCount = Sequence?.Tracks.Count ?? 0;
        var firstTrack = Math.Max(0, _viewport.YToTrackIndex(_viewport.RulerHeight));
        var lastTrack = Math.Min(trackCount - 1, _viewport.YToTrackIndex(RenderSize.Height) + 1);
        for (var i = firstTrack; i <= lastTrack; i++)
        {
            var track = Sequence!.Tracks[i];
            var y = _viewport.TrackIndexToY(i);
            var rect = new Rect(0, y, _viewport.TrackHeaderWidth, _viewport.TrackHeight);
            var selected = i == _selectedTrackIndex;
            var background = selected
                ? Color.FromRgb(25, 21, 38)
                : i % 2 == 0 ? Color.FromRgb(19, 15, 29) : Color.FromRgb(16, 12, 25);
            dc.DrawRectangle(new SolidColorBrush(background), new Pen(new SolidColorBrush(Color.FromRgb(53, 42, 73)), 0.65), rect);

            var code = GetTrackCode(track, i);
            var badgeRect = new Rect(10, y + 10, 34, 25);
            var badgeFill = selected
                ? new SolidColorBrush(Color.FromRgb(124, 58, 237))
                : new SolidColorBrush(Color.FromRgb(38, 35, 49));
            dc.DrawRoundedRectangle(badgeFill, null, badgeRect, 4, 4);
            var codeText = new FormattedText(
                code,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                10,
                Brushes.White,
                1.25);
            dc.DrawText(codeText, new Point(badgeRect.X + (badgeRect.Width - codeText.Width) / 2, badgeRect.Y + 5));

            var name = string.IsNullOrWhiteSpace(track.Name) ? track.Kind.ToString() : track.Name;
            var nameText = new FormattedText(
                name,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10.5,
                new SolidColorBrush(Color.FromRgb(213, 210, 220)),
                1.25)
            {
                MaxTextWidth = 76,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            dc.DrawText(nameText, new Point(52, y + 15));

            DrawTrackState(dc, "M", track.Muted, 136, y + 15);
            DrawTrackState(dc, "S", track.Solo, 153, y + 15);
            DrawTrackState(dc, "L", track.Locked, 170, y + 15);
        }
    }

    private static string GetTrackCode(Track track, int index) => track.Kind switch
    {
        TrackKind.Video => $"V{index + 1}",
        TrackKind.Text => $"T{index + 1}",
        TrackKind.Overlay => $"O{index + 1}",
        TrackKind.Audio or TrackKind.Music or TrackKind.Voice => $"A{index + 1}",
        _ => $"{index + 1}",
    };

    private static void DrawTrackState(DrawingContext dc, string text, bool active, double x, double y)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            8.5,
            new SolidColorBrush(active ? Color.FromRgb(181, 146, 255) : Color.FromRgb(91, 88, 103)),
            1.25);
        dc.DrawText(formatted, new Point(x, y));
    }

    private void DrawTrack(DrawingContext dc, Track track, int trackIndex)
    {
        var yBase = _viewport.TrackIndexToY(trackIndex);
        var trackRect = new Rect(_viewport.TrackHeaderWidth, yBase,
            Math.Max(0, RenderSize.Width - _viewport.TrackHeaderWidth), _viewport.TrackHeight);
        var trackBackground = trackIndex % 2 == 0 ? TrackEvenBrush : TrackOddBrush;
        dc.DrawRectangle(trackBackground, TrackBorderPen, trackRect);

        var items = _sceneIndex.GetTrackItems(trackIndex);
        var firstItem = _sceneIndex.FindFirstPotentiallyVisibleItem(trackIndex, VisibleStartSeconds);
        for (var itemIndex = firstItem; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            if (item.TimelineStart.Seconds > VisibleEndSeconds) break;
            if (_isDraggingClip && _dragItem?.Id == item.Id && trackIndex != _dragTrackIndex)
                continue;

            var x = _viewport.GetClipX(item.TimelineStart);
            var width = _viewport.GetClipWidth(item.Duration);
            if (x + width < _viewport.TrackHeaderWidth || x > RenderSize.Width) continue;

            var rect = new Rect(x + 1, yBase + 4, Math.Max(2, width - 2), _viewport.TrackHeight - 8);
            var isSelected = IsItemSelected(item);
            var isAudioTrack = track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice;
            var fill = CreateClipBrush(item.Kind, isAudioTrack);
            var border = isSelected ? SelectedClipBorderPen : ClipBorderPen;
            dc.DrawRoundedRectangle(fill, border, rect, 4, 4);

            dc.DrawLine(
                ClipHighlightPen,
                new Point(rect.X + 4, rect.Y + 1),
                new Point(rect.Right - 4, rect.Y + 1));

            if (isAudioTrack && rect.Width > 18)
            {
                var peaks = item.MediaAssetId is { } waveformAssetId
                    ? AssetWaveformResolver?.Invoke(waveformAssetId)
                    : null;
                DrawAudioWaveform(dc, rect, peaks, trackIndex);
                DrawAudioVolumeLine(dc, rect, item.Volume, isSelected);
            }

            if (rect.Width > 34)
            {
                var label = ResolveClipLabel(item);
                var visibleRect = Rect.Intersect(rect, GetTrackContentRect(yBase));
                if (visibleRect.Width <= 10 || visibleRect.Height <= 10)
                    continue;

                var labelClip = new Rect(
                    visibleRect.X + 4,
                    visibleRect.Y + 4,
                    Math.Max(1, visibleRect.Width - 8),
                    Math.Max(1, visibleRect.Height - 8));
                var text = new FormattedText(
                    label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
                    9,
                    Brushes.White,
                    1.25)
                {
                    MaxTextWidth = Math.Max(1, labelClip.Width - 4),
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                dc.PushClip(new RectangleGeometry(labelClip));
                dc.DrawText(text, new Point(labelClip.X + 2, rect.Y + (isAudioTrack ? 5 : 7)));
                dc.Pop();
            }

            if (isSelected && item.AnimationChannels.Count > 0 && rect.Width > 40)
                DrawAnimationLanes(dc, rect, item);

            if (item.FadeInDuration.Seconds > 0)
            {
                var fadeWidth = Math.Min(_viewport.GetClipWidth(item.FadeInDuration), rect.Width);
                dc.DrawLine(FadePen,
                    new Point(rect.X, rect.Bottom - 2), new Point(rect.X + fadeWidth, rect.Y + 2));
            }
            if (item.FadeOutDuration.Seconds > 0)
            {
                var fadeWidth = Math.Min(_viewport.GetClipWidth(item.FadeOutDuration), rect.Width);
                dc.DrawLine(FadePen,
                    new Point(rect.Right - fadeWidth, rect.Y + 2), new Point(rect.Right, rect.Bottom - 2));
            }
        }
    }

    private void DrawAnimationLanes(DrawingContext dc, Rect rect, TimelineItem item)
    {
        var channelCount = Math.Min(3, item.AnimationChannels.Count);
        if (channelCount == 0) return;
        var laneHeight = Math.Min(7, Math.Max(4, (rect.Height - 8) / channelCount));
        var startY = rect.Bottom - laneHeight * channelCount - 3;
        for (var laneIndex = 0; laneIndex < channelCount; laneIndex++)
        {
            var y = startY + laneIndex * laneHeight + laneHeight / 2;
            dc.DrawLine(AnimationLanePen, new Point(rect.X + 4, y), new Point(rect.Right - 4, y));
            foreach (var keyframe in item.AnimationChannels[laneIndex].Keyframes)
            {
                var x = _viewport.TimeToPixel(item.TimelineStart.Add(keyframe.Time));
                if (x < rect.X + 2 || x > rect.Right - 2) continue;
                var diamond = new StreamGeometry();
                using (var context = diamond.Open())
                {
                    context.BeginFigure(new Point(x, y - 3), true, true);
                    context.LineTo(new Point(x + 3, y), true, false);
                    context.LineTo(new Point(x, y + 3), true, false);
                    context.LineTo(new Point(x - 3, y), true, false);
                }
                diamond.Freeze();
                dc.DrawGeometry(AnimationKeyBrush, null, diamond);
            }
        }
    }

    private static Brush CreateClipBrush(ItemKind kind, bool audioTrack) =>
        ClipBrushes.TryGetValue((kind, audioTrack), out var brush)
            ? brush
            : ClipBrushes[(ItemKind.Clip, false)];

    private string ResolveClipLabel(TimelineItem item)
    {
        if (item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.TextContent))
            return item.TextContent;
        if (item.MediaAssetId is { } assetId)
            return AssetNameResolver?.Invoke(assetId) ?? item.Kind.ToString();
        return item.Kind.ToString();
    }

    private void DrawAudioWaveform(
        DrawingContext dc,
        Rect rect,
        IReadOnlyList<float>? peaks,
        int fallbackSeed)
    {
        var key = new WaveformDrawingKey(
            peaks == null ? 0 : RuntimeHelpers.GetHashCode(peaks),
            peaks?.Count ?? 0,
            fallbackSeed);
        if (!_waveformDrawingCache.TryGetValue(key, out var drawing))
        {
            drawing = BuildWaveformDrawing(peaks, fallbackSeed);
            if (_waveformDrawingCache.Count >= 512) _waveformDrawingCache.Clear();
            _waveformDrawingCache[key] = drawing;
        }

        dc.DrawImage(
            drawing,
            new Rect(rect.X + 4, rect.Y, Math.Max(1, rect.Width - 8), rect.Height));
    }

    private static DrawingImage BuildWaveformDrawing(IReadOnlyList<float>? peaks, int fallbackSeed)
    {
        const int samples = 180;
        const double width = 180;
        const double height = 100;
        const double center = height * 0.58;
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            for (var index = 0; index < samples; index++)
            {
                var progress = index / (double)(samples - 1);
                var x = progress * width;
                var amplitudeValue = peaks is { Count: > 0 }
                    ? peaks[Math.Clamp((int)Math.Round(progress * (peaks.Count - 1)), 0, peaks.Count - 1)]
                    : (float)(0.18 + 0.72 * Math.Abs(Math.Sin((index + fallbackSeed * 3) * 0.63)));
                var amplitude = Math.Clamp(amplitudeValue, 0, 1) * height * 0.34;
                context.DrawLine(WaveformPen, new Point(x, center - amplitude), new Point(x, center + amplitude));
            }
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static void DrawAudioVolumeLine(DrawingContext dc, Rect rect, double gain, bool selected)
    {
        var y = GainToVolumeLineY(rect, gain);
        var pen = new Pen(
            new SolidColorBrush(selected ? Color.FromRgb(236, 210, 255) : Color.FromArgb(190, 198, 174, 255)),
            selected ? 1.6 : 1.0);
        dc.DrawLine(pen, new Point(rect.X + 3, y), new Point(rect.Right - 3, y));
        if (selected)
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(222, 193, 255)), null, new Point(rect.Right - 8, y), 3, 3);
    }

    private static double GainToVolumeLineY(Rect rect, double gain)
    {
        var db = gain <= 0.00001 ? -60 : 20 * Math.Log10(gain);
        var normalized = Math.Clamp((db + 60) / 66, 0, 1);
        return rect.Bottom - 4 - (normalized * Math.Max(1, rect.Height - 8));
    }

    private static double VolumeLineYToGain(Rect rect, double y)
    {
        var normalized = Math.Clamp((rect.Bottom - 4 - y) / Math.Max(1, rect.Height - 8), 0, 1);
        var db = -60 + (normalized * 66);
        return db <= -59.9 ? 0 : Math.Pow(10, db / 20);
    }

    private void DrawTransitionHandles(DrawingContext dc)
    {
        if (Sequence == null) return;

        foreach (var selection in GetTransitionSlots())
        {
            var boundaryX = _viewport.TimeToPixel(selection.LeftItem.TimelineEnd);
            if (boundaryX < _viewport.TrackHeaderWidth || boundaryX > RenderSize.Width) continue;

            var yBase = _viewport.TrackIndexToY(selection.TrackIndex);
            var center = new Point(boundaryX, yBase + _viewport.TrackHeight / 2);
            var selected = _selectedTransition != null
                && _selectedTransition.LeftItemId == selection.LeftItem.Id
                && _selectedTransition.RightItemId == selection.RightItem.Id;
            var existing = selection.Transition != null;
            var fill = selected
                ? new SolidColorBrush(Color.FromRgb(167, 139, 250))
                : existing
                    ? new SolidColorBrush(Color.FromRgb(139, 92, 246))
                    : new SolidColorBrush(Color.FromRgb(22, 23, 32));
            var stroke = selected
                ? new Pen(new SolidColorBrush(Color.FromRgb(216, 200, 255)), 2)
                : new Pen(new SolidColorBrush(existing ? Color.FromRgb(180, 150, 255) : Color.FromRgb(91, 87, 105)), 1.25);

            var diamond = new StreamGeometry();
            using (var context = diamond.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - 11), true, true);
                context.LineTo(new Point(center.X + 11, center.Y), true, false);
                context.LineTo(new Point(center.X, center.Y + 11), true, false);
                context.LineTo(new Point(center.X - 11, center.Y), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(fill, stroke, diamond);

            var icon = new FormattedText(
                existing ? "T" : "+",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9,
                Brushes.White,
                1.25);
            dc.DrawText(icon, new Point(center.X - icon.Width / 2, center.Y - icon.Height / 2));
        }
    }

    private void DrawMarkers(DrawingContext dc)
    {
        if (Sequence == null) return;

        foreach (var marker in Sequence.Markers)
        {
            var markerBrush = ParseTimelineBrush(marker.Color, Color.FromRgb(167, 139, 250));
            var markerPen = new Pen(markerBrush, 1);
            var x = _viewport.TimeToPixel(marker.Time);
            if (x < _viewport.TrackHeaderWidth || x > RenderSize.Width) continue;

            if (marker.Duration.Seconds > 0)
            {
                var endX = _viewport.TimeToPixel(marker.Time.Add(marker.Duration));
                var durationRect = new Rect(x, 1, Math.Max(2, endX - x), Math.Max(5, _viewport.RulerHeight - 3));
                var durationBrush = new SolidColorBrush(Color.FromArgb(45, markerBrush.Color.R, markerBrush.Color.G, markerBrush.Color.B));
                dc.DrawRoundedRectangle(durationBrush, null, durationRect, 2, 2);
            }
            dc.DrawLine(markerPen, new Point(x, _viewport.RulerHeight - 6), new Point(x, RenderSize.Height));
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(new Point(x - 5, 30), true, true);
                context.LineTo(new Point(x + 5, 30), true, false);
                context.LineTo(new Point(x, 37), true, false);
            }
            triangle.Freeze();
            dc.DrawGeometry(markerBrush, null, triangle);

            if (!string.IsNullOrWhiteSpace(marker.Label))
            {
                var label = new FormattedText(
                    marker.Label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    9,
                    markerBrush,
                    1.25);
                dc.DrawText(label, new Point(x + 7, 31));
            }
        }
    }

    private static SolidColorBrush ParseTimelineBrush(string? value, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
                return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
        }
        return new SolidColorBrush(fallback);
    }

    private void DrawDraggedClipGhost(DrawingContext dc)
    {
        if (!_isDraggingClip || _dragItem == null || _dragTrackIndex == _selectedTrackIndex) return;
        if (Sequence == null || _dragTrackIndex < 0 || _dragTrackIndex >= Sequence.Tracks.Count) return;

        var x = _viewport.GetClipX(_dragItem.TimelineStart);
        var width = _viewport.GetClipWidth(_dragItem.Duration);
        var y = _viewport.TrackIndexToY(_dragTrackIndex) + 3;
        var rect = new Rect(x, y, width, _viewport.TrackHeight - 6);
        var fill = new SolidColorBrush(Color.FromArgb(135, 139, 92, 246));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(180, 150, 255)), 2);
        dc.DrawRoundedRectangle(fill, border, rect, 3, 3);
    }

    private void DrawPlayhead(DrawingContext dc)
    {
        var x = _viewport.TimeToPixel(_playheadTime);
        if (x < _viewport.TrackHeaderWidth || x > RenderSize.Width) return;

        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(192, 132, 252)), 1.5), new Point(x, 0), new Point(x, RenderSize.Height));

        var ft = new FormattedText($"{_playheadTime.Seconds:F1}s",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, new SolidColorBrush(Color.FromRgb(192, 132, 252)), 1.25);
        var labelX = Math.Min(Math.Max(x + 6, _viewport.TrackHeaderWidth + 4), Math.Max(_viewport.TrackHeaderWidth + 4, RenderSize.Width - ft.Width - 4));
        var labelRect = new Rect(labelX - 3, 2, ft.Width + 6, ft.Height + 3);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(13, 11, 19)), new Pen(new SolidColorBrush(Color.FromRgb(192, 132, 252)), 0.75), labelRect, 3, 3);
        dc.DrawText(ft, new Point(labelX, 3));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        using var measurement = _telemetry.DetailedEnabled ? _telemetry.MeasureUiInput("timeline.wheel") : null;
        SyncViewportSize();
        if (Sequence == null) return;

        var pos = e.GetPosition(this);
        if (!GetContentClipRect(includeRuler: false).Contains(pos))
        {
            _viewport.Pan(0, e.Delta);
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _viewport.Zoom(factor, e.GetPosition(this).X);
        ZoomScaleChanged?.Invoke(this, _viewport.ZoomScale);
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        using var measurement = _telemetry.DetailedEnabled ? _telemetry.MeasureUiInput("timeline.mouse_down") : null;
        Focus();
        var pos = e.GetPosition(this);
        _lastMousePos = pos;

        if (e.ChangedButton == MouseButton.Right)
        {
            var headerTrackIndex = pos.X <= _viewport.TrackHeaderWidth
                ? GetTrackIndexAtY(pos.Y)
                : -1;
            if (headerTrackIndex >= 0)
            {
                _selectedItem = null;
                _selectedItemIds.Clear();
                _selectedTransition = null;
                _selectedTrackIndex = headerTrackIndex;
                ClipSelected?.Invoke(this, null);
                TransitionSelected?.Invoke(this, null);
                TrackHeaderContextRequested?.Invoke(this, headerTrackIndex);
                OpenContextMenu(TrackHeaderContextMenu);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var contextTransition = HitTestTransition(pos);
            if (contextTransition != null)
            {
                _selectedItem = null;
                _selectedItemIds.Clear();
                _selectedTrackIndex = contextTransition.TrackIndex;
                _selectedTransition = contextTransition.Transition;
                TransitionSelected?.Invoke(this, contextTransition);
                ClipSelected?.Invoke(this, null);
                OpenContextMenu(ClipContextMenu);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var (contextItem, contextTrackIndex, _) = HitTest(pos);
            _selectedItemIds.Clear();
            if (contextItem != null) _selectedItemIds.Add(contextItem.Id);
            _selectedItem = contextItem;
            _selectedTransition = null;
            _selectedTrackIndex = contextTrackIndex;
            ClipSelected?.Invoke(this, contextItem);
            TransitionSelected?.Invoke(this, null);
            OpenContextMenu(ClipContextMenu);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            CaptureMouse();
            _isPanning = true;
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        CaptureMouse();
        var clickedMarker = pos.X > _viewport.TrackHeaderWidth ? HitTestMarker(pos.X) : null;
        if (clickedMarker != null && pos.Y >= _viewport.RulerHeight)
        {
            if (e.ClickCount >= 2)
            {
                ReleaseMouseCapture();
                MarkerEditRequested?.Invoke(this, clickedMarker);
                e.Handled = true;
                return;
            }

            SeekToMarker(clickedMarker);
            e.Handled = true;
            return;
        }

        if (pos.Y < _viewport.RulerHeight && pos.X > _viewport.TrackHeaderWidth)
        {
            if (e.ClickCount >= 2 && clickedMarker != null)
            {
                ReleaseMouseCapture();
                MarkerEditRequested?.Invoke(this, clickedMarker);
                e.Handled = true;
                return;
            }
            PlayheadTime = clickedMarker?.Time ?? _viewport.PixelToTime(ClampContentX(pos.X));
            _dragFromPlayhead = true;
            _selectedItem = null;
            _selectedItemIds.Clear();
            _selectedTransition = null;
            _selectedTrackIndex = -1;
            ClipSelected?.Invoke(this, null);
            TransitionSelected?.Invoke(this, null);
            PlayheadMoved?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var transition = HitTestTransition(pos);
        if (transition != null)
        {
            _selectedItem = null;
            _selectedItemIds.Clear();
            _selectedTransition = transition.Transition;
            _selectedTrackIndex = transition.TrackIndex;
            ClipSelected?.Invoke(this, null);
            TransitionSelected?.Invoke(this, transition);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var (item, trackIdx, _) = HitTest(pos);
        if (item == null)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                BeginBoxSelection(pos);
                e.Handled = true;
                return;
            }

            _selectedItem = null;
            _selectedItemIds.Clear();
            SelectionChanged?.Invoke(this, []);
            _selectedTransition = null;
            _selectedTrackIndex = -1;
            ClipSelected?.Invoke(this, null);
            TransitionSelected?.Invoke(this, null);
            if (pos.X > _viewport.TrackHeaderWidth)
            {
                PlayheadTime = _viewport.PixelToTime(ClampContentX(pos.X));
                PlayheadMoved?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        SelectPointerItem(item, trackIdx, Keyboard.Modifiers);
        if (_selectedItem == null)
        {
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        _dragTrackIndex = trackIdx;

        var track = Sequence!.Tracks[trackIdx];
        if (track.Locked || item.Locked)
        {
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _dragItem = item;
        _dragStartTime = item.TimelineStart;
        _dragOrigDuration = item.Duration;
        _dragOrigSourceStart = item.SourceStart;
        _dragOrigVolume = item.Volume;
        _dragOriginMouseX = pos.X;
        CaptureGroupDragSnapshots();

        var clipX = _viewport.GetClipX(item.TimelineStart);
        var clipW = _viewport.GetClipWidth(item.Duration);
        var distFromLeft = pos.X - clipX;
        var distFromRight = clipX + clipW - pos.X;
        var itemRect = new Rect(clipX + 1, _viewport.TrackIndexToY(trackIdx) + 4, Math.Max(2, clipW - 2), _viewport.TrackHeight - 8);
        var isAudioTrack = track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice;
        var nearVolumeLine = isAudioTrack && Math.Abs(pos.Y - GainToVolumeLineY(itemRect, item.Volume)) <= 7;

        if (nearVolumeLine)
        {
            _isTrimming = true;
            _dragMode = DragMode.Volume;
            Cursor = Cursors.SizeNS;
        }
        else if (distFromLeft < TrimEdgeThreshold && item.Duration.Seconds > 0.2)
        {
            _isTrimming = true;
            _dragMode = DragMode.TrimLeft;
            Cursor = Cursors.SizeWE;
        }
        else if (distFromRight < TrimEdgeThreshold && item.Duration.Seconds > 0.2)
        {
            _isTrimming = true;
            _dragMode = DragMode.TrimRight;
            Cursor = Cursors.SizeWE;
        }
        else
        {
            _isDraggingClip = true;
            _dragMode = DragMode.Move;
            Cursor = Cursors.SizeAll;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        using var measurement = _telemetry.DetailedEnabled ? _telemetry.MeasureUiInput("timeline.mouse_move") : null;
        var pos = e.GetPosition(this);

        if (UpdateBoxSelection(pos))
        {
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _viewport.Pan(pos.X - _lastMousePos.X, pos.Y - _lastMousePos.Y);
            _lastMousePos = pos;
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_dragFromPlayhead)
        {
            PlayheadTime = _viewport.PixelToTime(ClampContentX(pos.X));
            PlayheadMoved?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_isTrimming && _dragItem != null)
        {
            if (UpdateGroupTrimPreview(pos))
            {
                e.Handled = true;
                return;
            }

            if (_dragMode == DragMode.Volume)
            {
                var clipX = _viewport.GetClipX(_dragItem.TimelineStart);
                var clipWidth = _viewport.GetClipWidth(_dragItem.Duration);
                var clipRect = new Rect(
                    clipX + 1,
                    _viewport.TrackIndexToY(_selectedTrackIndex) + 4,
                    Math.Max(2, clipWidth - 2),
                    _viewport.TrackHeight - 8);
                _dragItem.Volume = VolumeLineYToGain(clipRect, pos.Y);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var totalDeltaSeconds = (pos.X - _dragOriginMouseX) / _viewport.PixelsPerSecond;
            const double minimumDurationSeconds = 0.1;

            if (_dragMode == DragMode.TrimLeft)
            {
                var candidateStartSeconds = Math.Clamp(
                    _dragStartTime.Seconds + totalDeltaSeconds,
                    0,
                    _dragStartTime.Seconds + _dragOrigDuration.Seconds - minimumDurationSeconds);
                var candidateStart = SnapTime(MediaTime.FromSeconds(candidateStartSeconds), _dragItem);
                candidateStartSeconds = Math.Clamp(
                    candidateStart.Seconds,
                    0,
                    _dragStartTime.Seconds + _dragOrigDuration.Seconds - minimumDurationSeconds);

                var actualDelta = candidateStartSeconds - _dragStartTime.Seconds;
                _dragItem.TimelineStart = MediaTime.FromSeconds(candidateStartSeconds);
                _dragItem.Duration = MediaTime.FromSeconds(_dragOrigDuration.Seconds - actualDelta);
                _dragItem.SourceStart = MediaTime.FromSeconds(
                    Math.Max(0, _dragOrigSourceStart.Seconds + actualDelta * Math.Max(0.1, _dragItem.Speed)));
            }
            else
            {
                var candidateEndSeconds = _dragStartTime.Seconds + _dragOrigDuration.Seconds + totalDeltaSeconds;
                var candidateEnd = SnapTime(MediaTime.FromSeconds(candidateEndSeconds), _dragItem);
                var newDurationSeconds = Math.Max(
                    minimumDurationSeconds,
                    candidateEnd.Seconds - _dragStartTime.Seconds);
                _dragItem.TimelineStart = _dragStartTime;
                _dragItem.Duration = MediaTime.FromSeconds(newDurationSeconds);
                _dragItem.SourceStart = _dragOrigSourceStart;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isDraggingClip && _dragItem != null)
        {
            if (UpdateGroupMovePreview(pos))
            {
                e.Handled = true;
                return;
            }

            var pixelsPerSecond = _viewport.PixelsPerSecond;
            if (!double.IsFinite(pos.X) || !double.IsFinite(_dragOriginMouseX) ||
                !double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0)
                return;

            var totalDeltaSeconds = (pos.X - _dragOriginMouseX) / pixelsPerSecond;
            var candidateSeconds = _dragStartTime.Seconds + totalDeltaSeconds;
            if (!double.IsFinite(candidateSeconds)) return;

            var candidateStart = MediaTime.FromSeconds(Math.Max(0, candidateSeconds));
            _dragItem.TimelineStart = SnapMoveStart(candidateStart, _dragItem.Duration, _dragItem);

            var candidateTrackIndex = GetTrackIndexAtY(pos.Y);
            if (candidateTrackIndex >= 0)
            {
                var candidateTrack = Sequence!.Tracks[candidateTrackIndex];
                if (!candidateTrack.Locked && TrackCompatibility.IsItemCompatibleWithTrack(_dragItem.Kind, candidateTrack.Kind))
                    _dragTrackIndex = candidateTrackIndex;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var (hoverItem, _, _) = HitTest(pos);
        if (pos.X > _viewport.TrackHeaderWidth && HitTestMarker(pos.X) != null)
        {
            Cursor = Cursors.Hand;
        }
        else if (HitTestTransition(pos) != null)
        {
            Cursor = Cursors.Hand;
        }
        else if (hoverItem != null)
        {
            var clipX = _viewport.GetClipX(hoverItem.TimelineStart);
            var clipW = _viewport.GetClipWidth(hoverItem.Duration);
            var distFromLeft = pos.X - clipX;
            var distFromRight = clipX + clipW - pos.X;
            Cursor = (distFromLeft < TrimEdgeThreshold || distFromRight < TrimEdgeThreshold)
                ? Cursors.SizeWE : Cursors.Arrow;
        }
        else
        {
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        using var measurement = _telemetry.DetailedEnabled ? _telemetry.MeasureUiInput("timeline.mouse_up") : null;
        if (FinishBoxSelection())
        {
            Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        if (_isPanning)
        {
            _isPanning = false;
            e.Handled = true;
        }

        if (_isDraggingClip && _dragItem != null)
        {
            if (FinishGroupMove())
            {
                e.Handled = true;
                return;
            }

            var item = _dragItem;
            var newStart = item.TimelineStart;
            var sourceTrackIndex = _selectedTrackIndex;
            var targetTrackIndex = _dragTrackIndex;

            item.TimelineStart = _dragStartTime;
            _isDraggingClip = false;
            _dragMode = DragMode.None;
            _dragItem = null;

            if (double.IsFinite(newStart.Seconds) && targetTrackIndex >= 0)
            {
                ClipMoveRequested?.Invoke(this, new ClipMoveRequestedEventArgs(
                    item,
                    sourceTrackIndex,
                    targetTrackIndex,
                    newStart));
            }

            e.Handled = true;
        }

        if (_isTrimming && _dragItem != null)
        {
            if (FinishGroupTrim())
            {
                e.Handled = true;
                return;
            }

            var item = _dragItem;
            if (_dragMode == DragMode.Volume)
            {
                var newVolume = item.Volume;
                item.Volume = _dragOrigVolume;
                _isTrimming = false;
                ClipVolumeRequested?.Invoke(this, new ClipVolumeRequestedEventArgs(item, newVolume));
                e.Handled = true;
                _dragMode = DragMode.None;
                _dragItem = null;
                return;
            }

            var newStart = item.TimelineStart;
            var newDuration = item.Duration;
            var newSourceStart = item.SourceStart;
            item.TimelineStart = _dragStartTime;
            item.Duration = _dragOrigDuration;
            item.SourceStart = _dragOrigSourceStart;
            _isTrimming = false;
            ClipTrimRequested?.Invoke(this, new ClipTrimRequestedEventArgs(
                item,
                _selectedTrackIndex,
                newStart,
                newDuration,
                newSourceStart));
            e.Handled = true;
        }

        if (_dragFromPlayhead)
        {
            _dragFromPlayhead = false;
            e.Handled = true;
        }

        _dragMode = DragMode.None;
        _dragItem = null;
    }

    private void OpenContextMenu(ContextMenu? menu)
    {
        if (menu == null) return;
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        using var measurement = _telemetry.DetailedEnabled ? _telemetry.MeasureUiInput("timeline.key_down") : null;
        if (e.Key == Key.Space)
        {
            PlayPauseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        if (e.Key == Key.Delete && _selectedItem != null)
        {
            DeleteSelectedClipRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private MediaTime SnapMoveStart(MediaTime candidateStart, MediaTime duration, TimelineItem exclude)
    {
        if (!SnapEnabled) return candidateStart;

        var snappedStart = SnapTime(candidateStart, exclude);
        var candidateEnd = candidateStart.Add(duration);
        var snappedEnd = SnapTime(candidateEnd, exclude);
        var startAdjustment = snappedStart.Seconds - candidateStart.Seconds;
        var endAdjustment = snappedEnd.Seconds - candidateEnd.Seconds;
        var adjustment = Math.Abs(startAdjustment) <= Math.Abs(endAdjustment)
            ? startAdjustment
            : endAdjustment;

        return MediaTime.FromSeconds(Math.Max(0, candidateStart.Seconds + adjustment));
    }

    private MediaTime SnapTime(MediaTime candidate, TimelineItem exclude)
    {
        if (!SnapEnabled || Sequence == null) return candidate;
        EnsureSceneIndex();
        var thresholdSeconds = 10.0 / Math.Max(1, _viewport.PixelsPerSecond);
        return MediaTime.FromSeconds(
            _sceneIndex.FindNearestSnapPoint(candidate.Seconds, thresholdSeconds, exclude.Id));
    }

    private int GetTrackIndexAtY(double y)
    {
        if (Sequence == null || y < _viewport.RulerHeight) return -1;
        var index = _viewport.YToTrackIndex(y);
        return index >= 0 && index < Sequence.Tracks.Count ? index : -1;
    }

    private Marker? HitTestMarker(double x)
    {
        if (Sequence == null) return null;
        EnsureSceneIndex();
        const double thresholdPixels = 7;
        var time = _viewport.PixelToTime(x).Seconds;
        return _sceneIndex.FindNearestMarker(
            time,
            thresholdPixels / Math.Max(1, _viewport.PixelsPerSecond));
    }

    private void SeekToMarker(Marker marker)
    {
        PlayheadTime = marker.Time;
        _dragFromPlayhead = false;
        _selectedItem = null;
        _selectedItemIds.Clear();
        _selectedTransition = null;
        _selectedTrackIndex = -1;
        ClipSelected?.Invoke(this, null);
        TransitionSelected?.Invoke(this, null);
        PlayheadMoved?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private (TimelineItem? item, int trackIndex, int itemIndex) HitTest(Point pos)
    {
        if (Sequence == null) return (null, -1, -1);
        if (!GetContentClipRect(includeRuler: false).Contains(pos)) return (null, -1, -1);
        EnsureSceneIndex();
        var trackIndex = GetTrackIndexAtY(pos.Y);
        if (trackIndex < 0) return (null, -1, -1);
        var time = _viewport.PixelToTime(pos.X).Seconds;
        var item = _sceneIndex.HitTestItem(trackIndex, time);
        return item == null ? (null, -1, -1) : (item, trackIndex, 0);
    }

    private TransitionSelection? HitTestTransition(Point pos)
    {
        if (Sequence == null) return null;
        if (!GetContentClipRect(includeRuler: false).Contains(pos)) return null;
        foreach (var selection in GetTransitionSlots())
        {
            var boundaryX = _viewport.TimeToPixel(selection.LeftItem.TimelineEnd);
            var yBase = _viewport.TrackIndexToY(selection.TrackIndex);
            var rect = new Rect(boundaryX - 14, yBase + _viewport.TrackHeight / 2 - 14, 28, 28);
            if (rect.Contains(pos))
                return selection;
        }

        return null;
    }

    private IReadOnlyList<TransitionSelection> GetTransitionSlots()
    {
        if (Sequence == null) return [];
        EnsureSceneIndex();
        return _sceneIndex.TransitionSlots;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _viewport.ViewportWidth = availableSize.Width;
        _viewport.ViewportHeight = availableSize.Height;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport.ViewportWidth = finalSize.Width;
        _viewport.ViewportHeight = finalSize.Height;
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        return finalSize;
    }

    private void SyncViewportSize()
    {
        _viewport.ViewportWidth = Math.Max(0, RenderSize.Width);
        _viewport.ViewportHeight = Math.Max(0, RenderSize.Height);
    }

    private Rect GetContentClipRect(bool includeRuler)
    {
        var y = includeRuler ? 0 : _viewport.RulerHeight;
        return new Rect(
            _viewport.TrackHeaderWidth,
            y,
            Math.Max(0, RenderSize.Width - _viewport.TrackHeaderWidth),
            Math.Max(0, RenderSize.Height - y));
    }

    private Rect GetTrackContentRect(double yBase) =>
        new(
            _viewport.TrackHeaderWidth,
            yBase,
            Math.Max(0, RenderSize.Width - _viewport.TrackHeaderWidth),
            _viewport.TrackHeight);

    private double ClampContentX(double x) =>
        Math.Clamp(x, _viewport.TrackHeaderWidth, Math.Max(_viewport.TrackHeaderWidth, RenderSize.Width));

    private static Dictionary<(ItemKind Kind, bool Audio), Brush> BuildClipBrushes()
    {
        var result = new Dictionary<(ItemKind, bool), Brush>();
        foreach (var kind in Enum.GetValues<ItemKind>())
        {
            result[(kind, true)] = CreateFrozenGradient(Color.FromRgb(54, 42, 87), Color.FromRgb(32, 27, 54));
            var colors = kind switch
            {
                ItemKind.Text => (Color.FromRgb(113, 52, 166), Color.FromRgb(71, 34, 111)),
                ItemKind.Image => (Color.FromRgb(82, 64, 132), Color.FromRgb(46, 38, 76)),
                ItemKind.Sticker => (Color.FromRgb(104, 73, 154), Color.FromRgb(61, 45, 91)),
                ItemKind.AdjustmentLayer => (Color.FromRgb(83, 62, 119), Color.FromRgb(48, 38, 69)),
                _ => (Color.FromRgb(65, 43, 105), Color.FromRgb(35, 27, 59)),
            };
            result[(kind, false)] = CreateFrozenGradient(colors.Item1, colors.Item2);
        }
        return result;
    }

    private static Brush CreateFrozenGradient(Color start, Color end) =>
        Freeze(new LinearGradientBrush(start, end, 0));

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }

    private readonly record struct WaveformDrawingKey(int SourceIdentity, int PeakCount, int FallbackSeed);
}

public sealed record TransitionSelection(Transition? Transition, TimelineItem LeftItem, TimelineItem RightItem, int TrackIndex);

public sealed class ClipMoveRequestedEventArgs : EventArgs
{
    public ClipMoveRequestedEventArgs(TimelineItem item, int sourceTrackIndex, int targetTrackIndex, MediaTime newStart)
    {
        Item = item;
        SourceTrackIndex = sourceTrackIndex;
        TargetTrackIndex = targetTrackIndex;
        NewStart = newStart;
    }

    public TimelineItem Item { get; }
    public int SourceTrackIndex { get; }
    public int TargetTrackIndex { get; }
    public MediaTime NewStart { get; }
}

public sealed class ClipVolumeRequestedEventArgs : EventArgs
{
    public ClipVolumeRequestedEventArgs(TimelineItem item, double newVolume)
    {
        Item = item;
        NewVolume = newVolume;
    }

    public TimelineItem Item { get; }
    public double NewVolume { get; }
}

public sealed class ClipTrimRequestedEventArgs : EventArgs
{
    public ClipTrimRequestedEventArgs(
        TimelineItem item,
        int trackIndex,
        MediaTime newStart,
        MediaTime newDuration,
        MediaTime newSourceStart)
    {
        Item = item;
        TrackIndex = trackIndex;
        NewStart = newStart;
        NewDuration = newDuration;
        NewSourceStart = newSourceStart;
    }

    public TimelineItem Item { get; }
    public int TrackIndex { get; }
    public MediaTime NewStart { get; }
    public MediaTime NewDuration { get; }
    public MediaTime NewSourceStart { get; }
}
