using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Workspace;

public static class WorkspaceGridLayoutPlanner
{
    public const int MaximumLandscapeWindows = 5;
    public const int MaximumPortraitWindows = 4;

    private static readonly IReadOnlyList<IReadOnlyDictionary<PanelId, PanelGridArea>> LandscapeLayouts =
        GenerateLayouts(portrait: false);

    private static readonly IReadOnlyList<IReadOnlyDictionary<PanelId, PanelGridArea>> PortraitLayouts =
        GenerateLayouts(portrait: true);

    public static bool TryMovePanel(
        WorkspaceLayout layout,
        PanelId source,
        int targetColumn,
        int targetRow,
        bool portrait,
        out WorkspaceLayout updated,
        out PanelGridArea destination)
    {
        updated = layout;
        destination = layout.GetGridArea(source, portrait);
        if (targetColumn is < 0 or >= WorkspaceLayout.GridColumns
            || targetRow is < 0 or >= WorkspaceLayout.GridRows)
            return false;

        if (portrait && source == PanelId.Preview)
            targetColumn = targetColumn <= 1 ? 0 : 2;

        var current = layout.GetGridAreas(portrait);
        var candidates = portrait ? PortraitLayouts : LandscapeLayouts;
        var best = candidates
            .Where(candidate => candidate[source].ContainsCell(targetColumn, targetRow))
            .Where(candidate => candidate[source] != current[source])
            .Where(candidate => !LayoutsEqual(candidate, current))
            .Select(candidate => new
            {
                Layout = candidate,
                Score = Score(candidate, current, source, targetColumn, targetRow),
            })
            .OrderBy(candidate => candidate.Score)
            .FirstOrDefault();

        if (best == null) return false;

        destination = best.Layout[source];
        updated = layout.WithGridAreas(portrait, best.Layout).Normalize();
        return true;
    }

    public static IReadOnlyList<IReadOnlyDictionary<PanelId, PanelGridArea>> GetValidLayouts(bool portrait) =>
        portrait ? PortraitLayouts : LandscapeLayouts;

    public static bool SupportsWindowCount(int count, bool portrait) =>
        count >= 0 && count <= (portrait ? MaximumPortraitWindows : MaximumLandscapeWindows);

    private static IReadOnlyList<IReadOnlyDictionary<PanelId, PanelGridArea>> GenerateLayouts(bool portrait)
    {
        var panelOrder = portrait
            ? new[] { PanelId.Preview, PanelId.Timeline, PanelId.Media, PanelId.Inspector }
            : new[] { PanelId.Timeline, PanelId.Media, PanelId.Preview, PanelId.Inspector };
        var layouts = new List<IReadOnlyDictionary<PanelId, PanelGridArea>>();
        var selected = new Dictionary<PanelId, PanelGridArea>();
        var occupied = new HashSet<(int Column, int Row)>();

        void Build(int index)
        {
            if (index == panelOrder.Length)
            {
                if (occupied.Count != WorkspaceLayout.GridColumns * WorkspaceLayout.GridRows)
                    return;
                layouts.Add(new Dictionary<PanelId, PanelGridArea>(selected));
                return;
            }

            var panelId = panelOrder[index];
            foreach (var area in GetCandidateAreas(panelId, portrait))
            {
                var cells = area.Cells().ToArray();
                if (cells.Any(occupied.Contains)) continue;

                selected[panelId] = area;
                foreach (var cell in cells) occupied.Add(cell);
                Build(index + 1);
                foreach (var cell in cells) occupied.Remove(cell);
                selected.Remove(panelId);
            }
        }

        Build(0);
        return layouts
            .Where(layout => WorkspaceLayout.IsValidGridLayout(layout, portrait))
            .ToArray();
    }

    private static IEnumerable<PanelGridArea> GetCandidateAreas(PanelId panelId, bool portrait)
    {
        if (panelId == PanelId.Timeline)
        {
            for (var row = 0; row < WorkspaceLayout.GridRows; row++)
            for (var column = 0; column <= WorkspaceLayout.GridColumns - 2; column++)
                yield return new PanelGridArea(column, row, 2, 1);
            yield break;
        }

        if (portrait && panelId == PanelId.Preview)
        {
            yield return new PanelGridArea(0, 0, 1, 2);
            yield return new PanelGridArea(2, 0, 1, 2);
            yield break;
        }

        for (var rowSpan = 1; rowSpan <= WorkspaceLayout.GridRows; rowSpan++)
        for (var columnSpan = 1; columnSpan <= 2; columnSpan++)
        for (var row = 0; row <= WorkspaceLayout.GridRows - rowSpan; row++)
        for (var column = 0; column <= WorkspaceLayout.GridColumns - columnSpan; column++)
            yield return new PanelGridArea(column, row, columnSpan, rowSpan);
    }

    private static double Score(
        IReadOnlyDictionary<PanelId, PanelGridArea> candidate,
        IReadOnlyDictionary<PanelId, PanelGridArea> current,
        PanelId source,
        int targetColumn,
        int targetRow)
    {
        var sourceArea = candidate[source];
        var targetDistance = Math.Abs(CenterColumn(sourceArea) - (targetColumn + 0.5))
            + Math.Abs(CenterRow(sourceArea) - (targetRow + 0.5));
        var changedPanels = candidate.Count(pair => current[pair.Key] != pair.Value);
        var totalMovement = candidate.Sum(pair =>
            Math.Abs(CenterColumn(pair.Value) - CenterColumn(current[pair.Key]))
            + Math.Abs(CenterRow(pair.Value) - CenterRow(current[pair.Key])));
        var shapeChange = candidate.Sum(pair =>
            Math.Abs(pair.Value.ColumnSpan - current[pair.Key].ColumnSpan)
            + Math.Abs(pair.Value.RowSpan - current[pair.Key].RowSpan));

        return targetDistance * 1000
            + changedPanels * 100
            + shapeChange * 10
            + totalMovement;
    }

    private static bool LayoutsEqual(
        IReadOnlyDictionary<PanelId, PanelGridArea> first,
        IReadOnlyDictionary<PanelId, PanelGridArea> second) =>
        first.All(pair => second.TryGetValue(pair.Key, out var area) && area == pair.Value);

    private static double CenterColumn(PanelGridArea area) => area.Column + area.ColumnSpan / 2d;

    private static double CenterRow(PanelGridArea area) => area.Row + area.RowSpan / 2d;
}
