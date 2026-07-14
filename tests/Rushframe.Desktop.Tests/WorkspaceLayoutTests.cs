using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void default_layout_opens_editor_panels_and_closes_utility_drawers()
    {
        var layout = WorkspaceLayout.Default();

        Assert.True(layout.IsPanelOpen(PanelId.Media));
        Assert.True(layout.IsPanelOpen(PanelId.Preview));
        Assert.True(layout.IsPanelOpen(PanelId.Inspector));
        Assert.True(layout.IsPanelOpen(PanelId.Timeline));
        Assert.False(layout.IsPanelOpen(PanelId.RenderQueue));
        Assert.False(layout.IsPanelOpen(PanelId.MediaIntelligence));
    }

    [Fact]
    public void default_landscape_layout_uses_a_three_by_two_grid()
    {
        var layout = WorkspaceLayout.Default();
        var areas = layout.GetGridAreas(portrait: false);

        Assert.Equal(new PanelGridArea(0, 0, 1, 1), areas[PanelId.Media]);
        Assert.Equal(new PanelGridArea(1, 0, 1, 1), areas[PanelId.Preview]);
        Assert.Equal(new PanelGridArea(0, 1, 2, 1), areas[PanelId.Timeline]);
        Assert.Equal(new PanelGridArea(2, 0, 1, 2), areas[PanelId.Inspector]);
        Assert.True(WorkspaceLayout.IsValidGridLayout(areas, portrait: false));
    }

    [Fact]
    public void default_portrait_layout_keeps_preview_on_an_edge_and_timeline_two_cells_wide()
    {
        var layout = WorkspaceLayout.Default();
        var areas = layout.GetGridAreas(portrait: true);

        Assert.Equal(new PanelGridArea(2, 0, 1, 2), areas[PanelId.Preview]);
        Assert.Equal(new PanelGridArea(0, 1, 2, 1), areas[PanelId.Timeline]);
        Assert.True(WorkspaceLayout.IsValidGridLayout(areas, portrait: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void every_generated_layout_obeys_fixed_preview_and_timeline_constraints(bool portrait)
    {
        var layouts = WorkspaceGridLayoutPlanner.GetValidLayouts(portrait);

        Assert.NotEmpty(layouts);
        foreach (var areas in layouts)
        {
            Assert.True(WorkspaceLayout.IsValidGridLayout(areas, portrait));
            Assert.Equal(2, areas[PanelId.Timeline].ColumnSpan);
            Assert.Equal(1, areas[PanelId.Timeline].RowSpan);
            if (!portrait) continue;

            var preview = areas[PanelId.Preview];
            Assert.Equal(1, preview.ColumnSpan);
            Assert.Equal(2, preview.RowSpan);
            Assert.Contains(preview.Column, new[] { 0, 2 });
        }
    }

    [Theory]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    [InlineData(4, true, true)]
    [InlineData(5, true, false)]
    public void adaptive_grid_enforces_orientation_window_limits(int count, bool portrait, bool expected)
    {
        Assert.Equal(expected, WorkspaceGridLayoutPlanner.SupportsWindowCount(count, portrait));
    }

    [Fact]
    public void moving_portrait_preview_to_the_left_repacks_the_other_windows()
    {
        var layout = WorkspaceLayout.Default();

        var moved = WorkspaceGridLayoutPlanner.TryMovePanel(
            layout,
            PanelId.Preview,
            targetColumn: 0,
            targetRow: 0,
            portrait: true,
            out var updated,
            out var destination);

        Assert.True(moved);
        Assert.Equal(new PanelGridArea(0, 0, 1, 2), destination);
        Assert.True(WorkspaceLayout.IsValidGridLayout(updated.GetGridAreas(portrait: true), portrait: true));
        Assert.Equal(2, updated.GetGridArea(PanelId.Timeline, portrait: true).ColumnSpan);
    }

    [Fact]
    public void dropping_a_panel_inside_its_current_area_does_not_repack_other_windows()
    {
        var layout = WorkspaceLayout.Default();

        var moved = WorkspaceGridLayoutPlanner.TryMovePanel(
            layout,
            PanelId.Preview,
            targetColumn: 2,
            targetRow: 1,
            portrait: true,
            out var updated,
            out _);

        Assert.False(moved);
        Assert.Same(layout, updated);
    }

    [Fact]
    public void moving_timeline_preserves_its_two_column_one_row_shape()
    {
        var layout = WorkspaceLayout.Default();

        var moved = WorkspaceGridLayoutPlanner.TryMovePanel(
            layout,
            PanelId.Timeline,
            targetColumn: 1,
            targetRow: 0,
            portrait: false,
            out var updated,
            out var destination);

        Assert.True(moved);
        Assert.Equal(2, destination.ColumnSpan);
        Assert.Equal(1, destination.RowSpan);
        Assert.True(WorkspaceLayout.IsValidGridLayout(updated.GetGridAreas(portrait: false), portrait: false));
    }

    [Fact]
    public void portrait_and_landscape_layouts_are_persisted_independently()
    {
        var layout = WorkspaceLayout.Default();
        Assert.True(WorkspaceGridLayoutPlanner.TryMovePanel(
            layout,
            PanelId.Preview,
            0,
            0,
            portrait: true,
            out var portraitUpdated,
            out _));

        Assert.Equal(
            WorkspaceLayout.GetDefaultLandscapeArea(PanelId.Preview),
            portraitUpdated.GetGridArea(PanelId.Preview, portrait: false));
        Assert.Equal(
            new PanelGridArea(0, 0, 1, 2),
            portraitUpdated.GetGridArea(PanelId.Preview, portrait: true));
    }

    [Fact]
    public void toggle_panel_closes_and_opens_without_changing_grid_areas()
    {
        var layout = WorkspaceLayout.Default();
        var before = layout.GetGridArea(PanelId.Inspector, portrait: false);

        layout = layout.WithPanelToggled(PanelId.Inspector, false);
        Assert.False(layout.IsPanelOpen(PanelId.Inspector));
        Assert.Equal(before, layout.GetGridArea(PanelId.Inspector, portrait: false));

        layout = layout.WithPanelToggled(PanelId.Inspector, true);
        Assert.True(layout.IsPanelOpen(PanelId.Inspector));
        Assert.Equal(before, layout.GetGridArea(PanelId.Inspector, portrait: false));
    }

    [Fact]
    public void preview_and_timeline_cannot_be_closed_through_layout_state()
    {
        var layout = WorkspaceLayout.Default();

        var previewAttempt = layout.WithPanelToggled(PanelId.Preview, false);
        var timelineAttempt = layout.WithPanelToggled(PanelId.Timeline, false);

        Assert.Same(layout, previewAttempt);
        Assert.Same(layout, timelineAttempt);
        Assert.True(layout.IsPanelOpen(PanelId.Preview));
        Assert.True(layout.IsPanelOpen(PanelId.Timeline));
    }

    [Fact]
    public void normalization_reopens_protected_preview_and_timeline_from_older_layouts()
    {
        var original = WorkspaceLayout.Default();
        var panels = original.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
        panels[PanelId.Preview.Key] = panels[PanelId.Preview.Key] with { IsOpen = false };
        panels[PanelId.Timeline.Key] = panels[PanelId.Timeline.Key] with { IsOpen = false };
        var normalized = new WorkspaceLayout
        {
            Version = WorkspaceLayout.SchemaVersion,
            Panels = panels,
        }.Normalize();

        Assert.True(normalized.IsPanelOpen(PanelId.Preview));
        Assert.True(normalized.IsPanelOpen(PanelId.Timeline));
    }

    [Fact]
    public void layout_serialization_round_trips_grid_areas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RushframeTest_" + Guid.NewGuid());
        try
        {
            var service = new WorkspaceLayoutService(dir);
            var original = WorkspaceLayout.Default().WithPanelToggled(PanelId.RenderQueue, true);
            Assert.True(WorkspaceGridLayoutPlanner.TryMovePanel(
                original,
                PanelId.Preview,
                0,
                0,
                portrait: true,
                out original,
                out _));
            service.Save(original);

            var loaded = service.Load();
            Assert.True(loaded.IsPanelOpen(PanelId.RenderQueue));
            Assert.Equal(
                new PanelGridArea(0, 0, 1, 2),
                loaded.GetGridArea(PanelId.Preview, portrait: true));
            Assert.True(WorkspaceLayout.IsValidGridLayout(loaded.GetGridAreas(false), false));
            Assert.True(WorkspaceLayout.IsValidGridLayout(loaded.GetGridAreas(true), true));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void version_three_slot_layout_is_upgraded_to_grid_areas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RushframeTest_" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "workspace-layout.json"),
                "{\"version\":3,\"panels\":{"
                + "\"media\":{\"isOpen\":false,\"dockSlot\":\"left\"},"
                + "\"preview\":{\"isOpen\":true,\"dockSlot\":\"center\"},"
                + "\"timeline\":{\"isOpen\":true,\"dockSlot\":\"bottom\"},"
                + "\"inspector\":{\"isOpen\":true,\"dockSlot\":\"right\"}}}");

            var service = new WorkspaceLayoutService(dir);
            var layout = service.Load();

            Assert.False(layout.IsPanelOpen(PanelId.Media));
            Assert.Equal(WorkspaceLayout.SchemaVersion, layout.Version);
            Assert.Equal(
                WorkspaceLayout.GetDefaultLandscapeArea(PanelId.Preview),
                layout.GetGridArea(PanelId.Preview, portrait: false));
            Assert.True(WorkspaceLayout.IsValidGridLayout(layout.GetGridAreas(false), false));
            Assert.True(WorkspaceLayout.IsValidGridLayout(layout.GetGridAreas(true), true));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void portrait_utility_window_uses_the_closed_inspector_cell_without_overlap()
    {
        var layout = WorkspaceLayout.Default();
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Preview,
            PanelId.Timeline,
        };

        var found = WorkspaceUtilityPlacementService.TryFindArea(
            layout.GetGridAreas(portrait: true),
            visible,
            portrait: true,
            preferredCell: null,
            out var area);

        Assert.True(found);
        Assert.Equal(new PanelGridArea(1, 0, 1, 1), area);
        Assert.DoesNotContain(area.Cells(), cell => visible.Any(panel =>
            layout.GetGridArea(panel, portrait: true).ContainsCell(cell.Column, cell.Row)));
        Assert.True(WorkspaceGridLayoutPlanner.SupportsWindowCount(visible.Count + 1, portrait: true));
    }

    [Fact]
    public void portrait_utility_window_uses_the_full_closed_preview_column()
    {
        var layout = WorkspaceLayout.Default();
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Inspector,
            PanelId.Timeline,
        };

        var found = WorkspaceUtilityPlacementService.TryFindArea(
            layout.GetGridAreas(portrait: true),
            visible,
            portrait: true,
            preferredCell: null,
            out var area);

        Assert.True(found);
        Assert.Equal(new PanelGridArea(2, 0, 1, 2), area);
    }

    [Fact]
    public void portrait_utility_window_falls_back_when_all_four_primary_windows_are_visible()
    {
        var layout = WorkspaceLayout.Default();
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Preview,
            PanelId.Timeline,
            PanelId.Inspector,
        };

        var found = WorkspaceUtilityPlacementService.TryFindArea(
            layout.GetGridAreas(portrait: true),
            visible,
            portrait: true,
            preferredCell: null,
            out var area);

        Assert.False(found);
        Assert.Equal(PanelGridArea.Empty, area);
        Assert.False(WorkspaceGridLayoutPlanner.SupportsWindowCount(visible.Count + 1, portrait: true));
    }

    [Fact]
    public void utility_placement_rejects_overlapping_primary_areas()
    {
        var areas = WorkspaceLayout.Default().GetGridAreas(portrait: true).ToDictionary();
        areas[PanelId.Media] = areas[PanelId.Inspector];
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Inspector,
            PanelId.Timeline,
        };

        Assert.False(WorkspaceUtilityPlacementService.TryFindArea(
            areas,
            visible,
            portrait: true,
            preferredCell: null,
            out _));
    }

    [Fact]
    public void closing_media_in_landscape_allows_preview_to_absorb_the_adjacent_space()
    {
        var stored = WorkspaceLayout.Default().GetGridAreas(portrait: false);
        var visible = new HashSet<PanelId>
        {
            PanelId.Preview,
            PanelId.Timeline,
            PanelId.Inspector,
        };

        var resolved = WorkspaceVisibleLayoutService.Resolve(
            stored,
            visible,
            portrait: false,
            PanelGridArea.Empty);

        Assert.False(WorkspaceVisibleLayoutService.HasOverlaps(resolved, PanelGridArea.Empty));
        Assert.Equal(6, resolved.Values.SelectMany(area => area.Cells()).Distinct().Count());
        Assert.True(resolved[PanelId.Preview].CellCount > stored[PanelId.Preview].CellCount);
        Assert.Equal(new PanelGridArea(0, 1, 2, 1).CellCount, resolved[PanelId.Timeline].CellCount);
    }

    [Fact]
    public void closing_inspector_in_landscape_does_not_leave_an_empty_grid_cell()
    {
        var stored = WorkspaceLayout.Default().GetGridAreas(portrait: false);
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Preview,
            PanelId.Timeline,
        };

        var resolved = WorkspaceVisibleLayoutService.Resolve(
            stored,
            visible,
            portrait: false,
            PanelGridArea.Empty);

        Assert.False(WorkspaceVisibleLayoutService.HasOverlaps(resolved, PanelGridArea.Empty));
        Assert.Equal(6, resolved.Values.SelectMany(area => area.Cells()).Distinct().Count());
        Assert.Equal(2, resolved[PanelId.Timeline].ColumnSpan);
        Assert.Equal(1, resolved[PanelId.Timeline].RowSpan);
    }

    [Fact]
    public void portrait_utility_reservation_cannot_overlap_compacted_primary_windows()
    {
        var stored = WorkspaceLayout.Default().GetGridAreas(portrait: true);
        var visible = new HashSet<PanelId>
        {
            PanelId.Media,
            PanelId.Preview,
            PanelId.Timeline,
        };
        var utility = new PanelGridArea(1, 0, 1, 1);

        var resolved = WorkspaceVisibleLayoutService.Resolve(
            stored,
            visible,
            portrait: true,
            utility);

        Assert.False(WorkspaceVisibleLayoutService.HasOverlaps(resolved, utility));
        Assert.Equal(5, resolved.Values.SelectMany(area => area.Cells()).Distinct().Count());
        Assert.Equal(1, resolved[PanelId.Preview].ColumnSpan);
        Assert.Equal(2, resolved[PanelId.Preview].RowSpan);
        Assert.Equal(2, resolved[PanelId.Timeline].ColumnSpan);
        Assert.Equal(1, resolved[PanelId.Timeline].RowSpan);
    }

    [Fact]
    public void damaged_layout_falls_back_to_default()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RushframeTest_" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "workspace-layout.json"), "{invalid json}");

            var service = new WorkspaceLayoutService(dir);
            var layout = service.Load();

            Assert.True(layout.IsPanelOpen(PanelId.Media));
            Assert.True(WorkspaceLayout.IsValidGridLayout(layout.GetGridAreas(false), false));
            Assert.True(WorkspaceLayout.IsValidGridLayout(layout.GetGridAreas(true), true));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
