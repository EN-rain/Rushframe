using System.Collections.Immutable;

namespace Rushframe.Desktop.Panels;

public static class PanelRegistry
{
    private static readonly ImmutableArray<PanelDefinition> _panels =
    [
        new() { Id = PanelId.Media, Title = "Project Files", CanFloat = true },
        new() { Id = PanelId.Preview, Title = "Preview", CanClose = false, CanFloat = true },
        new() { Id = PanelId.Inspector, Title = "Inspector", CanFloat = true },
        new() { Id = PanelId.Timeline, Title = "Timeline", CanClose = false, CanFloat = true },
        new() { Id = PanelId.RenderQueue, Title = "Render Queue", CanFloat = false },
        new() { Id = PanelId.MediaIntelligence, Title = "AI", CanFloat = false },
        new() { Id = PanelId.ProductionWorkflow, Title = "Production Workflow", CanFloat = false },
        new() { Id = PanelId.TranscriptEditor, Title = "Transcript Editor", CanFloat = false },
        new() { Id = PanelId.OutputVariants, Title = "Output Variants & QA", CanFloat = false },
        new() { Id = PanelId.GeneratedCompositions, Title = "Generated Compositions", CanFloat = false },
    ];

    public static ImmutableArray<PanelDefinition> All => _panels;

    public static PanelDefinition? Find(PanelId id) =>
        _panels.FirstOrDefault(p => p.Id == id);
}
