namespace Rushframe.Domain;

public sealed class Project
{
    public const int CurrentSchemaVersion = 7;

    public ProjectId Id { get; init; } = ProjectId.New();
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Name { get; set; } = "Untitled";
    public List<Sequence> Sequences { get; init; } = [new()];
    public List<MediaAsset> MediaLibrary { get; init; } = [];
    public List<MediaIntelligenceAnalysis> MediaIntelligence { get; init; } = [];
    public List<MediaRelationship> MediaRelationships { get; init; } = [];
    public string CampaignDescription { get; set; } = string.Empty;
    public EditingBrief EditingBrief { get; set; } = new();
    public List<CampaignTask> Tasks { get; init; } = [];
    public List<CreativeAssetProviderManifest> AssetProviders { get; init; } = [];
    public List<ExtensionManifest> Extensions { get; init; } = [];
    public ProjectOverview Overview { get; set; } = new();
    public ProductionWorkflow Workflow { get; set; } = new();
    public List<ExportVariant> ExportVariants { get; init; } = [];
    public List<ExternalCompositionSpec> ExternalCompositions { get; init; } = [];
    public List<AutomationProviderManifest> AutomationProviders { get; init; } = AutomationProviderManifest.CreateDefaults();
    public List<AgentEditPlanRecord> AgentEditPlans { get; init; } = [];
    public List<RenderJobRecord> RenderJobs { get; init; } = [];
    public List<RenderReceiptReference> RenderReceipts { get; init; } = [];
    public TranscriptEditPolicy TranscriptEditPolicy { get; set; } = new();

    public Sequence? MainSequence => Sequences.FirstOrDefault();

    public long IncrementRevision()
    {
        Revision = checked(Revision + 1);
        ModifiedUtc = DateTimeOffset.UtcNow;
        return Revision;
    }

    public Sequence AddSequence(string name)
    {
        var seq = new Sequence { Name = name };
        Sequences.Add(seq);
        return seq;
    }
}
