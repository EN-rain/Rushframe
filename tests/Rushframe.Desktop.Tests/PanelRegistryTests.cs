using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Tests;

public sealed class PanelRegistryTests
{
    [Fact]
    public void registry_contains_all_panels()
    {
        Assert.Equal(10, PanelRegistry.All.Length);
        Assert.NotNull(PanelRegistry.Find(PanelId.ProductionWorkflow));
        Assert.NotNull(PanelRegistry.Find(PanelId.TranscriptEditor));
        Assert.NotNull(PanelRegistry.Find(PanelId.OutputVariants));
        Assert.NotNull(PanelRegistry.Find(PanelId.GeneratedCompositions));
    }

    [Fact]
    public void panel_ids_are_unique()
    {
        var keys = PanelRegistry.All.Select(p => p.Id.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void known_panels_have_titles()
    {
        foreach (var panel in PanelRegistry.All)
            Assert.False(string.IsNullOrWhiteSpace(panel.Title));
    }

    [Fact]
    public void utility_panels_are_inspector_tabs_not_floating_grid_windows()
    {
        Assert.Equal("Project Files", PanelRegistry.Find(PanelId.Media)?.Title);
        foreach (var panelId in new[]
        {
            PanelId.RenderQueue,
            PanelId.MediaIntelligence,
            PanelId.ProductionWorkflow,
            PanelId.TranscriptEditor,
            PanelId.OutputVariants,
            PanelId.GeneratedCompositions,
        })
        {
            var panel = PanelRegistry.Find(panelId);
            Assert.NotNull(panel);
            Assert.False(panel.CanFloat);
        }
    }

    [Fact]
    public void preview_and_timeline_panels_cannot_be_closed()
    {
        var preview = PanelRegistry.Find(PanelId.Preview);
        var timeline = PanelRegistry.Find(PanelId.Timeline);
        Assert.NotNull(preview);
        Assert.NotNull(timeline);
        Assert.False(preview.CanClose);
        Assert.False(timeline.CanClose);
    }

    [Fact]
    public void find_returns_null_for_unknown()
    {
        Assert.Null(PanelRegistry.Find(new PanelId("bogus")));
    }
}
