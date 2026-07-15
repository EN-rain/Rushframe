using Rushframe.Domain;

namespace Rushframe.Infrastructure;

public static class ProjectArtifactHealthService
{
    public static IReadOnlyList<string> Inspect(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var hints = new List<string>();

        foreach (var stage in project.Workflow.Stages)
        {
            foreach (var artifact in stage.ArtifactPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!File.Exists(artifact))
                    hints.Add($"Workflow/{stage.Name}: artifact is missing: {Path.GetFileName(artifact)}.");
            }
        }

        foreach (var variant in project.ExportVariants)
        {
            if (!string.IsNullOrWhiteSpace(variant.LastOutputPath) && !File.Exists(variant.LastOutputPath))
                hints.Add($"Variant/{variant.Name}: last output is missing: {Path.GetFileName(variant.LastOutputPath)}.");
            if (!string.IsNullOrWhiteSpace(variant.LastReceiptPath) && !File.Exists(variant.LastReceiptPath))
                hints.Add($"Variant/{variant.Name}: render receipt is missing: {Path.GetFileName(variant.LastReceiptPath)}.");
        }

        foreach (var composition in project.ExternalCompositions)
        {
            if (composition.Status == ExternalCompositionStatus.Rendered
                && (string.IsNullOrWhiteSpace(composition.OutputPath) || !File.Exists(composition.OutputPath)))
                hints.Add($"Composition/{composition.Name}: rendered output is missing.");
        }

        foreach (var receipt in project.RenderReceipts)
        {
            if (!File.Exists(receipt.OutputPath))
                hints.Add($"Render receipt/{receipt.ReceiptId}: output file is missing: {Path.GetFileName(receipt.OutputPath)}.");
            if (!File.Exists(receipt.ReceiptPath))
                hints.Add($"Render receipt/{receipt.ReceiptId}: receipt file is missing.");
        }

        return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
