using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Workspace;

public static class WorkspaceVisibleLayoutService
{
    private static readonly PanelId[] StablePanelOrder =
    [
        PanelId.Timeline,
        PanelId.Preview,
        PanelId.Media,
        PanelId.Inspector,
    ];

    public static IReadOnlyDictionary<PanelId, PanelGridArea> Resolve(
        IReadOnlyDictionary<PanelId, PanelGridArea> storedAreas,
        IReadOnlySet<PanelId> visiblePanels,
        bool portrait,
        PanelGridArea reservedArea)
    {
        var visible = StablePanelOrder.Where(visiblePanels.Contains).ToArray();
        if (visible.Length == 0)
            return new Dictionary<PanelId, PanelGridArea>();

        var reservedCells = reservedArea.IsWithinGrid
            ? reservedArea.Cells().ToHashSet()
            : [];
        var storedVisibleAreas = visible.ToDictionary(
            panelId => panelId,
            panelId => storedAreas.TryGetValue(panelId, out var area) ? area : PanelGridArea.Empty);
        if (!HasOverlaps(storedVisibleAreas, reservedArea)
            && storedVisibleAreas.Values.Sum(area => area.CellCount) + reservedCells.Count
                == WorkspaceLayout.GridColumns * WorkspaceLayout.GridRows)
            return storedVisibleAreas;

        var candidates = new Dictionary<PanelId, PanelGridArea>();
        var occupied = new HashSet<(int Column, int Row)>();
        Dictionary<PanelId, PanelGridArea>? best = null;
        var bestScore = double.PositiveInfinity;

        void Build(int index)
        {
            if (index == visible.Length)
            {
                var score = Score(candidates, storedAreas, visible, portrait, reservedCells);
                if (score >= bestScore) return;
                bestScore = score;
                best = new Dictionary<PanelId, PanelGridArea>(candidates);
                return;
            }

            var panelId = visible[index];
            foreach (var area in CandidateAreas(panelId, portrait, storedAreas))
            {
                var cells = area.Cells().ToArray();
                if (cells.Any(reservedCells.Contains) || cells.Any(occupied.Contains))
                    continue;

                candidates[panelId] = area;
                foreach (var cell in cells) occupied.Add(cell);
                Build(index + 1);
                foreach (var cell in cells) occupied.Remove(cell);
                candidates.Remove(panelId);
            }
        }

        Build(0);
        return best ?? visible.ToDictionary(
            panelId => panelId,
            panelId => storedAreas.TryGetValue(panelId, out var area) ? area : PanelGridArea.Empty);
    }

    public static bool HasOverlaps(
        IReadOnlyDictionary<PanelId, PanelGridArea> areas,
        PanelGridArea reservedArea)
    {
        var occupied = reservedArea.IsWithinGrid
            ? reservedArea.Cells().ToHashSet()
            : [];
        foreach (var area in areas.Values)
        {
            if (!area.IsWithinGrid) return true;
            foreach (var cell in area.Cells())
                if (!occupied.Add(cell)) return true;
        }
        return false;
    }

    private static IEnumerable<PanelGridArea> CandidateAreas(
        PanelId panelId,
        bool portrait,
        IReadOnlyDictionary<PanelId, PanelGridArea> storedAreas)
    {
        if (storedAreas.TryGetValue(panelId, out var stored) && stored.IsWithinGrid)
            yield return stored;

        if (panelId == PanelId.Timeline)
        {
            for (var row = 0; row < WorkspaceLayout.GridRows; row++)
            for (var column = 0; column <= WorkspaceLayout.GridColumns - 2; column++)
            {
                var area = new PanelGridArea(column, row, 2, 1);
                if (area != stored) yield return area;
            }
            yield break;
        }

        if (portrait && panelId == PanelId.Preview)
        {
            foreach (var column in new[] { 0, 2 })
            {
                var area = new PanelGridArea(column, 0, 1, 2);
                if (area != stored) yield return area;
            }
            yield break;
        }

        for (var rowSpan = 1; rowSpan <= WorkspaceLayout.GridRows; rowSpan++)
        for (var columnSpan = 1; columnSpan <= 2; columnSpan++)
        for (var row = 0; row <= WorkspaceLayout.GridRows - rowSpan; row++)
        for (var column = 0; column <= WorkspaceLayout.GridColumns - columnSpan; column++)
        {
            var area = new PanelGridArea(column, row, columnSpan, rowSpan);
            if (area != stored) yield return area;
        }
    }

    private static double Score(
        IReadOnlyDictionary<PanelId, PanelGridArea> candidate,
        IReadOnlyDictionary<PanelId, PanelGridArea> stored,
        IReadOnlyCollection<PanelId> visible,
        bool portrait,
        IReadOnlySet<(int Column, int Row)> reservedCells)
    {
        var occupied = candidate.Values.SelectMany(area => area.Cells()).ToHashSet();
        var usableCellCount = WorkspaceLayout.GridColumns * WorkspaceLayout.GridRows - reservedCells.Count;
        var uncovered = Math.Max(0, usableCellCount - occupied.Count);
        var overlapPenalty = occupied.Count != candidate.Values.Sum(area => area.CellCount) ? 1_000_000 : 0;

        var previewReward = !portrait && candidate.TryGetValue(PanelId.Preview, out var preview)
            ? preview.CellCount * 750
            : 0;
        var emptyAdjacentToPreview = !portrait && candidate.TryGetValue(PanelId.Preview, out preview)
            ? CountAdjacentEmpty(preview, occupied, reservedCells)
            : 0;

        var movement = 0d;
        var shapeChange = 0d;
        var changed = 0;
        foreach (var panelId in visible)
        {
            if (!stored.TryGetValue(panelId, out var original) || !candidate.TryGetValue(panelId, out var area))
                continue;
            if (area != original) changed++;
            movement += Math.Abs(CenterColumn(area) - CenterColumn(original))
                + Math.Abs(CenterRow(area) - CenterRow(original));
            shapeChange += Math.Abs(area.ColumnSpan - original.ColumnSpan)
                + Math.Abs(area.RowSpan - original.RowSpan);
        }

        return overlapPenalty
            + uncovered * 100_000
            + emptyAdjacentToPreview * 10_000
            + changed * 100
            + shapeChange * 10
            + movement
            - previewReward;
    }

    private static int CountAdjacentEmpty(
        PanelGridArea area,
        IReadOnlySet<(int Column, int Row)> occupied,
        IReadOnlySet<(int Column, int Row)> reserved)
    {
        var adjacent = new HashSet<(int Column, int Row)>();
        foreach (var cell in area.Cells())
        {
            foreach (var candidate in new[]
            {
                (cell.Column - 1, cell.Row),
                (cell.Column + 1, cell.Row),
                (cell.Column, cell.Row - 1),
                (cell.Column, cell.Row + 1),
            })
            {
                if (candidate.Item1 < 0 || candidate.Item1 >= WorkspaceLayout.GridColumns
                    || candidate.Item2 < 0 || candidate.Item2 >= WorkspaceLayout.GridRows
                    || occupied.Contains(candidate)
                    || reserved.Contains(candidate))
                    continue;
                adjacent.Add(candidate);
            }
        }
        return adjacent.Count;
    }

    private static double CenterColumn(PanelGridArea area) => area.Column + area.ColumnSpan / 2d;

    private static double CenterRow(PanelGridArea area) => area.Row + area.RowSpan / 2d;
}
