namespace Rushframe.Desktop.Tests;

public sealed class ReleaseSafetyContractTests
{
    [Fact]
    public void explicit_save_clears_dirty_state_only_for_the_written_revision()
    {
        var source = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Project.cs"));

        Assert.Contains("var savedRevision = await _saveCoordinator.SaveExplicitAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (_project.Revision == savedRevision)", source, StringComparison.Ordinal);
        Assert.Contains("newer edits remain unsaved", source, StringComparison.Ordinal);
        Assert.Contains("_saveCoordinator.MarkDirty(_project)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void source_monitor_overwrite_is_one_composite_edit()
    {
        var source = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Preview.cs"));

        Assert.Contains("new AddPreparedTrackCommand", source, StringComparison.Ordinal);
        Assert.Contains("new CompositeEditCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("seq.Tracks.Add(track);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var existing in overlapping)\n                Execute(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void group_paste_prevalidates_every_destination_before_execute()
    {
        var source = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var pasteStart = source.IndexOf("private void Paste_Executed", StringComparison.Ordinal);
        var pasteEnd = source.IndexOf("private void SplitClip_Executed", pasteStart, StringComparison.Ordinal);
        var pasteSection = source[pasteStart..pasteEnd];

        Assert.Contains("pasteTargets", pasteSection, StringComparison.Ordinal);
        Assert.Contains("Group paste canceled: no compatible unlocked destination exists for every item", pasteSection, StringComparison.Ordinal);
        Assert.DoesNotContain("if (groupTargetTrack == null) continue", pasteSection, StringComparison.Ordinal);
        Assert.Contains("TrackCompatibility.IsItemCompatibleWithTrack(_clipboard.Clipboard.Kind", pasteSection, StringComparison.Ordinal);
    }

    [Fact]
    public void realtime_preview_fallback_reports_the_failure_instead_of_swallowing_it()
    {
        var source = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.RealtimePreview.cs"));

        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("Realtime preview unavailable", source, StringComparison.Ordinal);
        Assert.Contains("Using exact preview", source, StringComparison.Ordinal);
        Assert.Contains("AddRenderQueueMessage(message)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void external_composition_render_is_cancelable_and_commits_only_terminal_state()
    {
        var service = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Services", "ExternalCompositionService.cs"));
        var automation = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Automation.cs"));

        Assert.DoesNotContain("spec.Status = ExternalCompositionStatus.Rendering", service, StringComparison.Ordinal);
        Assert.DoesNotContain("spec.LastError =", service, StringComparison.Ordinal);
        Assert.Contains("LocalOutputPathGuard.Resolve", service, StringComparison.Ordinal);
        Assert.Contains("operationCancellation.Token", automation, StringComparison.Ordinal);
        Assert.Contains("Composition render was canceled", automation, StringComparison.Ordinal);
        Assert.Contains("spec.LastOutputSha256 = result.OutputSha256", automation, StringComparison.Ordinal);
        Assert.Contains("CreateGeneratedCompositionAssetAsync(spec, fullOutput, cancellationToken)", File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void agent_callers_cannot_disable_editor_owned_approval()
    {
        var desktop = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var backend = File.ReadAllText(SourcePath("rushframe_intelligence", "backend.py"));

        Assert.DoesNotContain("ReadBool(payload, \"require_approval\"", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("\"require_approval\"", backend, StringComparison.Ordinal);
        Assert.Contains("approvalAlreadyGranted", desktop, StringComparison.Ordinal);
        Assert.Contains("new AgentEditPlanPreviewDialog(this, plan).ShowDialog()", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void project_replacement_is_blocked_and_async_render_commits_are_project_scoped()
    {
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var project = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Project.cs"));
        var automation = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Automation.cs"));

        Assert.Contains("ProjectReplacement_CanExecute", window, StringComparison.Ordinal);
        Assert.Contains("CaptureProjectOperationContext", window, StringComparison.Ordinal);
        Assert.Contains("IsCurrentProjectOperation(projectContext)", window, StringComparison.Ordinal);
        Assert.Contains("HasActiveProjectOperation", project, StringComparison.Ordinal);
        Assert.Contains("_projectGeneration", project, StringComparison.Ordinal);
        Assert.Contains("IsCurrentProjectOperation(projectContext)", automation, StringComparison.Ordinal);
    }

    [Fact]
    public void timeline_render_paths_use_revision_frozen_project_snapshots()
    {
        var controller = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Controllers", "ExportController.cs"));
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var automation = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Automation.cs"));
        var variantContext = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Services", "VariantRenderContextService.cs"));
        var receipts = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Services", "RenderReceiptService.cs"));

        Assert.Contains("ProjectSerializer.CreateSnapshot(project)", controller, StringComparison.Ordinal);
        Assert.Contains("ProjectSerializer.CreateSnapshot(_project)", window, StringComparison.Ordinal);
        Assert.Contains("VariantRenderContextService.Create(_project, variant)", automation, StringComparison.Ordinal);
        Assert.Contains("ProjectSerializer.CreateSnapshot(sourceProject)", variantContext, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(\n                renderProject", controller, StringComparison.Ordinal);
        Assert.Contains("CreateAsync(\n                renderProject", window, StringComparison.Ordinal);
        var createSection = receipts[..receipts.IndexOf("public static void ApplyToProject", StringComparison.Ordinal)];
        Assert.DoesNotContain("RenderReceipts.Add", createSection, StringComparison.Ordinal);
        Assert.Contains("public static void ApplyToProject", receipts, StringComparison.Ordinal);
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
