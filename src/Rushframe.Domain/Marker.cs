namespace Rushframe.Domain;

public sealed class Marker
{
    public MarkerId Id { get; init; } = MarkerId.New();
    public required string Label { get; set; }
    public required MediaTime Time { get; set; }
    public string? Note { get; set; }
    public string? Color { get; set; }
    public MediaTime Duration { get; set; }
    public int DurationInFrames { get; set; }
    public MediaAssetId? MediaIntelligenceSourceAssetId { get; set; }
}
