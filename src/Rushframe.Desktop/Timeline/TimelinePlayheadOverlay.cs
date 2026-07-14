using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Rushframe.Desktop.Timeline;

/// <summary>
/// Lightweight transport layer drawn above the static timeline. Moving the playhead only
/// invalidates this element, not the clip/ruler/waveform surface.
/// </summary>
public sealed class TimelinePlayheadOverlay : FrameworkElement, IDisposable
{
    private static readonly SolidColorBrush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(192, 132, 252)));
    private static readonly SolidColorBrush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(13, 11, 19)));
    private static readonly Pen LinePen = Freeze(new Pen(AccentBrush, 1.5));
    private static readonly Pen BorderPen = Freeze(new Pen(AccentBrush, 0.75));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private TimelineControl? _timeline;
    private bool _disposed;

    public TimelinePlayheadOverlay(TimelineControl timeline)
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Attach(timeline);
    }

    public void Attach(TimelineControl timeline)
    {
        if (ReferenceEquals(_timeline, timeline)) return;
        Detach();
        _timeline = timeline;
        timeline.PlayheadVisualChanged += OnTimelineVisualChanged;
        timeline.ViewportChanged += OnTimelineVisualChanged;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var timeline = _timeline;
        if (timeline == null || timeline.Sequence == null) return;
        var x = timeline.PlayheadPixelX;
        if (x < timeline.TrackHeaderWidth || x > RenderSize.Width) return;

        dc.DrawLine(LinePen, new Point(x, 0), new Point(x, RenderSize.Height));
        var text = $"{timeline.PlayheadTime.Seconds:F1}s";
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9,
            AccentBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var labelX = Math.Clamp(x + 4, timeline.TrackHeaderWidth, Math.Max(timeline.TrackHeaderWidth, RenderSize.Width - formatted.Width - 10));
        var labelRect = new Rect(labelX - 3, 1, formatted.Width + 7, formatted.Height + 4);
        dc.DrawRoundedRectangle(LabelBrush, BorderPen, labelRect, 3, 3);
        dc.DrawText(formatted, new Point(labelX, 3));
    }

    private void OnTimelineVisualChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void Detach()
    {
        if (_timeline == null) return;
        _timeline.PlayheadVisualChanged -= OnTimelineVisualChanged;
        _timeline.ViewportChanged -= OnTimelineVisualChanged;
        _timeline = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    private static T Freeze<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
