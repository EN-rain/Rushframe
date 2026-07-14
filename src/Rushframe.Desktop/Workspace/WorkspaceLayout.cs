using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Workspace;

public enum PanelDockSlot
{
    Unassigned = 0,
    Left = 1,
    Center = 2,
    Bottom = 3,
    Right = 4,
}

public readonly record struct PanelGridArea(
    int Column,
    int Row,
    int ColumnSpan,
    int RowSpan)
{
    public static PanelGridArea Empty => new(-1, -1, 0, 0);

    public bool IsAssigned => Column >= 0 && Row >= 0 && ColumnSpan > 0 && RowSpan > 0;

    public bool IsWithinGrid => IsAssigned
        && Column + ColumnSpan <= WorkspaceLayout.GridColumns
        && Row + RowSpan <= WorkspaceLayout.GridRows;

    public int CellCount => Math.Max(0, ColumnSpan) * Math.Max(0, RowSpan);

    public bool ContainsCell(int column, int row) =>
        column >= Column
        && column < Column + ColumnSpan
        && row >= Row
        && row < Row + RowSpan;

    public IEnumerable<(int Column, int Row)> Cells()
    {
        for (var row = Row; row < Row + RowSpan; row++)
        for (var column = Column; column < Column + ColumnSpan; column++)
            yield return (column, row);
    }
}

public sealed class WorkspaceLayout
{
    private static readonly PanelId[] DockablePanels =
    [
        PanelId.Media,
        PanelId.Preview,
        PanelId.Timeline,
        PanelId.Inspector,
    ];

    public const int GridColumns = 3;
    public const int GridRows = 2;
    public const int SchemaVersion = 4;

    public int Version { get; init; } = SchemaVersion;

    public required Dictionary<string, PanelState> Panels { get; init; }

    public static WorkspaceLayout Default() => new()
    {
        Panels = PanelRegistry.All.ToDictionary(
            panel => panel.Id.Key,
            panel => new PanelState
            {
                IsOpen = IsOpenByDefault(panel.Id),
                DockSlot = GetDefaultDockSlot(panel.Id),
                LandscapeArea = GetDefaultLandscapeArea(panel.Id),
                PortraitArea = GetDefaultPortraitArea(panel.Id),
            }),
    };

    public bool IsPanelOpen(PanelId id) =>
        Panels.TryGetValue(id.Key, out var state) && state.IsOpen;

    public PanelGridArea GetGridArea(PanelId id, bool portrait)
    {
        if (Panels.TryGetValue(id.Key, out var state))
        {
            var area = portrait ? state.PortraitArea : state.LandscapeArea;
            if (area.IsAssigned) return area;
        }

        return portrait ? GetDefaultPortraitArea(id) : GetDefaultLandscapeArea(id);
    }

    public IReadOnlyDictionary<PanelId, PanelGridArea> GetGridAreas(bool portrait) =>
        DockablePanels.ToDictionary(panelId => panelId, panelId => GetGridArea(panelId, portrait));

    public WorkspaceLayout WithPanelToggled(PanelId id, bool open)
    {
        var definition = PanelRegistry.Find(id);
        if (!open && definition?.CanClose == false) return this;

        var updated = ClonePanels();
        var current = updated.TryGetValue(id.Key, out var state)
            ? state
            : CreateDefaultPanelState(id);
        updated[id.Key] = current with { IsOpen = open };
        return new WorkspaceLayout { Version = SchemaVersion, Panels = updated };
    }

    public WorkspaceLayout WithGridAreas(
        bool portrait,
        IReadOnlyDictionary<PanelId, PanelGridArea> areas)
    {
        var updated = ClonePanels();
        foreach (var panelId in DockablePanels)
        {
            if (!areas.TryGetValue(panelId, out var area)) continue;
            var current = updated.TryGetValue(panelId.Key, out var state)
                ? state
                : CreateDefaultPanelState(panelId);
            updated[panelId.Key] = portrait
                ? current with { PortraitArea = area }
                : current with { LandscapeArea = area };
        }

        return new WorkspaceLayout { Version = SchemaVersion, Panels = updated };
    }

    public WorkspaceLayout Normalize()
    {
        var normalized = new Dictionary<string, PanelState>(StringComparer.Ordinal);
        foreach (var definition in PanelRegistry.All)
        {
            var state = Panels.TryGetValue(definition.Id.Key, out var existing)
                ? existing
                : CreateDefaultPanelState(definition.Id);
            normalized[definition.Id.Key] = definition.CanClose
                ? state
                : state with { IsOpen = true };
        }

        var migrated = new WorkspaceLayout
        {
            Version = SchemaVersion,
            Panels = normalized,
        };

        if (Version <= 3)
            migrated = migrated.WithMigratedLegacyAreas();

        if (!IsValidGridLayout(migrated.GetGridAreas(portrait: false), portrait: false))
            migrated = migrated.WithGridAreas(portrait: false, GetDefaultLandscapeAreas());
        if (!IsValidGridLayout(migrated.GetGridAreas(portrait: true), portrait: true))
            migrated = migrated.WithGridAreas(portrait: true, GetDefaultPortraitAreas());

        return migrated;
    }

    public static bool IsValidGridLayout(
        IReadOnlyDictionary<PanelId, PanelGridArea> areas,
        bool portrait)
    {
        if (DockablePanels.Any(panelId => !areas.TryGetValue(panelId, out var area) || !area.IsWithinGrid))
            return false;

        var timeline = areas[PanelId.Timeline];
        if (timeline.ColumnSpan != 2 || timeline.RowSpan != 1)
            return false;

        var preview = areas[PanelId.Preview];
        if (portrait && (preview.ColumnSpan != 1
            || preview.RowSpan != 2
            || preview.Row != 0
            || preview.Column is not (0 or 2)))
            return false;

        var occupied = new HashSet<(int Column, int Row)>();
        foreach (var panelId in DockablePanels)
        {
            foreach (var cell in areas[panelId].Cells())
            {
                if (!occupied.Add(cell)) return false;
            }
        }

        return occupied.Count == GridColumns * GridRows;
    }

    public static PanelGridArea GetDefaultLandscapeArea(PanelId id) =>
        id == PanelId.Media ? new PanelGridArea(0, 0, 1, 1)
        : id == PanelId.Preview ? new PanelGridArea(1, 0, 1, 1)
        : id == PanelId.Timeline ? new PanelGridArea(0, 1, 2, 1)
        : id == PanelId.Inspector ? new PanelGridArea(2, 0, 1, 2)
        : PanelGridArea.Empty;

    public static PanelGridArea GetDefaultPortraitArea(PanelId id) =>
        id == PanelId.Media ? new PanelGridArea(0, 0, 1, 1)
        : id == PanelId.Inspector ? new PanelGridArea(1, 0, 1, 1)
        : id == PanelId.Timeline ? new PanelGridArea(0, 1, 2, 1)
        : id == PanelId.Preview ? new PanelGridArea(2, 0, 1, 2)
        : PanelGridArea.Empty;

    public static PanelDockSlot GetDefaultDockSlot(PanelId id) =>
        id == PanelId.Media ? PanelDockSlot.Left
        : id == PanelId.Preview ? PanelDockSlot.Center
        : id == PanelId.Timeline ? PanelDockSlot.Bottom
        : id == PanelId.Inspector ? PanelDockSlot.Right
        : PanelDockSlot.Unassigned;

    private WorkspaceLayout WithMigratedLegacyAreas()
    {
        var slotByPanel = DockablePanels.ToDictionary(
            panelId => panelId,
            panelId => Panels.TryGetValue(panelId.Key, out var state) && state.DockSlot != PanelDockSlot.Unassigned
                ? state.DockSlot
                : GetDefaultDockSlot(panelId));

        var landscape = new Dictionary<PanelId, PanelGridArea>
        {
            [PanelId.Media] = AreaForLegacySlot(slotByPanel[PanelId.Media]),
            [PanelId.Preview] = AreaForLegacySlot(slotByPanel[PanelId.Preview]),
            [PanelId.Timeline] = AreaForLegacySlot(slotByPanel[PanelId.Timeline]),
            [PanelId.Inspector] = AreaForLegacySlot(slotByPanel[PanelId.Inspector]),
        };
        if (!IsValidGridLayout(landscape, portrait: false))
            landscape = GetDefaultLandscapeAreas();

        var previewOnLeft = slotByPanel[PanelId.Preview] == PanelDockSlot.Left;
        var previewColumn = previewOnLeft ? 0 : 2;
        var remainingColumns = previewOnLeft ? new[] { 1, 2 } : new[] { 0, 1 };
        var upperPanels = new[] { PanelId.Media, PanelId.Inspector }
            .OrderBy(panelId => LegacySlotOrder(slotByPanel[panelId]))
            .ToArray();
        var portrait = new Dictionary<PanelId, PanelGridArea>
        {
            [PanelId.Preview] = new PanelGridArea(previewColumn, 0, 1, 2),
            [PanelId.Timeline] = new PanelGridArea(remainingColumns[0], 1, 2, 1),
            [upperPanels[0]] = new PanelGridArea(remainingColumns[0], 0, 1, 1),
            [upperPanels[1]] = new PanelGridArea(remainingColumns[1], 0, 1, 1),
        };

        return WithGridAreas(portrait: false, landscape)
            .WithGridAreas(portrait: true, portrait);
    }

    private static PanelGridArea AreaForLegacySlot(PanelDockSlot slot) =>
        slot == PanelDockSlot.Left ? new PanelGridArea(0, 0, 1, 1)
        : slot == PanelDockSlot.Center ? new PanelGridArea(1, 0, 1, 1)
        : slot == PanelDockSlot.Bottom ? new PanelGridArea(0, 1, 2, 1)
        : slot == PanelDockSlot.Right ? new PanelGridArea(2, 0, 1, 2)
        : PanelGridArea.Empty;

    private static int LegacySlotOrder(PanelDockSlot slot) => slot switch
    {
        PanelDockSlot.Left => 0,
        PanelDockSlot.Center => 1,
        PanelDockSlot.Right => 2,
        PanelDockSlot.Bottom => 3,
        _ => 4,
    };

    private static Dictionary<PanelId, PanelGridArea> GetDefaultLandscapeAreas() =>
        DockablePanels.ToDictionary(panelId => panelId, GetDefaultLandscapeArea);

    private static Dictionary<PanelId, PanelGridArea> GetDefaultPortraitAreas() =>
        DockablePanels.ToDictionary(panelId => panelId, GetDefaultPortraitArea);

    private static PanelState CreateDefaultPanelState(PanelId id) => new()
    {
        IsOpen = IsOpenByDefault(id),
        DockSlot = GetDefaultDockSlot(id),
        LandscapeArea = GetDefaultLandscapeArea(id),
        PortraitArea = GetDefaultPortraitArea(id),
    };

    private static bool IsOpenByDefault(PanelId id) =>
        id != PanelId.RenderQueue
        && id != PanelId.MediaIntelligence
        && id != PanelId.ProductionWorkflow
        && id != PanelId.TranscriptEditor
        && id != PanelId.OutputVariants
        && id != PanelId.GeneratedCompositions;

    private Dictionary<string, PanelState> ClonePanels() =>
        Panels.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}

public sealed record PanelState
{
    public bool IsOpen { get; init; } = true;
    public double Width { get; init; } = 250;
    public double Height { get; init; } = 200;
    public PanelDockSlot DockSlot { get; init; } = PanelDockSlot.Unassigned;
    public PanelGridArea LandscapeArea { get; init; } = PanelGridArea.Empty;
    public PanelGridArea PortraitArea { get; init; } = PanelGridArea.Empty;
}
