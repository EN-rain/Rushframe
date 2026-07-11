namespace Rushframe.Desktop.Panels;

public readonly record struct PanelId(string Key)
{
    public static readonly PanelId Media = new("media");
    public static readonly PanelId Preview = new("preview");
    public static readonly PanelId Inspector = new("inspector");
    public static readonly PanelId Timeline = new("timeline");
    public static readonly PanelId Tasks = new("tasks");
    public static readonly PanelId RenderQueue = new("renderQueue");
    public static readonly PanelId MediaIntelligence = new("mediaIntelligence");
}
