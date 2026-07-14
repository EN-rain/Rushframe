using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private Canvas? _previewSelectionGroup;
    private Rectangle? _previewSelectionOutline;
    private Line? _previewSnapVertical;
    private Line? _previewSnapHorizontal;
    private TimelineItem? _previewInteractionItem;
    private PreviewTransformSnapshot? _previewTransformStart;
    private Transform2D? _previewWorkingTransform;
    private PreviewManipulationMode _previewManipulationMode;
    private PreviewHandlePosition _previewHandlePosition;

    private enum PreviewManipulationMode { None, Move, Scale, Rotate }
    private enum PreviewHandlePosition { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

    private void InitializePreviewInteraction()
    {
        _previewSnapVertical = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(196, 181, 253)),
            StrokeThickness = 1.25,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _previewSnapHorizontal = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(196, 181, 253)),
            StrokeThickness = 1.25,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        PreviewInteractionCanvas.Children.Add(_previewSnapVertical);
        PreviewInteractionCanvas.Children.Add(_previewSnapHorizontal);

        _previewSelectionGroup = new Canvas { Background = Brushes.Transparent };
        _previewSelectionOutline = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(211, 177, 255)),
            StrokeThickness = 1.5,
            StrokeDashArray = [5, 3],
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _previewSelectionGroup.Children.Add(_previewSelectionOutline);

        var moveThumb = new Thumb
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Opacity = 0.02,
        };
        moveThumb.DragStarted += (_, _) => BeginPreviewManipulation(PreviewManipulationMode.Move);
        moveThumb.DragDelta += (_, args) => MovePreviewSelection(args.HorizontalChange, args.VerticalChange);
        moveThumb.DragCompleted += (_, _) => CompletePreviewManipulation();
        Panel.SetZIndex(moveThumb, 1);
        _previewSelectionGroup.Children.Add(moveThumb);

        foreach (var position in Enum.GetValues<PreviewHandlePosition>())
        {
            var handle = CreatePreviewHandle(position);
            _previewSelectionGroup.Children.Add(handle);
        }

        var rotationHandle = new Thumb
        {
            Width = 12,
            Height = 12,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(168, 112, 255)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Template = CreateEllipseThumbTemplate(),
            Tag = "rotation",
        };
        rotationHandle.DragStarted += (_, _) => BeginPreviewManipulation(PreviewManipulationMode.Rotate);
        rotationHandle.DragDelta += (_, args) => RotatePreviewSelection(args.HorizontalChange, args.VerticalChange);
        rotationHandle.DragCompleted += (_, _) => CompletePreviewManipulation();
        Panel.SetZIndex(rotationHandle, 3);
        _previewSelectionGroup.Children.Add(rotationHandle);

        PreviewInteractionCanvas.Children.Add(_previewSelectionGroup);
        PreviewSurface.SizeChanged += (_, _) => UpdatePreviewInteractionOverlay(_previewInteractionItem);
    }

    private Thumb CreatePreviewHandle(PreviewHandlePosition position)
    {
        var cursor = position switch
        {
            PreviewHandlePosition.Top or PreviewHandlePosition.Bottom => Cursors.SizeNS,
            PreviewHandlePosition.Left or PreviewHandlePosition.Right => Cursors.SizeWE,
            PreviewHandlePosition.TopLeft or PreviewHandlePosition.BottomRight => Cursors.SizeNWSE,
            _ => Cursors.SizeNESW,
        };
        var thumb = new Thumb
        {
            Width = 10,
            Height = 10,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
            BorderThickness = new Thickness(1.5),
            Cursor = cursor,
            Tag = position,
        };
        thumb.DragStarted += (_, _) =>
        {
            _previewHandlePosition = position;
            BeginPreviewManipulation(PreviewManipulationMode.Scale);
        };
        thumb.DragDelta += (_, args) => ScalePreviewSelection(args.HorizontalChange, args.VerticalChange);
        thumb.DragCompleted += (_, _) => CompletePreviewManipulation();
        Panel.SetZIndex(thumb, 3);
        return thumb;
    }

    private static ControlTemplate CreateEllipseThumbTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var factory = new FrameworkElementFactory(typeof(Ellipse));
        factory.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding(nameof(Control.Background))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        factory.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        factory.SetBinding(Shape.StrokeThicknessProperty, new System.Windows.Data.Binding(nameof(Control.BorderThickness))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
            Converter = new BorderThicknessToDoubleConverter(),
        });
        template.VisualTree = factory;
        return template;
    }

    private void UpdatePreviewInteractionOverlay(TimelineItem? item)
    {
        if (_previewInteractionItem?.Id != item?.Id)
        {
            _previewManipulationMode = PreviewManipulationMode.None;
            _previewTransformStart = null;
            _previewWorkingTransform = null;
            HidePreviewSnapGuides();
        }
        _previewInteractionItem = item;
        if (_previewSelectionGroup == null || _previewSelectionOutline == null
            || item == null || item.Kind is not (ItemKind.Clip or ItemKind.Image or ItemKind.Text or ItemKind.Sticker))
        {
            PreviewInteractionCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var sequence = _project.MainSequence;
        var track = sequence?.Tracks.FirstOrDefault(candidate => candidate.Items.Any(current => current.Id == item.Id));
        if (sequence == null || track?.Locked == true || item.Locked
            || PreviewInteractionCanvas.ActualWidth <= 1 || PreviewInteractionCanvas.ActualHeight <= 1)
        {
            PreviewInteractionCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var transform = _previewWorkingTransform ?? item.Transform;
        var canvasRect = GetPreviewCanvasDisplayRect(sequence);
        var naturalSize = GetPreviewItemNaturalSize(item, sequence);
        var width = Math.Max(12, naturalSize.Width * Math.Abs(transform.ScaleX) * canvasRect.Width / sequence.Width);
        var height = Math.Max(12, naturalSize.Height * Math.Abs(transform.ScaleY) * canvasRect.Height / sequence.Height);
        var left = canvasRect.Left + ((sequence.Width - naturalSize.Width * Math.Abs(transform.ScaleX)) / 2 + transform.PositionX)
            * canvasRect.Width / sequence.Width;
        var top = canvasRect.Top + ((sequence.Height - naturalSize.Height * Math.Abs(transform.ScaleY)) / 2 + transform.PositionY)
            * canvasRect.Height / sequence.Height;

        _previewSelectionGroup.Width = width;
        _previewSelectionGroup.Height = height;
        Canvas.SetLeft(_previewSelectionGroup, left);
        Canvas.SetTop(_previewSelectionGroup, top);
        _previewSelectionGroup.RenderTransformOrigin = new Point(0.5, 0.5);
        _previewSelectionGroup.RenderTransform = new RotateTransform(transform.RotationDegrees);
        _previewSelectionOutline.Width = width;
        _previewSelectionOutline.Height = height;

        var children = _previewSelectionGroup.Children.OfType<Thumb>().ToArray();
        var moveThumb = children.First(thumb => thumb.Tag == null);
        moveThumb.Width = width;
        moveThumb.Height = height;
        Canvas.SetLeft(moveThumb, 0);
        Canvas.SetTop(moveThumb, 0);

        foreach (var handle in children.Where(thumb => thumb.Tag is PreviewHandlePosition))
            PositionPreviewHandle(handle, (PreviewHandlePosition)handle.Tag, width, height);

        var rotation = children.First(thumb => Equals(thumb.Tag, "rotation"));
        Canvas.SetLeft(rotation, width / 2 - rotation.Width / 2);
        Canvas.SetTop(rotation, -28);

        PreviewInteractionCanvas.Visibility = Visibility.Visible;
    }

    private static void PositionPreviewHandle(Thumb handle, PreviewHandlePosition position, double width, double height)
    {
        var x = position switch
        {
            PreviewHandlePosition.TopLeft or PreviewHandlePosition.Left or PreviewHandlePosition.BottomLeft => -handle.Width / 2,
            PreviewHandlePosition.Top or PreviewHandlePosition.Bottom => width / 2 - handle.Width / 2,
            _ => width - handle.Width / 2,
        };
        var y = position switch
        {
            PreviewHandlePosition.TopLeft or PreviewHandlePosition.Top or PreviewHandlePosition.TopRight => -handle.Height / 2,
            PreviewHandlePosition.Left or PreviewHandlePosition.Right => height / 2 - handle.Height / 2,
            _ => height - handle.Height / 2,
        };
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private Rect GetPreviewCanvasDisplayRect(Sequence sequence)
    {
        var hostWidth = Math.Max(1, PreviewInteractionCanvas.ActualWidth);
        var hostHeight = Math.Max(1, PreviewInteractionCanvas.ActualHeight);
        var scale = Math.Min(hostWidth / sequence.Width, hostHeight / sequence.Height);
        var width = sequence.Width * scale;
        var height = sequence.Height * scale;
        return new Rect((hostWidth - width) / 2, (hostHeight - height) / 2, width, height);
    }

    private Size GetPreviewItemNaturalSize(TimelineItem item, Sequence sequence)
    {
        if (item.Kind == ItemKind.Text)
        {
            var measured = TextLayoutMetrics.Measure(item);
            return new Size(measured.Width, measured.Height);
        }

        if (item.MediaAssetId is { } assetId)
        {
            var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
            if (asset is { PixelWidth: > 0, PixelHeight: > 0 })
                return new Size(asset.PixelWidth, asset.PixelHeight);
        }
        return new Size(sequence.Width, sequence.Height);
    }

    private void BeginPreviewManipulation(PreviewManipulationMode mode)
    {
        if (_previewInteractionItem == null) return;
        _previewManipulationMode = mode;
        _previewTransformStart = PreviewTransformSnapshot.From(_previewInteractionItem.Transform);
        _previewWorkingTransform = _previewTransformStart.ToTransform();
        if (_isPreviewPlaying) PausePreview();
    }

    private void MovePreviewSelection(double horizontalChange, double verticalChange)
    {
        var item = _previewInteractionItem;
        var transform = _previewWorkingTransform;
        var sequence = _project.MainSequence;
        if (item == null || transform == null || sequence == null) return;
        var display = GetPreviewCanvasDisplayRect(sequence);
        transform.PositionX += horizontalChange * sequence.Width / display.Width;
        transform.PositionY += verticalChange * sequence.Height / display.Height;
        ApplyPreviewSnapping(item, transform, sequence, display);
        UpdatePreviewInteractionOverlay(item);
        UpdateInspectorFieldsFromPreview(transform);
    }

    private void ScalePreviewSelection(double horizontalChange, double verticalChange)
    {
        var item = _previewInteractionItem;
        var transform = _previewWorkingTransform;
        var sequence = _project.MainSequence;
        if (item == null || transform == null || sequence == null) return;
        var display = GetPreviewCanvasDisplayRect(sequence);
        var natural = GetPreviewItemNaturalSize(item, sequence);
        var deltaX = horizontalChange * sequence.Width / display.Width / Math.Max(1, natural.Width);
        var deltaY = verticalChange * sequence.Height / display.Height / Math.Max(1, natural.Height);

        var affectsLeft = _previewHandlePosition is PreviewHandlePosition.Left or PreviewHandlePosition.TopLeft or PreviewHandlePosition.BottomLeft;
        var affectsRight = _previewHandlePosition is PreviewHandlePosition.Right or PreviewHandlePosition.TopRight or PreviewHandlePosition.BottomRight;
        var affectsTop = _previewHandlePosition is PreviewHandlePosition.Top or PreviewHandlePosition.TopLeft or PreviewHandlePosition.TopRight;
        var affectsBottom = _previewHandlePosition is PreviewHandlePosition.Bottom or PreviewHandlePosition.BottomLeft or PreviewHandlePosition.BottomRight;

        if (affectsLeft || affectsRight)
        {
            var signed = affectsLeft ? -deltaX : deltaX;
            transform.ScaleX = Math.Clamp(transform.ScaleX + signed, 0.02, 20);
            transform.PositionX += horizontalChange * sequence.Width / display.Width / 2;
        }
        if (affectsTop || affectsBottom)
        {
            var signed = affectsTop ? -deltaY : deltaY;
            transform.ScaleY = Math.Clamp(transform.ScaleY + signed, 0.02, 20);
            transform.PositionY += verticalChange * sequence.Height / display.Height / 2;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var uniform = Math.Max(transform.ScaleX, transform.ScaleY);
            transform.ScaleX = uniform;
            transform.ScaleY = uniform;
        }

        UpdatePreviewInteractionOverlay(item);
        UpdateInspectorFieldsFromPreview(transform);
    }

    private void RotatePreviewSelection(double horizontalChange, double verticalChange)
    {
        var item = _previewInteractionItem;
        var transform = _previewWorkingTransform;
        if (item == null || transform == null) return;
        transform.RotationDegrees += horizontalChange + verticalChange;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            transform.RotationDegrees = Math.Round(transform.RotationDegrees / 15) * 15;
        transform.RotationDegrees %= 360;
        UpdatePreviewInteractionOverlay(item);
        UpdateInspectorFieldsFromPreview(transform);
    }

    private void ApplyPreviewSnapping(TimelineItem item, Transform2D transform, Sequence sequence, Rect display)
    {
        var natural = GetPreviewItemNaturalSize(item, sequence);
        var width = natural.Width * Math.Abs(transform.ScaleX);
        var height = natural.Height * Math.Abs(transform.ScaleY);
        var left = (sequence.Width - width) / 2 + transform.PositionX;
        var top = (sequence.Height - height) / 2 + transform.PositionY;
        var centerX = left + width / 2;
        var centerY = top + height / 2;
        var thresholdX = 8 * sequence.Width / display.Width;
        var thresholdY = 8 * sequence.Height / display.Height;
        var snappedX = false;
        var snappedY = false;

        var xTargets = new[] { 0d, sequence.Width / 2d, sequence.Width };
        foreach (var target in xTargets)
        {
            var point = target == 0 ? left : target == sequence.Width ? left + width : centerX;
            if (Math.Abs(point - target) > thresholdX) continue;
            transform.PositionX += target - point;
            snappedX = true;
            break;
        }
        var yTargets = new[] { 0d, sequence.Height / 2d, sequence.Height };
        foreach (var target in yTargets)
        {
            var point = target == 0 ? top : target == sequence.Height ? top + height : centerY;
            if (Math.Abs(point - target) > thresholdY) continue;
            transform.PositionY += target - point;
            snappedY = true;
            break;
        }

        if (_previewSnapVertical != null)
        {
            _previewSnapVertical.X1 = _previewSnapVertical.X2 = display.Left + display.Width / 2;
            _previewSnapVertical.Y1 = display.Top;
            _previewSnapVertical.Y2 = display.Bottom;
            _previewSnapVertical.Visibility = snappedX ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_previewSnapHorizontal != null)
        {
            _previewSnapHorizontal.Y1 = _previewSnapHorizontal.Y2 = display.Top + display.Height / 2;
            _previewSnapHorizontal.X1 = display.Left;
            _previewSnapHorizontal.X2 = display.Right;
            _previewSnapHorizontal.Visibility = snappedY ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CompletePreviewManipulation()
    {
        var item = _previewInteractionItem;
        var start = _previewTransformStart;
        var working = _previewWorkingTransform;
        if (item == null || start == null || working == null || _previewManipulationMode == PreviewManipulationMode.None) return;
        var final = PreviewTransformSnapshot.From(working);
        HidePreviewSnapGuides();
        _previewManipulationMode = PreviewManipulationMode.None;
        _previewTransformStart = null;
        _previewWorkingTransform = null;

        if (final != start)
        {
            Execute(new UpdateTransformCommand
            {
                ItemId = item.Id,
                NewTransform = final.ToTransform(),
            });
        }
        UpdatePreviewInteractionOverlay(item);
        if (_timeline != null)
            _ = EnsureTimelineCompositePreviewAsync(_timeline.PlayheadTime.Seconds);
    }

    private void HidePreviewSnapGuides()
    {
        if (_previewSnapVertical != null) _previewSnapVertical.Visibility = Visibility.Collapsed;
        if (_previewSnapHorizontal != null) _previewSnapHorizontal.Visibility = Visibility.Collapsed;
    }

    private void UpdateInspectorFieldsFromPreview(Transform2D transform)
    {
        try
        {
            _suppressInspectorChangeTracking = true;
            PositionXBox.Text = Format(transform.PositionX);
            PositionYBox.Text = Format(transform.PositionY);
            ScaleXBox.Text = Format(transform.ScaleX);
            ScaleYBox.Text = Format(transform.ScaleY);
            RotationBox.Text = Format(transform.RotationDegrees);
        }
        finally
        {
            _suppressInspectorChangeTracking = false;
        }
    }

    private sealed record PreviewTransformSnapshot(
        double PositionX,
        double PositionY,
        double ScaleX,
        double ScaleY,
        double Rotation,
        double AnchorX,
        double AnchorY)
    {
        public static PreviewTransformSnapshot From(Transform2D transform) => new(
            transform.PositionX,
            transform.PositionY,
            transform.ScaleX,
            transform.ScaleY,
            transform.RotationDegrees,
            transform.AnchorX,
            transform.AnchorY);

        public Transform2D ToTransform() => new()
        {
            PositionX = PositionX,
            PositionY = PositionY,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            RotationDegrees = Rotation,
            AnchorX = AnchorX,
            AnchorY = AnchorY,
        };
    }

    private sealed class BorderThicknessToDoubleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is Thickness thickness ? thickness.Left : 1d;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            System.Windows.Data.Binding.DoNothing;
    }
}
