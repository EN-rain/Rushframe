using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private void OpenCanvasSettings()
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        var result = new Dialogs.CanvasSettingsDialog(this, sequence).Show();
        if (result == null) return;

        Execute(new UpdateSequenceSettingsCommand
        {
            SequenceId = sequence.Id,
            Width = result.Width,
            Height = result.Height,
            FrameRate = result.FrameRate,
            Background = result.Background,
            LayoutGuides = result.Guides,
        });
        RefreshCanvasPreviewAfterSettingsChange();
        StatusText.Text = $"Canvas updated to {result.Width}×{result.Height} at {result.FrameRate.Value:0.###} fps";
    }

    private void TogglePreviewWindowOrientation()
    {
        if (_previewWindowPortrait && !_previewWindowFree)
        {
            _previewWindowPortrait = false;
            _previewWindowFree = false;
        }
        else
        {
            _previewWindowPortrait = true;
            _previewWindowFree = false;
        }

        UpdatePreviewOrientationButton();
        UpdateResponsiveLayout();
        StatusText.Text = $"Preview window switched to {(_previewWindowPortrait ? "portrait" : "landscape")}";
    }

    private void UpdatePreviewOrientationButton()
    {
        var target = _previewWindowPortrait && !_previewWindowFree ? "landscape" : "portrait";
        PreviewOrientationIcon.Data = Geometry.Parse(
            _previewWindowPortrait && !_previewWindowFree
                ? "M2,5 L16,5 L16,13 L2,13 Z M14,8 L14,10"
                : "M5,2 L13,2 L13,16 L5,16 Z M8,14 L10,14");
        PreviewOrientationButton.ToolTip = $"Switch preview window to {target}";
        System.Windows.Automation.AutomationProperties.SetName(
            PreviewOrientationButton,
            $"Switch preview window to {target}");

        PreviewFreeLayoutButton.Background = _previewWindowFree
            ? (Brush)FindResource("SelectionBrush")
            : Brushes.Transparent;
        PreviewFreeLayoutButton.ToolTip = _previewWindowFree
            ? "Return Preview to landscape grid sizing"
            : "Use free adaptive Preview sizing";
    }

    private void RefreshCanvasPreviewAfterSettingsChange()
    {
        RefreshPreviewGuidesOverlay();
        UpdatePreviewInteractionOverlay(_selectedInspectorItem);
        if (_timeline != null)
            _ = EnsureTimelineCompositePreviewAsync(_timeline.PlayheadTime.Seconds);
    }

    private void RefreshPreviewGuidesOverlay()
    {
        PreviewGuidesOverlay.Children.Clear();
        var sequence = _project.MainSequence;
        if (sequence == null || PreviewGuidesToggle.IsChecked != true) return;
        var display = GetPreviewCanvasDisplayRect(sequence);

        foreach (var guide in sequence.LayoutGuides.Where(guide => guide.Enabled))
        {
            var brush = ParseGuideBrush(guide.Color);
            switch (guide.Kind)
            {
                case LayoutGuideKind.Grid:
                    AddGuideLine(display.Left + display.Width / 3, display.Top, display.Left + display.Width / 3, display.Bottom, brush);
                    AddGuideLine(display.Left + display.Width * 2 / 3, display.Top, display.Left + display.Width * 2 / 3, display.Bottom, brush);
                    AddGuideLine(display.Left, display.Top + display.Height / 3, display.Right, display.Top + display.Height / 3, brush);
                    AddGuideLine(display.Left, display.Top + display.Height * 2 / 3, display.Right, display.Top + display.Height * 2 / 3, brush);
                    break;
                case LayoutGuideKind.Center:
                    AddGuideLine(display.Left + display.Width / 2, display.Top, display.Left + display.Width / 2, display.Bottom, brush);
                    AddGuideLine(display.Left, display.Top + display.Height / 2, display.Right, display.Top + display.Height / 2, brush);
                    break;
                default:
                    var left = display.Left + display.Width * Math.Clamp(guide.Left, 0, 0.49);
                    var top = display.Top + display.Height * Math.Clamp(guide.Top, 0, 0.49);
                    var right = display.Right - display.Width * Math.Clamp(guide.Right, 0, 0.49);
                    var bottom = display.Bottom - display.Height * Math.Clamp(guide.Bottom, 0, 0.49);
                    var border = new Border
                    {
                        Width = Math.Max(1, right - left),
                        Height = Math.Max(1, bottom - top),
                        BorderBrush = brush,
                        BorderThickness = new Thickness(1),
                        ToolTip = guide.Name,
                        IsHitTestVisible = false,
                    };
                    border.HorizontalAlignment = HorizontalAlignment.Left;
                    border.VerticalAlignment = VerticalAlignment.Top;
                    border.Margin = new Thickness(left, top, 0, 0);
                    PreviewGuidesOverlay.Children.Add(border);
                    break;
            }
        }
    }

    private void AddGuideLine(double x1, double y1, double x2, double y2, Brush brush)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        PreviewGuidesOverlay.Children.Add(line);
    }

    private static Brush ParseGuideBrush(string? value)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? "#66FFFFFF" : value);
            if (converted is Color color) return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
        }
        return new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
    }
}
