using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop.Tests;

public sealed class AdaptiveWindowServiceTests
{
    [Fact]
    public void minimum_window_size_scales_with_ui_and_never_exceeds_work_area()
    {
        var scaled = AdaptiveWindowService.GetWindowMinimum(1.15, 1600, 900);
        Assert.Equal(1288, scaled.Width, precision: 3);
        Assert.Equal(713, scaled.Height, precision: 3);

        var clamped = AdaptiveWindowService.GetWindowMinimum(1.15, 1200, 680);
        Assert.Equal(1200, clamped.Width);
        Assert.Equal(680, clamped.Height);
    }

    [Theory]
    [InlineData(359, 1)]
    [InlineData(360, 2)]
    [InlineData(double.NaN, 1)]
    public void dense_panel_columns_adapt_to_available_width(double width, int expected)
    {
        Assert.Equal(expected, AdaptiveWindowService.GetDensePanelColumnCount(width));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void utility_can_use_a_free_cell_in_both_orientations_without_moving_preview_or_timeline(bool portrait)
    {
        var stored = WorkspaceLayout.Default().GetGridAreas(portrait);
        var visible = portrait
            ? new HashSet<PanelId> { PanelId.Media, PanelId.Preview, PanelId.Timeline }
            : new HashSet<PanelId> { PanelId.Preview, PanelId.Timeline, PanelId.Inspector };
        var utility = portrait
            ? new PanelGridArea(1, 0, 1, 1)
            : new PanelGridArea(0, 0, 1, 1);
        var resolved = WorkspaceVisibleLayoutService.Resolve(stored, visible, portrait, utility);

        Assert.True(AdaptiveWindowService.CanHostSeparateUtility(
            1200,
            600,
            resolved,
            visible,
            utility,
            portrait));
        Assert.Equal(stored[PanelId.Timeline], resolved[PanelId.Timeline]);
        Assert.Equal(stored[PanelId.Preview], resolved[PanelId.Preview]);
    }

    [Fact]
    public void compact_viewport_keeps_utility_in_tabs_instead_of_compressing_protected_windows()
    {
        var portrait = false;
        var stored = WorkspaceLayout.Default().GetGridAreas(portrait);
        var visible = new HashSet<PanelId> { PanelId.Preview, PanelId.Timeline, PanelId.Inspector };
        var utility = new PanelGridArea(0, 0, 1, 1);
        var resolved = WorkspaceVisibleLayoutService.Resolve(stored, visible, portrait, utility);

        Assert.False(AdaptiveWindowService.CanHostSeparateUtility(
            850,
            430,
            resolved,
            visible,
            utility,
            portrait));
    }
}
