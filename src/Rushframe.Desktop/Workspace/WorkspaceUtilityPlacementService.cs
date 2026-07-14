using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Workspace;

public static class WorkspaceUtilityPlacementService
{
    public static bool TryFindArea(
        IReadOnlyDictionary<PanelId, PanelGridArea> primaryAreas,
        IReadOnlySet<PanelId> visiblePrimaryPanels,
        bool portrait,
        (int Column, int Row)? preferredCell,
        out PanelGridArea area)
    {
        area = PanelGridArea.Empty;
        if (!WorkspaceGridLayoutPlanner.SupportsWindowCount(visiblePrimaryPanels.Count + 1, portrait))
            return false;

        var occupied = new bool[WorkspaceLayout.GridColumns, WorkspaceLayout.GridRows];
        foreach (var panelId in visiblePrimaryPanels)
        {
            if (!primaryAreas.TryGetValue(panelId, out var panelArea) || !panelArea.IsWithinGrid)
                return false;

            foreach (var cell in panelArea.Cells())
            {
                if (occupied[cell.Column, cell.Row])
                    return false;
                occupied[cell.Column, cell.Row] = true;
            }
        }

        var candidates = new List<PanelGridArea>();
        for (var rowSpan = 1; rowSpan <= WorkspaceLayout.GridRows; rowSpan++)
        for (var columnSpan = 1; columnSpan <= 2; columnSpan++)
        for (var row = 0; row <= WorkspaceLayout.GridRows - rowSpan; row++)
        for (var column = 0; column <= WorkspaceLayout.GridColumns - columnSpan; column++)
        {
            var candidate = new PanelGridArea(column, row, columnSpan, rowSpan);
            if (candidate.Cells().All(cell => !occupied[cell.Column, cell.Row]))
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return false;

        area = candidates
            .OrderByDescending(candidate => preferredCell.HasValue
                && candidate.ContainsCell(preferredCell.Value.Column, preferredCell.Value.Row))
            .ThenByDescending(candidate => candidate.CellCount)
            .ThenBy(candidate => candidate.Row)
            .ThenBy(candidate => candidate.Column)
            .First();
        return true;
    }
}
