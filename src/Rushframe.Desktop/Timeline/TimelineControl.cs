using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Desktop.Viewport;
using Rushframe.Domain;

namespace Rushframe.Desktop.Timeline;

public sealed class TimelineControl : FrameworkElement
{
    private readonly TimelineViewport _viewport = new();

    public Sequence? Sequence
    {
        get => _sequence;
        set
        {
            _sequence = value;
            ClearSelection();
            InvalidateVisual();
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
    private double _dragOriginMouseX;
    private DragMode _dragMode;
    private MediaTime _playheadTime;
    private TimelineItem? _selectedItem;
    private Transition? _selectedTransition;
    private int _selectedTrackIndex;
    private bool _dragFromPlayhead;

    private enum DragMode { None, Move, TrimLeft, TrimRight }

    public MediaTime PlayheadTime { get => _playheadTime; set { _playheadTime = value; InvalidateVisual(); } }
    public TimelineItem? SelectedItem => _selectedItem;
    public Transition? SelectedTransition => _selectedTransition;
    public int SelectedTrackIndex => _selectedTrackIndex;
    public bool SnapEnabled { get; set; } = true;
    public Func<MediaAssetId, string?>? AssetNameResolver { get; set; }
    public ContextMenu? ClipContextMenu { get; set; }
    public ContextMenu? TrackHeaderContextMenu { get; set; }

    public event EventHandler<TimelineItem?>? ClipSelected;
    public event EventHandler<TransitionSelection?>? TransitionSelected;
    public event EventHandler? PlayheadMoved;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler<int>? TrackHeaderContextRequested;
    public event EventHandler? DeleteSelectedClipRequested;
    public event EventHandler<ClipMoveRequestedEventArgs>? ClipMoveRequested;
    public event EventHandler<ClipTrimRequestedEventArgs>? ClipTrimRequested;
    public event EventHandler<double>? ZoomScaleChanged;

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
        _viewport.RulerHeight = 34;
    }

    public void ClearSelection()
    {
        var hadClip = _selectedItem != null;
        var hadTransition = _selectedTransition != null;
        _selectedItem = null;
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
        }
    }

    public void ScrollBy(double deltaPixels)
    {
        _viewport.HorizontalOffset = Math.Clamp(
            _viewport.HorizontalOffset + deltaPixels,
            0,
            _viewport.GetMaxHorizontalOffset());
        InvalidateVisual();
    }

    public void SetZoomScale(double scale)
    {
        SyncViewportSize();
        _viewport.SetZoomScale(scale, Math.Max(_viewport.TrackHeaderWidth, RenderSize.Width / 2));
        InvalidateVisual();
    }

    public void SelectTransition(Transition? transition, int trackIndex)
    {
        _selectedItem = null;
        _selectedTransition = transition;
        _selectedTrackIndex = trackIndex;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        SyncViewportSize();
        DrawBackground(dc);
        if (Sequence == null) return;
        UpdateSequenceDuration();

        DrawRuler(dc);
        DrawTrackHeaders(dc);

        var contentClip = GetContentClipRect(includeRuler: true);
        if (contentClip.Width > 0 && contentClip.Height > 0)
        {
            dc.PushClip(new RectangleGeometry(contentClip));
            for (int i = 0; i < Sequence.Tracks.Count; i++)
                DrawTrack(dc, Sequence.Tracks[i], i);

            DrawTransitionHandles(dc);
            DrawMarkers(dc);
            DrawDraggedClipGhost(dc);
            DrawPlayhead(dc);
            dc.Pop();
        }
    }

    private void UpdateSequenceDuration()
    {
        double max = 0;
        if (Sequence != null)
        {
            foreach (var track in Sequence.Tracks)
            foreach (var item in track.Items)
            {
                var end = item.TimelineStart.Seconds + item.Duration.Seconds;
                if (end > max) max = end;
            }
        }
        _viewport.SequenceDurationSeconds = Math.Max(max + 10, 60);
        _viewport.TrackCount = Sequence?.Tracks.Count ?? 0;
        _viewport.HorizontalOffset = Math.Clamp(_viewport.HorizontalOffset, 0, _viewport.GetMaxHorizontalOffset());
        _viewport.VerticalOffset = Math.Clamp(_viewport.VerticalOffset, 0, _viewport.GetMaxVerticalOffset());
    }

    private void DrawBackground(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 13, 18)), null, new Rect(RenderSize));
    }

    private void DrawRuler(DrawingContext dc)
    {
        var headerRect = new Rect(0, 0, _viewport.TrackHeaderWidth, _viewport.RulerHeight);
        var rulerRect = new Rect(_viewport.TrackHeaderWidth, 0,
            Math.Max(0, RenderSize.Width - _viewport.TrackHeaderWidth), _viewport.RulerHeight);
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(38, 40, 50)), 0.75);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13, 15, 21)), borderPen, headerRect);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 17, 23)), borderPen, rulerRect);

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

            var labelWouldCollideWithPlayhead = Math.Abs(s - _playheadTime.Seconds) < 0.05;
            if (!labelWouldCollideWithPlayhead)
            {
                var ft = new FormattedText(
                    FormatRulerTime(s),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Cascadia Mono"),
                    9,
                    new SolidColorBrush(Color.FromRgb(128, 124, 139)),
                    1.25);
                dc.DrawText(ft, new Point(x + 5, 9));
            }

            dc.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(70, 90, 86, 103)), 0.6),
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
        for (int i = 0; i < (Sequence?.Tracks.Count ?? 0); i++)
        {
            var track = Sequence!.Tracks[i];
            var y = _viewport.TrackIndexToY(i);
            var rect = new Rect(0, y, _viewport.TrackHeaderWidth, _viewport.TrackHeight);
            var selected = i == _selectedTrackIndex;
            var background = selected
                ? Color.FromRgb(25, 21, 38)
                : i % 2 == 0 ? Color.FromRgb(17, 19, 26) : Color.FromRgb(15, 17, 23);
            dc.DrawRectangle(new SolidColorBrush(background), new Pen(new SolidColorBrush(Color.FromRgb(37, 39, 49)), 0.65), rect);

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
        var trackBackground = trackIndex % 2 == 0
            ? Color.FromRgb(13, 15, 21)
            : Color.FromRgb(11, 13, 18);
        dc.DrawRectangle(new SolidColorBrush(trackBackground), new Pen(new SolidColorBrush(Color.FromRgb(34, 36, 45)), 0.6), trackRect);

        foreach (var item in track.Items)
        {
            if (_isDraggingClip && _dragItem?.Id == item.Id && trackIndex != _dragTrackIndex)
                continue;

            var x = _viewport.GetClipX(item.TimelineStart);
            var width = _viewport.GetClipWidth(item.Duration);
            if (x + width < _viewport.TrackHeaderWidth || x > RenderSize.Width) continue;

            var rect = new Rect(x + 1, yBase + 4, Math.Max(2, width - 2), _viewport.TrackHeight - 8);
            var isSelected = _selectedItem != null && _selectedItem.Id == item.Id;
            var isAudioTrack = track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice;
            var fill = CreateClipBrush(item.Kind, isAudioTrack);
            var border = isSelected
                ? new Pen(new SolidColorBrush(Color.FromRgb(179, 139, 255)), 1.8)
                : new Pen(new SolidColorBrush(Color.FromRgb(74, 55, 104)), 0.8);
            dc.DrawRoundedRectangle(fill, border, rect, 4, 4);

            dc.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(105, 220, 205, 255)), 0.7),
                new Point(rect.X + 4, rect.Y + 1),
                new Point(rect.Right - 4, rect.Y + 1));

            if (isAudioTrack && rect.Width > 18)
                DrawAudioWaveform(dc, rect, trackIndex);

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

            if (item.FadeInDuration.Seconds > 0)
            {
                var fadeWidth = Math.Min(_viewport.GetClipWidth(item.FadeInDuration), rect.Width);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1),
                    new Point(rect.X, rect.Bottom - 2), new Point(rect.X + fadeWidth, rect.Y + 2));
            }
            if (item.FadeOutDuration.Seconds > 0)
            {
                var fadeWidth = Math.Min(_viewport.GetClipWidth(item.FadeOutDuration), rect.Width);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1),
                    new Point(rect.Right - fadeWidth, rect.Y + 2), new Point(rect.Right, rect.Bottom - 2));
            }
        }
    }

    private static Brush CreateClipBrush(ItemKind kind, bool audioTrack)
    {
        Color start;
        Color end;
        if (audioTrack)
        {
            start = Color.FromRgb(19, 82, 59);
            end = Color.FromRgb(12, 48, 39);
        }
        else
        {
            (start, end) = kind switch
            {
                ItemKind.Text => (Color.FromRgb(113, 52, 166), Color.FromRgb(71, 34, 111)),
                ItemKind.Image => (Color.FromRgb(46, 101, 105), Color.FromRgb(28, 62, 71)),
                ItemKind.Sticker => (Color.FromRgb(135, 94, 39), Color.FromRgb(82, 57, 26)),
                ItemKind.AdjustmentLayer => (Color.FromRgb(83, 62, 119), Color.FromRgb(48, 38, 69)),
                _ => (Color.FromRgb(65, 43, 105), Color.FromRgb(35, 27, 59)),
            };
        }

        var brush = new LinearGradientBrush(start, end, 0);
        brush.Freeze();
        return brush;
    }

    private string ResolveClipLabel(TimelineItem item)
    {
        if (item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.TextContent))
            return item.TextContent;
        if (item.MediaAssetId is { } assetId)
            return AssetNameResolver?.Invoke(assetId) ?? item.Kind.ToString();
        return item.Kind.ToString();
    }

    private static void DrawAudioWaveform(DrawingContext dc, Rect rect, int seed)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(145, 81, 196, 127)), 0.75);
        var center = rect.Y + rect.Height * 0.66;
        var usableWidth = Math.Max(0, rect.Width - 8);
        var samples = Math.Min(90, Math.Max(8, (int)(usableWidth / 4)));
        for (var i = 0; i < samples; i++)
        {
            var progress = samples <= 1 ? 0 : i / (double)(samples - 1);
            var x = rect.X + 4 + progress * usableWidth;
            var amplitude = (0.18 + 0.72 * Math.Abs(Math.Sin((i + seed * 3) * 0.63))) * rect.Height * 0.22;
            dc.DrawLine(pen, new Point(x, center - amplitude), new Point(x, center + amplitude));
        }
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
                ? new SolidColorBrush(Color.FromRgb(242, 184, 75))
                : existing
                    ? new SolidColorBrush(Color.FromRgb(139, 92, 246))
                    : new SolidColorBrush(Color.FromRgb(22, 23, 32));
            var stroke = selected
                ? new Pen(new SolidColorBrush(Color.FromRgb(255, 225, 150)), 2)
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

        var markerBrush = new SolidColorBrush(Color.FromRgb(242, 184, 75));
        var markerPen = new Pen(markerBrush, 1);
        foreach (var marker in Sequence.Markers)
        {
            var x = _viewport.TimeToPixel(marker.Time);
            if (x < _viewport.TrackHeaderWidth || x > RenderSize.Width) continue;

            dc.DrawLine(markerPen, new Point(x, _viewport.RulerHeight - 6), new Point(x, RenderSize.Height));
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(new Point(x - 5, 0), true, true);
                context.LineTo(new Point(x + 5, 0), true, false);
                context.LineTo(new Point(x, 7), true, false);
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
                dc.DrawText(label, new Point(x + 5, 10));
            }
        }
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
        SyncViewportSize();
        if (Sequence == null) return;

        var pos = e.GetPosition(this);
        if (!GetContentClipRect(includeRuler: false).Contains(pos))
        {
            _viewport.Pan(0, e.Delta);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _viewport.Zoom(factor, e.GetPosition(this).X);
        ZoomScaleChanged?.Invoke(this, _viewport.ZoomScale);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
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
        if (pos.Y < _viewport.RulerHeight && pos.X > _viewport.TrackHeaderWidth)
        {
            _playheadTime = HitTestMarker(pos.X)?.Time ?? _viewport.PixelToTime(ClampContentX(pos.X));
            _dragFromPlayhead = true;
            _selectedItem = null;
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
            _selectedItem = null;
            _selectedTransition = null;
            _selectedTrackIndex = -1;
            ClipSelected?.Invoke(this, null);
            TransitionSelected?.Invoke(this, null);
            if (pos.X > _viewport.TrackHeaderWidth)
            {
                _playheadTime = _viewport.PixelToTime(ClampContentX(pos.X));
                PlayheadMoved?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _selectedItem = item;
        _selectedTransition = null;
        _selectedTrackIndex = trackIdx;
        _dragTrackIndex = trackIdx;
        ClipSelected?.Invoke(this, item);
        TransitionSelected?.Invoke(this, null);

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
        _dragOriginMouseX = pos.X;

        var clipX = _viewport.GetClipX(item.TimelineStart);
        var clipW = _viewport.GetClipWidth(item.Duration);
        var distFromLeft = pos.X - clipX;
        var distFromRight = clipX + clipW - pos.X;

        if (distFromLeft < TrimEdgeThreshold && item.Duration.Seconds > 0.2)
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
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            _viewport.Pan(pos.X - _lastMousePos.X, pos.Y - _lastMousePos.Y);
            _lastMousePos = pos;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragFromPlayhead)
        {
            _playheadTime = _viewport.PixelToTime(ClampContentX(pos.X));
            PlayheadMoved?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isTrimming && _dragItem != null)
        {
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
            var totalDeltaSeconds = (pos.X - _dragOriginMouseX) / _viewport.PixelsPerSecond;
            var candidateStart = MediaTime.FromSeconds(
                Math.Max(0, _dragStartTime.Seconds + totalDeltaSeconds));
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
        if (HitTestTransition(pos) != null)
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
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        if (_isPanning)
        {
            _isPanning = false;
            e.Handled = true;
        }

        if (_isDraggingClip && _dragItem != null)
        {
            var item = _dragItem;
            var newStart = item.TimelineStart;
            item.TimelineStart = _dragStartTime;
            _isDraggingClip = false;
            ClipMoveRequested?.Invoke(this, new ClipMoveRequestedEventArgs(
                item,
                _selectedTrackIndex,
                _dragTrackIndex,
                newStart));
            e.Handled = true;
        }

        if (_isTrimming && _dragItem != null)
        {
            var item = _dragItem;
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

        var thresholdSeconds = 10.0 / Math.Max(1, _viewport.PixelsPerSecond);
        var bestSeconds = candidate.Seconds;
        var bestDistance = thresholdSeconds + double.Epsilon;

        void Consider(MediaTime target)
        {
            var distance = Math.Abs(target.Seconds - candidate.Seconds);
            if (distance <= thresholdSeconds && distance < bestDistance)
            {
                bestDistance = distance;
                bestSeconds = target.Seconds;
            }
        }

        Consider(MediaTime.Zero);
        foreach (var marker in Sequence.Markers)
            Consider(marker.Time);

        foreach (var item in Sequence.Tracks.SelectMany(track => track.Items))
        {
            if (item.Id == exclude.Id) continue;
            Consider(item.TimelineStart);
            Consider(item.TimelineEnd);
        }

        return MediaTime.FromSeconds(bestSeconds);
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
        const double threshold = 7;
        return Sequence.Markers
            .Select(marker => new { Marker = marker, Distance = Math.Abs(_viewport.TimeToPixel(marker.Time) - x) })
            .Where(candidate => candidate.Distance <= threshold)
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Marker)
            .FirstOrDefault();
    }

    private (TimelineItem? item, int trackIndex, int itemIndex) HitTest(Point pos)
    {
        if (Sequence == null) return (null, -1, -1);
        if (!GetContentClipRect(includeRuler: false).Contains(pos)) return (null, -1, -1);

        for (int t = 0; t < Sequence.Tracks.Count; t++)
        {
            var track = Sequence.Tracks[t];
            var y = _viewport.TrackIndexToY(t);
            if (pos.Y < y || pos.Y > y + _viewport.TrackHeight) continue;

            for (int i = 0; i < track.Items.Count; i++)
            {
                var item = track.Items[i];
                var x = _viewport.GetClipX(item.TimelineStart);
                var w = _viewport.GetClipWidth(item.Duration);
                if (pos.X >= x && pos.X <= x + w)
                    return (item, t, i);
            }
        }

        return (null, -1, -1);
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

    private IEnumerable<TransitionSelection> GetTransitionSlots()
    {
        if (Sequence == null) yield break;

        for (var t = 0; t < Sequence.Tracks.Count; t++)
        {
            var items = Sequence.Tracks[t].Items
                .OrderBy(item => item.TimelineStart.Seconds)
                .ToList();
            for (var i = 0; i < items.Count - 1; i++)
            {
                var left = items[i];
                var right = items[i + 1];
                var gap = right.TimelineStart.Seconds - left.TimelineEnd.Seconds;
                if (Math.Abs(gap) > 0.05) continue;

                var transition = Sequence.Transitions.FirstOrDefault(candidate =>
                    candidate.LeftItemId == left.Id && candidate.RightItemId == right.Id);
                yield return new TransitionSelection(transition, left, right, t);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _viewport.ViewportWidth = availableSize.Width;
        _viewport.ViewportHeight = availableSize.Height;
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport.ViewportWidth = finalSize.Width;
        _viewport.ViewportHeight = finalSize.Height;
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
