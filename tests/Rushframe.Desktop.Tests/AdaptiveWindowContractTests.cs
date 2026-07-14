namespace Rushframe.Desktop.Tests;

public sealed class AdaptiveWindowContractTests
{
    [Fact]
    public void shell_uses_viewport_guardrails_and_the_same_utility_policy_in_both_orientations()
    {
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var shell = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.UiShell.cs"));
        var layout = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Layout.cs"));

        Assert.Contains("ApplyWindowSizeGuardrails", window, StringComparison.Ordinal);
        Assert.Contains("AdaptiveWindowService.GetWindowMinimum", layout, StringComparison.Ordinal);
        Assert.Contains("TryResolveSeparateUtilityArea", shell, StringComparison.Ordinal);
        Assert.Contains("AdaptiveWindowService.CanHostSeparateUtility", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("var useSeparateWindow = portrait", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void preview_and_timeline_stay_protected_when_utility_panels_overflow_to_tabs()
    {
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var visibleLayout = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Workspace", "WorkspaceVisibleLayoutService.cs"));
        var registry = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Panels", "PanelRegistry.cs"));

        Assert.Contains("GetVisiblePrimaryPanels(mediaOpen, previewOpen, inspectorWindowOpen)", window, StringComparison.Ordinal);
        Assert.Contains("Inspector restored to host utility tabs", window, StringComparison.Ordinal);
        Assert.Contains("return storedVisibleAreas", visibleLayout, StringComparison.Ordinal);
        Assert.Contains("Id = PanelId.Preview, Title = \"Preview\", CanClose = false", registry, StringComparison.Ordinal);
        Assert.Contains("Id = PanelId.Timeline, Title = \"Timeline\", CanClose = false", registry, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] parts) =>
        Path.Combine(FindRepositoryRoot(), Path.Combine(parts));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
