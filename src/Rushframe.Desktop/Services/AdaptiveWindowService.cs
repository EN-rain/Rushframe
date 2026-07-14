using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop.Services;

public readonly record struct AdaptiveWindowMinimum(double Width, double Height);

public static class AdaptiveWindowService
{
    public const double BaseMinimumWindowWidth = 1120;
    public const double BaseMinimumWindowHeight = 620;
    public const double CompactPanelWidth = 360;

    private const double VerticalSplitterWidth = 4;
    private const double HorizontalSplitterHeight = 4;

    public static AdaptiveWindowMinimum GetWindowMinimum(
        double uiScale,
        double availableScreenWidth,
        double availableScreenHeight)
    {
        var scale = double.IsFinite(uiScale) ? Math.Clamp(uiScale, 0.5, 2) : 1;
        var screenWidth = double.IsFinite(availableScreenWidth) ? Math.Max(0, availableScreenWidth) : 0;
        var screenHeight = double.IsFinite(availableScreenHeight) ? Math.Max(0, availableScreenHeight) : 0;
        return new AdaptiveWindowMinimum(
            Math.Min(BaseMinimumWindowWidth * scale, screenWidth),
            Math.Min(BaseMinimumWindowHeight * scale, screenHeight));
    }

    public static int GetDensePanelColumnCount(double availableWidth) =>
        double.IsFinite(availableWidth) && availableWidth >= CompactPanelWidth ? 2 : 1;

    public static bool CanHostSeparateUtility(
        double availableWidth,
        double availableHeight,
        IReadOnlyDictionary<PanelId, PanelGridArea> primaryAreas,
        IReadOnlySet<PanelId> visiblePrimaryPanels,
        PanelGridArea utilityArea,
        bool portrait)
    {
        if (!double.IsFinite(availableWidth)
            || !double.IsFinite(availableHeight)
            || availableWidth <= 0
            || availableHeight <= 0
            || !utilityArea.IsWithinGrid)
            return false;

        var occupied = new HashSet<(int Column, int Row)>();
        foreach (var panelId in visiblePrimaryPanels)
        {
            if (!primaryAreas.TryGetValue(panelId, out var area) || !area.IsWithinGrid)
                return false;
            foreach (var cell in area.Cells())
                if (!occupied.Add(cell)) return false;

            var size = GetAreaSize(availableWidth, availableHeight, area);
            if (!MeetsPanelMinimum(panelId, size.Width, size.Height, portrait))
                return false;
        }

        foreach (var cell in utilityArea.Cells())
            if (!occupied.Add(cell)) return false;

        var utilitySize = GetAreaSize(availableWidth, availableHeight, utilityArea);
        return utilitySize.Width >= 300 && utilitySize.Height >= 235;
    }

    public static (double Width, double Height) GetAreaSize(
        double availableWidth,
        double availableHeight,
        PanelGridArea area)
    {
        if (!area.IsWithinGrid || availableWidth <= 0 || availableHeight <= 0)
            return (0, 0);

        var usableWidth = Math.Max(0, availableWidth - (WorkspaceLayout.GridColumns - 1) * VerticalSplitterWidth);
        var usableHeight = Math.Max(0, availableHeight - (WorkspaceLayout.GridRows - 1) * HorizontalSplitterHeight);
        var cellWidth = usableWidth / WorkspaceLayout.GridColumns;
        var cellHeight = usableHeight / WorkspaceLayout.GridRows;
        return (
            cellWidth * area.ColumnSpan + VerticalSplitterWidth * Math.Max(0, area.ColumnSpan - 1),
            cellHeight * area.RowSpan + HorizontalSplitterHeight * Math.Max(0, area.RowSpan - 1));
    }

    private static bool MeetsPanelMinimum(
        PanelId panelId,
        double width,
        double height,
        bool portrait) =>
        panelId == PanelId.Preview
            ? width >= (portrait ? 250 : 300) && height >= (portrait ? 420 : 205)
        : panelId == PanelId.Timeline
            ? width >= 540 && height >= 185
        : panelId == PanelId.Media
            ? width >= 250 && height >= 210
        : panelId == PanelId.Inspector
            ? width >= 280 && height >= 235
        : true;
}
