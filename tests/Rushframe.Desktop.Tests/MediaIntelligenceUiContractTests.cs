namespace Rushframe.Desktop.Tests;

public sealed class MediaIntelligenceUiContractTests
{
    [Fact]
    public void ai_tab_is_scrollable_and_switches_dense_controls_to_responsive_grids()
    {
        var xaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var media = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Media.cs"));

        Assert.Contains("x:Name=\"MediaIntelligenceScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MediaIntelligenceFeatureGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MediaIntelligenceModelGrid\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<DockPanel Margin=\"8\" MinWidth=\"300\">", xaml, StringComparison.Ordinal);
        Assert.Contains("GetDensePanelColumnCount", media, StringComparison.Ordinal);
    }

    [Fact]
    public void ai_actions_share_selection_state_support_cancel_and_rollback_rejected_apply()
    {
        var media = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Media.cs"));

        Assert.Contains("ResolveMediaIntelligenceAsset()", media, StringComparison.Ordinal);
        Assert.Contains("MediaIntelligenceUiPolicy.NormalizeWhisperModel", media, StringComparison.Ordinal);
        Assert.Contains("operationCancellation.Token", media, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", media, StringComparison.Ordinal);
        Assert.Contains("MediaIntelligenceProjectMutationGuard.Capture", media, StringComparison.Ordinal);
        Assert.Contains("MediaIntelligenceProjectMutationGuard.Restore", media, StringComparison.Ordinal);
        Assert.Contains("Use Apply to add", media, StringComparison.Ordinal);
        Assert.Contains("StoreMediaIntelligenceAnalysisAsync(dialog.FileName, asset)", media, StringComparison.Ordinal);
        Assert.DoesNotContain("autoApply", media, StringComparison.Ordinal);
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
