using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop.Tests;

public sealed class PanelRegistryTests
{
    [Fact]
    public void registry_contains_all_panels()
    {
        Assert.Equal(7, PanelRegistry.All.Length);
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
    public void timeline_panel_cannot_be_closed()
    {
        var timeline = PanelRegistry.Find(PanelId.Timeline);
        Assert.NotNull(timeline);
        Assert.False(timeline.CanClose);
    }

    [Fact]
    public void find_returns_null_for_unknown()
    {
        Assert.Null(PanelRegistry.Find(new PanelId("bogus")));
    }
}
