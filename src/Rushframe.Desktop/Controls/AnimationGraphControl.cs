using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Domain;

namespace Rushframe.Desktop.Controls;

public sealed class AnimationGraphControl : FrameworkElement
{
    private const double Padding = 34;
    private AnimationChannel? _channel;
    private int _selectedIndex = -1;
    private DragTarget _dragTarget;
    private ValueRange _valueRange = new(-1, 1);

    private enum DragTarget { None, Keyframe, OutHandle, InHandle }

    public AnimationChannel? Channel
    {
        get => _channel;
        set
        {
            _channel = value;
            _selectedIndex = value?.Keyframes.Count > 0 ? 0 : -1;
            InvalidateVisual();
        }
    }

    public double DurationSeconds { get; set; } = 5;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            InvalidateVisual();
        }
    }

    public event EventHandler? ChannelChanged;
    public event EventHandler<int>? SelectedIndexChanged;

    public AnimationGraphControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 250;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(16, 12, 25)),
            new Pen(new SolidColorBrush(Color.FromRgb(59, 47, 78)), 1),
            new Rect(0, 0, RenderSize.Width, RenderSize.Height),
            6,
            6);
        if (_channel == null || RenderSize.Width <= Padding * 2 || RenderSize.Height <= Padding * 2)
            return;

        _valueRange = ResolveValueRange(_channel);
        DrawGrid(dc);
        DrawCurve(dc);
        DrawKeyframes(dc);
        DrawSelectedHandles(dc);
    }

    private void DrawGrid(DrawingContext dc)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 105, 83, 130)), 0.75);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 155, 130, 184)), 1);
        for (var index = 0; index <= 10; index++)
        {
            var x = Padding + (RenderSize.Width - Padding * 2) * index / 10;
            dc.DrawLine(index is 0 or 10 ? axisPen : gridPen, new Point(x, Padding), new Point(x, RenderSize.Height - Padding));
        }
        for (var index = 0; index <= 6; index++)
        {
            var y = Padding + (RenderSize.Height - Padding * 2) * index / 6;
            dc.DrawLine(index is 0 or 6 ? axisPen : gridPen, new Point(Padding, y), new Point(RenderSize.Width - Padding, y));
        }

        var textBrush = new SolidColorBrush(Color.FromRgb(153, 137, 174));
        for (var index = 0; index <= 5; index++)
        {
            var seconds = Math.Max(0.001, DurationSeconds) * index / 5;
            var text = new FormattedText(
                $"{seconds:0.##}s",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                9,
                textBrush,
                1.25);
            dc.DrawText(text, new Point(TimeToX(seconds) - text.Width / 2, RenderSize.Height - Padding + 7));
        }
        for (var index = 0; index <= 4; index++)
        {
            var value = _valueRange.Maximum - _valueRange.Span * index / 4;
            var text = new FormattedText(
                value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono"),
                9,
                textBrush,
                1.25);
            dc.DrawText(text, new Point(4, ValueToY(value) - text.Height / 2));
        }
    }

    private void DrawCurve(DrawingContext dc)
    {
        if (_channel == null || _channel.Keyframes.Count == 0) return;
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var samples = Math.Max(80, (int)Math.Round(RenderSize.Width));
        for (var index = 0; index <= samples; index++)
        {
            var time = Math.Max(0.001, DurationSeconds) * index / samples;
            var point = new Point(TimeToX(time), ValueToY(_channel.GetValueAt(MediaTime.FromSeconds(time))));
            if (index == 0) context.BeginFigure(point, false, false);
            else context.LineTo(point, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(173, 122, 255)), 2), geometry);
    }

    private void DrawKeyframes(DrawingContext dc)
    {
        if (_channel == null) return;
        var ordered = _channel.Keyframes.OrderBy(keyframe => keyframe.Time.Ticks).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var point = KeyframePoint(ordered[index]);
            var selected = ReferenceEquals(ordered[index], GetSelectedKeyframe());
            var fill = new SolidColorBrush(selected ? Color.FromRgb(238, 222, 255) : Color.FromRgb(147, 92, 246));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(248, 244, 255)), selected ? 1.5 : 0.75);
            var diamond = new StreamGeometry();
            using (var context = diamond.Open())
            {
                context.BeginFigure(new Point(point.X, point.Y - 6), true, true);
                context.LineTo(new Point(point.X + 6, point.Y), true, false);
                context.LineTo(new Point(point.X, point.Y + 6), true, false);
                context.LineTo(new Point(point.X - 6, point.Y), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(fill, pen, diamond);
        }
    }

    private void DrawSelectedHandles(DrawingContext dc)
    {
        var selected = GetSelectedKeyframe();
        if (_channel == null || selected == null || selected.Interpolation != InterpolationType.Bezier) return;
        var ordered = _channel.Keyframes.OrderBy(keyframe => keyframe.Time.Ticks).ToList();
        var index = ordered.IndexOf(selected);
        var selectedPoint = KeyframePoint(selected);
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(196, 181, 253)), 1);
        var handleFill = new SolidColorBrush(Color.FromRgb(196, 181, 253));

        if (index < ordered.Count - 1)
        {
            var right = ordered[index + 1];
            var spanTime = right.Time.Seconds - selected.Time.Seconds;
            var spanValue = right.Value - selected.Value;
            var handle = new Point(
                TimeToX(selected.Time.Seconds + spanTime * selected.OutTangentX),
                ValueToY(selected.Value + spanValue * selected.OutTangentY));
            dc.DrawLine(handlePen, selectedPoint, handle);
            dc.DrawEllipse(handleFill, Brushes.White is SolidColorBrush white ? new Pen(white, 0.75) : null, handle, 4, 4);
        }

        if (index > 0)
        {
            var left = ordered[index - 1];
            var spanTime = selected.Time.Seconds - left.Time.Seconds;
            var spanValue = selected.Value - left.Value;
            var handle = new Point(
                TimeToX(left.Time.Seconds + spanTime * selected.InTangentX),
                ValueToY(left.Value + spanValue * selected.InTangentY));
            dc.DrawLine(handlePen, selectedPoint, handle);
            dc.DrawEllipse(handleFill, Brushes.White is SolidColorBrush white ? new Pen(white, 0.75) : null, handle, 4, 4);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton != MouseButton.Left || _channel == null) return;
        var point = e.GetPosition(this);

        var handle = HitTestHandle(point);
        if (handle != DragTarget.None)
        {
            _dragTarget = handle;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var hit = _channel.Keyframes
            .Select((keyframe, index) => new { Keyframe = keyframe, Index = index, Distance = (KeyframePoint(keyframe) - point).Length })
            .Where(candidate => candidate.Distance <= 10)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (hit != null)
        {
            _selectedIndex = hit.Index;
            _dragTarget = DragTarget.Keyframe;
            SelectedIndexChanged?.Invoke(this, _selectedIndex);
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || _channel == null || _dragTarget == DragTarget.None) return;
        var selected = GetSelectedKeyframe();
        if (selected == null) return;
        var point = e.GetPosition(this);

        if (_dragTarget == DragTarget.Keyframe)
        {
            var time = Math.Clamp(XToTime(point.X), 0, Math.Max(0.001, DurationSeconds));
            var value = YToValue(point.Y);
            selected.Time = MediaTime.FromSeconds(time);
            selected.Value = value;
            SortAndRestoreSelection(selected);
        }
        else
        {
            UpdateBezierHandle(selected, point, _dragTarget);
        }

        ChannelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _dragTarget = DragTarget.None;
        base.OnMouseUp(e);
    }

    private DragTarget HitTestHandle(Point point)
    {
        var selected = GetSelectedKeyframe();
        if (_channel == null || selected == null || selected.Interpolation != InterpolationType.Bezier) return DragTarget.None;
        var ordered = _channel.Keyframes.OrderBy(keyframe => keyframe.Time.Ticks).ToList();
        var index = ordered.IndexOf(selected);
        if (index < ordered.Count - 1)
        {
            var right = ordered[index + 1];
            var handle = new Point(
                TimeToX(selected.Time.Seconds + (right.Time.Seconds - selected.Time.Seconds) * selected.OutTangentX),
                ValueToY(selected.Value + (right.Value - selected.Value) * selected.OutTangentY));
            if ((handle - point).Length <= 10) return DragTarget.OutHandle;
        }
        if (index > 0)
        {
            var left = ordered[index - 1];
            var handle = new Point(
                TimeToX(left.Time.Seconds + (selected.Time.Seconds - left.Time.Seconds) * selected.InTangentX),
                ValueToY(left.Value + (selected.Value - left.Value) * selected.InTangentY));
            if ((handle - point).Length <= 10) return DragTarget.InHandle;
        }
        return DragTarget.None;
    }

    private void UpdateBezierHandle(Keyframe selected, Point point, DragTarget target)
    {
        if (_channel == null) return;
        var ordered = _channel.Keyframes.OrderBy(keyframe => keyframe.Time.Ticks).ToList();
        var index = ordered.IndexOf(selected);
        if (target == DragTarget.OutHandle && index < ordered.Count - 1)
        {
            var right = ordered[index + 1];
            var spanTime = Math.Max(0.000001, right.Time.Seconds - selected.Time.Seconds);
            var spanValue = Math.Abs(right.Value - selected.Value) < 0.000001 ? _valueRange.Span : right.Value - selected.Value;
            selected.OutTangentX = Math.Clamp((XToTime(point.X) - selected.Time.Seconds) / spanTime, 0, 1);
            selected.OutTangentY = (YToValue(point.Y) - selected.Value) / spanValue;
        }
        else if (target == DragTarget.InHandle && index > 0)
        {
            var left = ordered[index - 1];
            var spanTime = Math.Max(0.000001, selected.Time.Seconds - left.Time.Seconds);
            var spanValue = Math.Abs(selected.Value - left.Value) < 0.000001 ? _valueRange.Span : selected.Value - left.Value;
            selected.InTangentX = Math.Clamp((XToTime(point.X) - left.Time.Seconds) / spanTime, 0, 1);
            selected.InTangentY = (YToValue(point.Y) - left.Value) / spanValue;
        }
    }

    private void SortAndRestoreSelection(Keyframe selected)
    {
        if (_channel == null) return;
        _channel.Keyframes.Sort((left, right) => left.Time.Ticks.CompareTo(right.Time.Ticks));
        _selectedIndex = _channel.Keyframes.IndexOf(selected);
        SelectedIndexChanged?.Invoke(this, _selectedIndex);
    }

    private Keyframe? GetSelectedKeyframe() =>
        _channel != null && _selectedIndex >= 0 && _selectedIndex < _channel.Keyframes.Count
            ? _channel.Keyframes[_selectedIndex]
            : null;

    private Point KeyframePoint(Keyframe keyframe) => new(TimeToX(keyframe.Time.Seconds), ValueToY(keyframe.Value));
    private double TimeToX(double seconds) => Padding + Math.Clamp(seconds / Math.Max(0.001, DurationSeconds), 0, 1) * Math.Max(1, RenderSize.Width - Padding * 2);
    private double XToTime(double x) => Math.Clamp((x - Padding) / Math.Max(1, RenderSize.Width - Padding * 2), 0, 1) * Math.Max(0.001, DurationSeconds);
    private double ValueToY(double value) => Padding + (1 - Math.Clamp((value - _valueRange.Minimum) / _valueRange.Span, 0, 1)) * Math.Max(1, RenderSize.Height - Padding * 2);
    private double YToValue(double y) => _valueRange.Minimum + (1 - Math.Clamp((y - Padding) / Math.Max(1, RenderSize.Height - Padding * 2), 0, 1)) * _valueRange.Span;

    private static ValueRange ResolveValueRange(AnimationChannel channel)
    {
        var values = channel.Keyframes.Select(keyframe => keyframe.Value).Append(channel.DefaultValue).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        if (Math.Abs(maximum - minimum) < 0.001)
        {
            minimum -= Math.Max(1, Math.Abs(minimum) * 0.25);
            maximum += Math.Max(1, Math.Abs(maximum) * 0.25);
        }
        else
        {
            var padding = (maximum - minimum) * 0.2;
            minimum -= padding;
            maximum += padding;
        }
        return new ValueRange(minimum, maximum);
    }

    private readonly record struct ValueRange(double Minimum, double Maximum)
    {
        public double Span => Math.Max(0.000001, Maximum - Minimum);
    }
}
