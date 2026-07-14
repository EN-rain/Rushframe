namespace Rushframe.Desktop.Panels;

public readonly record struct PanelId(string Key)
{
    public static readonly PanelId Media = new("media");
    public static readonly PanelId Preview = new("preview");
    public static readonly PanelId Inspector = new("inspector");
    public static readonly PanelId Timeline = new("timeline");
    public static readonly PanelId RenderQueue = new("renderQueue");
    public static readonly PanelId MediaIntelligence = new("mediaIntelligence");
    public static readonly PanelId ProductionWorkflow = new("productionWorkflow");
    public static readonly PanelId TranscriptEditor = new("transcriptEditor");
    public static readonly PanelId OutputVariants = new("outputVariants");
    public static readonly PanelId GeneratedCompositions = new("generatedCompositions");
}
