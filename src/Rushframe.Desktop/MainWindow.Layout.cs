using System.Windows;
using System.Windows.Controls;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private void ApplyWindowSizeGuardrails()
    {
        var workArea = SystemParameters.WorkArea;
        var minimum = AdaptiveWindowService.GetWindowMinimum(
            _settings.UiScale,
            workArea.Width,
            workArea.Height);
        MinWidth = minimum.Width;
        MinHeight = minimum.Height;
    }

    private void ApplyAdaptiveGridPlacements(bool portrait)
    {
        ResetAdaptiveGridTracks();
        foreach (var panelId in new[] { PanelId.Media, PanelId.Preview, PanelId.Timeline, PanelId.Inspector })
        {
            if (!_effectivePrimaryAreas.TryGetValue(panelId, out var area)) continue;
            PlacePanelInGrid(panelId, area);
        }
    }

    private void ResetAdaptiveGridTracks()
    {
        MediaColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
        InspectorColumn.Width = new GridLength(1, GridUnitType.Star);
        WorkspaceRow.Height = new GridLength(1, GridUnitType.Star);
        TimelineRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private void PlacePanelInGrid(PanelId panelId, PanelGridArea area)
    {
        var root = GetPanelWindowRoot(panelId);
        PlaceWindowInGrid(root, area);
    }

    private void PlaceUtilityWindowInGrid(PanelGridArea area) =>
        PlaceWindowInGrid(UtilityWindowHost, area);

    private static void PlaceWindowInGrid(FrameworkElement root, PanelGridArea area)
    {
        Grid.SetColumn(root, area.Column * 2);
        Grid.SetColumnSpan(root, area.ColumnSpan * 2 - 1);
        Grid.SetRow(root, area.Row * 2);
        Grid.SetRowSpan(root, area.RowSpan * 2 - 1);
    }

    private void ConfigureAdaptiveGridSplitters(
        bool portrait,
        bool mediaOpen,
        bool previewOpen,
        bool rightPanelOpen)
    {
        var occupants = new PanelId?[WorkspaceLayout.GridColumns, WorkspaceLayout.GridRows];
        foreach (var panelId in new[] { PanelId.Media, PanelId.Preview, PanelId.Timeline, PanelId.Inspector })
        {
            if (!IsPanelWindowVisible(panelId, mediaOpen, previewOpen, rightPanelOpen)) continue;
            if (!_effectivePrimaryAreas.TryGetValue(panelId, out var area)) continue;
            foreach (var cell in area.Cells())
                occupants[cell.Column, cell.Row] = panelId;
        }
        if (_utilityWindowSeparate && _utilityWindowArea.IsAssigned)
        {
            foreach (var cell in _utilityWindowArea.Cells())
                occupants[cell.Column, cell.Row] = UtilityWindowPanelId;
        }

        ConfigureVerticalSplitter(
            MediaSplitter,
            MediaSplitterColumn,
            boundaryColumn: 1,
            occupants);
        ConfigureVerticalSplitter(
            InspectorSplitter,
            InspectorSplitterColumn,
            boundaryColumn: 2,
            occupants);
        ConfigureHorizontalSplitter(occupants);
        TimelineTasksSplitter.Visibility = Visibility.Collapsed;
    }

    private static bool IsPanelWindowVisible(
        PanelId panelId,
        bool mediaOpen,
        bool previewOpen,
        bool rightPanelOpen) =>
        panelId == PanelId.Media ? mediaOpen
        : panelId == PanelId.Preview ? previewOpen
        : panelId == PanelId.Inspector ? rightPanelOpen
        : true;

    private static void ConfigureVerticalSplitter(
        GridSplitter splitter,
        ColumnDefinition splitterColumn,
        int boundaryColumn,
        PanelId?[,] occupants)
    {
        var topActive = HasBoundary(occupants[boundaryColumn - 1, 0], occupants[boundaryColumn, 0]);
        var bottomActive = HasBoundary(occupants[boundaryColumn - 1, 1], occupants[boundaryColumn, 1]);
        var visible = topActive || bottomActive;

        splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        splitterColumn.Width = visible ? new GridLength(4) : new GridLength(0);
        if (!visible) return;

        if (topActive && bottomActive)
        {
            Grid.SetRow(splitter, 0);
            Grid.SetRowSpan(splitter, 3);
        }
        else if (topActive)
        {
            Grid.SetRow(splitter, 0);
            Grid.SetRowSpan(splitter, 1);
        }
        else
        {
            Grid.SetRow(splitter, 2);
            Grid.SetRowSpan(splitter, 1);
        }
    }

    private void ConfigureHorizontalSplitter(PanelId?[,] occupants)
    {
        var activeColumns = Enumerable.Range(0, WorkspaceLayout.GridColumns)
            .Where(column => HasBoundary(occupants[column, 0], occupants[column, 1]))
            .ToArray();
        if (activeColumns.Length == 0)
        {
            PreviewTimelineSplitter.Visibility = Visibility.Collapsed;
            WorkspaceSplitterRow.Height = new GridLength(0);
            return;
        }

        WorkspaceSplitterRow.Height = new GridLength(4);
        PreviewTimelineSplitter.Visibility = Visibility.Visible;
        var firstColumn = activeColumns[0];
        var lastColumn = activeColumns[^1];
        Grid.SetColumn(PreviewTimelineSplitter, firstColumn * 2);
        Grid.SetColumnSpan(PreviewTimelineSplitter, (lastColumn - firstColumn + 1) * 2 - 1);
        Grid.SetRow(PreviewTimelineSplitter, 1);
        Grid.SetRowSpan(PreviewTimelineSplitter, 1);
    }

    private static bool HasBoundary(PanelId? first, PanelId? second) =>
        first != null && second != null && first != second;

    private bool TryGetAdaptiveGridCell(Point position, out int column, out int row)
    {
        column = FindTrack(position.X, new[]
        {
            MediaColumn.ActualWidth,
            MediaSplitterColumn.ActualWidth,
            PreviewColumn.ActualWidth,
            InspectorSplitterColumn.ActualWidth,
            InspectorColumn.ActualWidth,
        });
        row = FindTrack(position.Y, new[]
        {
            WorkspaceRow.ActualHeight,
            WorkspaceSplitterRow.ActualHeight,
            TimelineRow.ActualHeight,
        });

        if (column < 0 || row < 0) return false;
        column /= 2;
        row /= 2;
        return column is >= 0 and < WorkspaceLayout.GridColumns
            && row is >= 0 and < WorkspaceLayout.GridRows;
    }

    private static int FindTrack(double coordinate, IReadOnlyList<double> trackSizes)
    {
        var offset = 0d;
        for (var index = 0; index < trackSizes.Count; index++)
        {
            var end = offset + trackSizes[index];
            if (coordinate >= offset && coordinate <= end)
            {
                if (index % 2 == 1)
                {
                    var previousDistance = coordinate - offset;
                    var nextDistance = end - coordinate;
                    return previousDistance <= nextDistance ? index - 1 : index + 1;
                }
                return index;
            }
            offset = end;
        }
        return -1;
    }

    private Rect GetAdaptiveGridAreaBounds(PanelGridArea area)
    {
        var columnSizes = new[]
        {
            MediaColumn.ActualWidth,
            MediaSplitterColumn.ActualWidth,
            PreviewColumn.ActualWidth,
            InspectorSplitterColumn.ActualWidth,
            InspectorColumn.ActualWidth,
        };
        var rowSizes = new[]
        {
            WorkspaceRow.ActualHeight,
            WorkspaceSplitterRow.ActualHeight,
            TimelineRow.ActualHeight,
        };
        var physicalColumn = area.Column * 2;
        var physicalColumnSpan = area.ColumnSpan * 2 - 1;
        var physicalRow = area.Row * 2;
        var physicalRowSpan = area.RowSpan * 2 - 1;
        var x = columnSizes.Take(physicalColumn).Sum();
        var y = rowSizes.Take(physicalRow).Sum();
        var width = columnSizes.Skip(physicalColumn).Take(physicalColumnSpan).Sum();
        var height = rowSizes.Skip(physicalRow).Take(physicalRowSpan).Sum();
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }
}
