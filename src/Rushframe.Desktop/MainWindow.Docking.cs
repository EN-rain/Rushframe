using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private PanelTitleDragState? _panelTitleDrag;
    private WorkspaceLayout? _panelDropLayout;
    private PanelGridArea? _panelDropArea;
    private PanelGridArea? _utilityDropArea;

    private void InitializePanelDocking()
    {
        RegisterPanelTitleBar(MediaWindowDragHandle, PanelId.Media);
        RegisterPanelTitleBar(PreviewWindowDragHandle, PanelId.Preview);
        RegisterPanelTitleBar(TimelineWindowDragHandle, PanelId.Timeline);
        RegisterPanelTitleBar(InspectorWindowDragHandle, PanelId.Inspector);
        RegisterPanelTitleBar(UtilityWindowDragHandle, UtilityWindowPanelId);
        PreviewKeyDown += PanelDocking_PreviewKeyDown;
    }

    private void RegisterPanelTitleBar(FrameworkElement titleBar, PanelId panelId)
    {
        titleBar.Tag = panelId;
        titleBar.PreviewMouseLeftButtonDown += PanelTitleBar_PreviewMouseLeftButtonDown;
        titleBar.PreviewMouseMove += PanelTitleBar_PreviewMouseMove;
        titleBar.PreviewMouseLeftButtonUp += PanelTitleBar_PreviewMouseLeftButtonUp;
        titleBar.LostMouseCapture += PanelTitleBar_LostMouseCapture;
    }

    private void PanelTitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_previewFullscreen
            || sender is not FrameworkElement titleBar
            || titleBar.Tag is not PanelId panelId
            || e.ChangedButton != MouseButton.Left
            || IsInteractiveHeaderElement(e.OriginalSource as DependencyObject, titleBar))
            return;

        _panelTitleDrag = new PanelTitleDragState(panelId, titleBar, e.GetPosition(MainGrid), false);
        Mouse.OverrideCursor = Cursors.Hand;
        titleBar.CaptureMouse();
        e.Handled = true;
    }

    private void PanelTitleBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panelTitleDrag is not { } drag || !ReferenceEquals(sender, drag.TitleBar)) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CompletePanelTitleDrag(commit: false);
            return;
        }

        var position = e.GetPosition(MainGrid);
        if (!drag.Active)
        {
            var delta = position - drag.Start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            drag = drag with { Active = true };
            _panelTitleDrag = drag;
            PanelDockOverlay.Visibility = Visibility.Visible;
            PanelDockDragLabel.Text = drag.PanelId == UtilityWindowPanelId
                ? $"Move {UtilityWindowTitleText.Text}"
                : $"Move {PanelRegistry.Find(drag.PanelId)?.Title ?? "panel"}";
        }

        _panelDropLayout = null;
        _panelDropArea = null;
        _utilityDropArea = null;
        if (TryGetAdaptiveGridCell(position, out var column, out var row))
        {
            if (drag.PanelId == UtilityWindowPanelId)
            {
                var visiblePrimaryPanels = GetVisiblePrimaryPanels(
                    _layout.IsPanelOpen(PanelId.Media),
                    _layout.IsPanelOpen(PanelId.Preview),
                    _layout.IsPanelOpen(PanelId.Inspector));
                if (TryResolveSeparateUtilityArea(
                    _previewWindowPortrait,
                    visiblePrimaryPanels,
                    activityOpen: true,
                    out var utilityDestination,
                    preferredCell: (column, row))
                    && utilityDestination != _utilityWindowArea)
                {
                    _utilityDropArea = utilityDestination;
                    _panelDropArea = utilityDestination;
                }
            }
            else
            {
                if (_previewWindowPortrait && drag.PanelId == PanelId.Preview && column == 1)
                    column = position.X < MainGrid.ActualWidth / 2 ? 0 : 2;

                if (WorkspaceGridLayoutPlanner.TryMovePanel(
                    _layout,
                    drag.PanelId,
                    column,
                    row,
                    _previewWindowPortrait,
                    out var proposed,
                    out var destination))
                {
                    _panelDropLayout = proposed;
                    _panelDropArea = destination;
                }
            }
        }

        UpdatePanelDockOverlay(position, _panelDropArea);
        e.Handled = true;
    }

    private void PanelTitleBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panelTitleDrag is not { } drag
            || !ReferenceEquals(sender, drag.TitleBar)
            || e.ChangedButton != MouseButton.Left)
            return;

        CompletePanelTitleDrag(commit: drag.Active);
        e.Handled = true;
    }

    private void PanelTitleBar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_panelTitleDrag is { } drag && ReferenceEquals(sender, drag.TitleBar))
            ResetPanelTitleDrag();
    }

    private void PanelDocking_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _panelTitleDrag == null) return;
        CompletePanelTitleDrag(commit: false);
        e.Handled = true;
    }

    private void CompletePanelTitleDrag(bool commit)
    {
        var drag = _panelTitleDrag;
        var proposed = _panelDropLayout;
        var utilityDestination = _utilityDropArea;
        ResetPanelTitleDrag();
        if (!commit || drag == null) return;

        if (drag.Value.PanelId == UtilityWindowPanelId)
        {
            if (utilityDestination == null) return;
            _utilityWindowPreferredCell = (utilityDestination.Value.Column, utilityDestination.Value.Row);
            ApplyLayout();
            StatusText.Text = $"Moved {UtilityWindowTitleText.Text} window to column {utilityDestination.Value.Column + 1}, row {utilityDestination.Value.Row + 1}";
            return;
        }

        if (proposed == null) return;
        _layout = proposed.Normalize();
        ApplyLayout();
        SaveLayout();
        var destination = _layout.GetGridArea(drag.Value.PanelId, _previewWindowPortrait);
        StatusText.Text = $"Moved {PanelRegistry.Find(drag.Value.PanelId)?.Title} window to column {destination.Column + 1}, row {destination.Row + 1}";
    }

    private void ResetPanelTitleDrag()
    {
        var titleBar = _panelTitleDrag?.TitleBar;
        _panelTitleDrag = null;
        _panelDropLayout = null;
        _panelDropArea = null;
        _utilityDropArea = null;
        PanelDockOverlay.Visibility = Visibility.Collapsed;
        PanelDockTargetHighlight.Visibility = Visibility.Collapsed;
        PanelDockDragBadge.Visibility = Visibility.Collapsed;
        if (titleBar?.IsMouseCaptured == true)
            titleBar.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
    }

    private void UpdatePanelDockOverlay(Point position, PanelGridArea? destination)
    {
        PanelDockDragBadge.Visibility = Visibility.Visible;
        Canvas.SetLeft(PanelDockDragBadge, Math.Clamp(position.X + 14, 0, Math.Max(0, MainGrid.ActualWidth - 170)));
        Canvas.SetTop(PanelDockDragBadge, Math.Clamp(position.Y + 14, 0, Math.Max(0, MainGrid.ActualHeight - 40)));

        if (destination == null)
        {
            PanelDockTargetHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var bounds = GetAdaptiveGridAreaBounds(destination.Value);
        PanelDockTargetHighlight.Visibility = Visibility.Visible;
        PanelDockTargetHighlight.Width = bounds.Width;
        PanelDockTargetHighlight.Height = bounds.Height;
        Canvas.SetLeft(PanelDockTargetHighlight, bounds.X);
        Canvas.SetTop(PanelDockTargetHighlight, bounds.Y);
    }

    private FrameworkElement GetPanelWindowRoot(PanelId panelId) =>
        panelId == PanelId.Media ? MediaBorder
        : panelId == PanelId.Preview ? PreviewBorder
        : panelId == PanelId.Timeline ? TimelineWindow
        : panelId == UtilityWindowPanelId ? UtilityWindowHost
        : RightPanelHost;

    private static bool IsInteractiveHeaderElement(DependencyObject? source, FrameworkElement titleBar)
    {
        for (var current = source; current != null && !ReferenceEquals(current, titleBar); current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or TextBoxBase or Selector or RangeBase or ScrollBar)
                return true;
        }
        return false;
    }

    private readonly record struct PanelTitleDragState(
        PanelId PanelId,
        FrameworkElement TitleBar,
        Point Start,
        bool Active);
}
