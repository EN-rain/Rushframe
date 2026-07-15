using Rushframe.Domain;
using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Tests;

public sealed class ProjectArtifactHealthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rushframe-artifact-health-{Guid.NewGuid():N}");

    public ProjectArtifactHealthTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void infrastructure_reports_missing_runtime_artifacts_without_domain_io()
    {
        var project = new Project();
        project.Workflow.Stages[0].ArtifactPaths.Add(Path.Combine(_root, "missing-stage.json"));
        project.ExportVariants.Clear();
        project.ExportVariants.Add(new ExportVariant
        {
            Name = "Portrait",
            SequenceId = project.MainSequence!.Id,
            LastOutputPath = Path.Combine(_root, "missing-output.mp4"),
            LastReceiptPath = Path.Combine(_root, "missing-receipt.json"),
        });
        project.RenderReceipts.Add(new RenderReceiptReference
        {
            ReceiptId = "receipt-1",
            OutputPath = Path.Combine(_root, "missing-render.mp4"),
            ReceiptPath = Path.Combine(_root, "missing-render-receipt.json"),
        });

        var hints = ProjectArtifactHealthService.Inspect(project);
        var domainSource = File.ReadAllText(SourcePath("src", "Rushframe.Domain", "ProjectOverview.cs"));

        Assert.Contains(hints, hint => hint.Contains("artifact is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints, hint => hint.Contains("last output is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints, hint => hint.Contains("render receipt is missing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("File.Exists", domainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Exists", domainSource, StringComparison.Ordinal);
    }

    [Fact]
    public void existing_artifacts_do_not_report_missing()
    {
        var output = Path.Combine(_root, "output.mp4");
        var receipt = Path.Combine(_root, "receipt.json");
        File.WriteAllText(output, "video");
        File.WriteAllText(receipt, "receipt");
        var project = new Project();
        project.RenderReceipts.Add(new RenderReceiptReference
        {
            ReceiptId = "receipt-ok",
            OutputPath = output,
            ReceiptPath = receipt,
        });

        var hints = ProjectArtifactHealthService.Inspect(project);

        Assert.DoesNotContain(hints, hint => hint.Contains("receipt-ok", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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
