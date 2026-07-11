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
        Assert.False(layout.IsPanelOpen(PanelId.Tasks));
        Assert.False(layout.IsPanelOpen(PanelId.RenderQueue));
        Assert.False(layout.IsPanelOpen(PanelId.MediaIntelligence));
    }

    [Fact]
    public void toggle_panel_closes_and_opens()
    {
        var layout = WorkspaceLayout.Default();
        Assert.True(layout.IsPanelOpen(PanelId.Inspector));

        layout = layout.WithPanelToggled(PanelId.Inspector, false);
        Assert.False(layout.IsPanelOpen(PanelId.Inspector));

        layout = layout.WithPanelToggled(PanelId.Inspector, true);
        Assert.True(layout.IsPanelOpen(PanelId.Inspector));
    }

    [Fact]
    public void toggle_panel_does_not_affect_other_panels()
    {
        var layout = WorkspaceLayout.Default();
        layout = layout.WithPanelToggled(PanelId.Media, false);

        Assert.False(layout.IsPanelOpen(PanelId.Media));
        Assert.True(layout.IsPanelOpen(PanelId.Preview));
        Assert.True(layout.IsPanelOpen(PanelId.Inspector));
        Assert.True(layout.IsPanelOpen(PanelId.Timeline));
        Assert.False(layout.IsPanelOpen(PanelId.Tasks));
    }

    [Fact]
    public void unknown_panel_returns_false()
    {
        var layout = WorkspaceLayout.Default();
        var unknown = new PanelId("nonexistent");
        Assert.False(layout.IsPanelOpen(unknown));
    }

    [Fact]
    public void layout_serialization_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RushframeTest_" + Guid.NewGuid());
        try
        {
            var service = new WorkspaceLayoutService(dir);

            var original = WorkspaceLayout.Default();
            original = original.WithPanelToggled(PanelId.Tasks, false);
            service.Save(original);

            var loaded = service.Load();
            Assert.True(loaded.IsPanelOpen(PanelId.Media));
            Assert.True(loaded.IsPanelOpen(PanelId.Timeline));
            Assert.False(loaded.IsPanelOpen(PanelId.Tasks));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
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

            Assert.NotNull(layout);
            Assert.True(layout.IsPanelOpen(PanelId.Media));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
