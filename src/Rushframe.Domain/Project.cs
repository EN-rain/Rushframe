namespace Rushframe.Domain;

public sealed class Project
{
    public ProjectId Id { get; init; } = ProjectId.New();
    public string Name { get; set; } = "Untitled";
    public List<Sequence> Sequences { get; init; } = [new()];
    public List<MediaAsset> MediaLibrary { get; init; } = [];
    public List<MediaIntelligenceAnalysis> MediaIntelligence { get; init; } = [];
    public string CampaignDescription { get; set; } = string.Empty;
    public List<CampaignTask> Tasks { get; init; } = [];

    public Sequence? MainSequence => Sequences.FirstOrDefault();

    public Sequence AddSequence(string name)
    {
        var seq = new Sequence { Name = name };
        Sequences.Add(seq);
        return seq;
    }
}
