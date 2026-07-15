namespace Rushframe.Desktop.Tests;

public sealed class InspectorContractTests
{
    [Fact]
    public void Inspector_uses_independent_scale_fields_and_stream_neutral_fades()
    {
        var xaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));

        Assert.Contains("x:Name=\"ScaleXBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScaleYBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ScaleBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FadesInspectorExpander\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "x:Name=\"FadeInBox\""));
        Assert.Equal(1, CountOccurrences(xaml, "x:Name=\"FadeOutBox\""));
    }

    [Fact]
    public void Inspector_apply_is_change_only_and_resolves_pending_edits()
    {
        var inspector = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Inspector.cs"));
        var project = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Project.cs"));

        Assert.Contains("commands.Count == 0", inspector, StringComparison.Ordinal);
        Assert.Contains("InspectorValueLogic.CloneSpeedCurve", inspector, StringComparison.Ordinal);
        Assert.Contains("InspectorValueLogic.BuildColorCorrection", inspector, StringComparison.Ordinal);
        Assert.Contains("TryResolvePendingInspectorChanges", inspector, StringComparison.Ordinal);
        Assert.Contains("resolvePendingInspectorChanges: false", inspector, StringComparison.Ordinal);
        Assert.Contains("resolvePendingInspectorChanges && !TryResolvePendingInspectorChanges()", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_core_tabs_close_from_their_right_click_menu_and_restore()
    {
        var shell = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.UiShell.cs"));

        Assert.Contains("Close {title} tab", shell, StringComparison.Ordinal);
        Assert.Contains("tab.ContextMenu = new ContextMenu()", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("header.Children.Add(close)", shell, StringComparison.Ordinal);
        Assert.Contains("hiddenCoreTab.Visibility = Visibility.Visible", shell, StringComparison.Ordinal);
        Assert.Contains("UpdateInspectorTabControls", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_manipulation_uses_a_working_transform_until_commit()
    {
        var preview = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.PreviewInteraction.cs"));

        Assert.Contains("_previewWorkingTransform", preview, StringComparison.Ordinal);
        Assert.Contains("PreviewTransformSnapshot.From(working)", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Transform.PositionX += horizontalChange", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("start.Apply(item.Transform)", preview, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
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
