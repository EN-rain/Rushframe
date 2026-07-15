namespace Rushframe.Domain;

public enum ProductionStageStatus
{
    Pending,
    Ready,
    Running,
    AwaitingApproval,
    Approved,
    Completed,
    Failed,
    Skipped,
}

public enum ProductionBudgetMode
{
    Observe,
    Warn,
    Cap,
}

public enum ProductionCostStatus
{
    Estimated,
    Reserved,
    Completed,
    Failed,
    Refunded,
}

public enum ProductionDecisionStatus
{
    Proposed,
    Approved,
    Rejected,
    Superseded,
}

public sealed class ProductionWorkflow
{
    public int Version { get; set; } = 1;
    public string ActiveStageId { get; set; } = "brief";
    public decimal? BudgetLimitUsd { get; set; }
    public decimal EstimatedSpendUsd { get; set; }
    public decimal ActualSpendUsd { get; set; }
    public ProductionBudgetMode BudgetMode { get; set; } = ProductionBudgetMode.Cap;
    public bool LocalFirst { get; set; } = true;
    public bool PaidProvidersEnabled { get; set; }
    public bool RequireApprovalForPaidOperations { get; set; } = true;
    public decimal SingleActionApprovalThresholdUsd { get; set; } = 0.50m;
    public List<ProductionWorkflowStage> Stages { get; init; } = CreateDefaultStages();
    public List<ProductionDecision> Decisions { get; init; } = [];
    public List<ProductionCostEvent> CostEvents { get; init; } = [];

    public static List<ProductionWorkflowStage> CreateDefaultStages() =>
    [
        NewStage("brief", "Brief", requiresApproval: true),
        NewStage("source_review", "Source Review", requiresApproval: false),
        NewStage("edit_plan", "Edit Plan", requiresApproval: true),
        NewStage("agent_draft", "Agent Draft", requiresApproval: true),
        NewStage("human_review", "Human Review", requiresApproval: true),
        NewStage("final_qa", "Final QA", requiresApproval: false),
        NewStage("export", "Export", requiresApproval: true),
    ];

    public void EnsureDefaults()
    {
        var defaults = CreateDefaultStages();
        foreach (var defaultStage in defaults)
        {
            if (Stages.All(stage => !string.Equals(stage.Id, defaultStage.Id, StringComparison.OrdinalIgnoreCase)))
                Stages.Add(defaultStage);
        }
        if (string.IsNullOrWhiteSpace(ActiveStageId)) ActiveStageId = "brief";
    }

    private static ProductionWorkflowStage NewStage(string id, string name, bool requiresApproval) =>
        new()
        {
            Id = id,
            Name = name,
            RequiresApproval = requiresApproval,
            Status = id == "brief" ? ProductionStageStatus.Ready : ProductionStageStatus.Pending,
        };
}

public sealed class ProductionWorkflowStage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ProductionStageStatus Status { get; set; }
    public bool RequiresApproval { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Inputs { get; init; } = [];
    public List<string> Outputs { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> ArtifactPaths { get; init; } = [];
    public int Revision { get; set; }
}

public sealed class ProductionCostEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProviderId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public decimal EstimatedUsd { get; set; }
    public decimal ReservedUsd { get; set; }
    public decimal ActualUsd { get; set; }
    public ProductionCostStatus Status { get; set; } = ProductionCostStatus.Estimated;
    public bool UserApproved { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class AutomationProviderManifest
{
    public static List<AutomationProviderManifest> CreateDefaults() =>
    [
        new AutomationProviderManifest
        {
            Id = "rushframe-ffmpeg",
            Name = "Rushframe FFmpeg",
            Enabled = true,
            Local = true,
            Paid = false,
            Capabilities = { "edit", "render", "transcode", "audio", "verification" },
        },
        new AutomationProviderManifest
        {
            Id = "rushframe-intelligence",
            Name = "Rushframe Local Intelligence",
            Enabled = true,
            Local = true,
            Paid = false,
            Capabilities = { "transcription", "scenes", "audio-analysis", "visual-analysis", "semantic-search" },
        },
        new AutomationProviderManifest
        {
            Id = "remotion-local",
            Name = "Local Remotion Adapter",
            Enabled = false,
            Local = true,
            Paid = false,
            Capabilities = { "motion-graphics", "react-composition" },
            Notes = "Enabled only after a project-local Remotion binary is validated.",
        },
        new AutomationProviderManifest
        {
            Id = "hyperframes-local",
            Name = "Local HyperFrames Adapter",
            Enabled = false,
            Local = true,
            Paid = false,
            Capabilities = { "motion-graphics", "html-composition" },
            Notes = "Enabled only after a project-local HyperFrames binary is validated.",
        },
    ];

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Local { get; set; } = true;
    public bool Paid { get; set; }
    public string? Endpoint { get; set; }
    public List<string> Capabilities { get; init; } = [];
    public Dictionary<string, decimal> EstimatedUnitCostsUsd { get; init; } = [];
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProductionDecision
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Category { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> OptionsConsidered { get; init; } = [];
    public string SelectedOption { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool UserVisible { get; set; } = true;
    public ProductionDecisionStatus Status { get; set; } = ProductionDecisionStatus.Proposed;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedUtc { get; set; }
}

public enum ExportVariantStatus
{
    Draft,
    Ready,
    Rendering,
    Completed,
    Failed,
}

public sealed class ExportVariant
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Primary";
    public SequenceId? SequenceId { get; set; }
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;
    public FrameRate? FrameRate { get; set; }
    public string Format { get; set; } = "mp4";
    public string Quality { get; set; } = "high";
    public double? MaximumDurationSeconds { get; set; }
    public double SafeAreaTopPercent { get; set; } = 5;
    public double SafeAreaRightPercent { get; set; } = 10;
    public double SafeAreaBottomPercent { get; set; } = 30;
    public double SafeAreaLeftPercent { get; set; } = 5;
    public bool ShareTimelineEdits { get; set; } = true;
    public Dictionary<string, string> Overrides { get; init; } = [];
    public List<VariantTrackOverride> TrackOverrides { get; init; } = [];
    public List<VariantItemOverride> ItemOverrides { get; init; } = [];
    public ExportVariantStatus Status { get; set; } = ExportVariantStatus.Draft;
    public string? LastOutputPath { get; set; }
    public string? LastReceiptPath { get; set; }
    public DateTimeOffset? LastRenderedUtc { get; set; }
}

public sealed class VariantTrackOverride
{
    public TrackId TrackId { get; set; }
    public bool? Hidden { get; set; }
    public bool? Muted { get; set; }
    public bool? Solo { get; set; }
}

public sealed class VariantItemOverride
{
    public TimelineItemId ItemId { get; set; }
    public bool Hidden { get; set; }
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }
    public double? RotationDegrees { get; set; }
    public double? Opacity { get; set; }
    public double? Volume { get; set; }
    public double? Pan { get; set; }
    public double? FontSize { get; set; }
    public string? TextContent { get; set; }
}

public enum ExternalCompositionKind
{
    Remotion,
    HyperFrames,
    Custom,
}

public enum ExternalCompositionStatus
{
    Draft,
    Validated,
    Rendering,
    Rendered,
    Failed,
    Offline,
}

public sealed class ExternalCompositionSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ExternalCompositionKind Kind { get; set; }
    public string ProjectDirectory { get; set; } = string.Empty;
    public string? EntryPoint { get; set; }
    public string? CompositionId { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public FrameRate FrameRate { get; set; } = new(30, 1);
    public double DurationSeconds { get; set; }
    public bool TransparentBackground { get; set; }
    public bool ImportAfterRender { get; set; } = true;
    public ExternalCompositionStatus Status { get; set; } = ExternalCompositionStatus.Draft;
    public string? LastError { get; set; }
    public string? LastOutputSha256 { get; set; }
    public DateTimeOffset? LastRenderedUtc { get; set; }
    public Dictionary<string, string> Parameters { get; init; } = [];
}

public enum AgentEditPlanStatus
{
    Proposed,
    Validated,
    Approved,
    Applied,
    Rejected,
    Conflict,
    Failed,
}

public sealed class AgentEditPlanRecord
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N");
    public string Summary { get; set; } = string.Empty;
    public long BaseRevision { get; set; }
    public long? AppliedRevision { get; set; }
    public AgentEditPlanStatus Status { get; set; } = AgentEditPlanStatus.Proposed;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedUtc { get; set; }
    public string PromptId { get; set; } = "rushframe-editing-agent";
    public string PromptVersion { get; set; } = "1.0";
    public AgentCreativePlan CreativePlan { get; set; } = new();
    public AgentPlanQualityScores QualityScores { get; set; } = new();
    public List<TimelineQualityIssue> QualityIssues { get; init; } = [];
    public List<AgentEditOperationRecord> Operations { get; init; } = [];
    public List<AgentAffectedRange> AffectedRanges { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? Error { get; set; }
}

public sealed class AgentEditOperationRecord
{
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? TargetId { get; set; }
}

public sealed class AgentAffectedRange
{
    public string? TrackId { get; set; }
    public string? ItemId { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Change { get; set; } = string.Empty;
}

public enum RenderJobKind
{
    Timeline,
    Variant,
    ExternalComposition,
}

public enum RenderJobStatus
{
    Pending,
    Rendering,
    Verifying,
    Completed,
    Failed,
    Canceled,
}

public sealed class RenderJobRecord
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public RenderJobKind Kind { get; set; }
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Pending;
    public string OutputPath { get; set; } = string.Empty;
    public string? VariantId { get; set; }
    public string? CompositionId { get; set; }
    public long SourceRevision { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = "mp4";
    public string Quality { get; set; } = "high";
    public bool IncludeAudio { get; set; } = true;
    public bool HardwareEncoding { get; set; }
    public int AttemptCount { get; set; }
    public string? ReceiptId { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}

public enum RenderVerificationStatus
{
    Pending,
    Passed,
    PassedWithWarnings,
    Failed,
}

public sealed class RenderReceiptReference
{
    public string ReceiptId { get; init; } = Guid.NewGuid().ToString("N");
    public string OutputPath { get; set; } = string.Empty;
    public string ReceiptPath { get; set; } = string.Empty;
    public string? VariantId { get; set; }
    public long ProjectRevision { get; set; }
    public string? OutputSha256 { get; set; }
    public RenderVerificationStatus VerificationStatus { get; set; } = RenderVerificationStatus.Pending;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TranscriptEditPolicy
{
    public double WordPaddingBeforeSeconds { get; set; } = 0.05;
    public double WordPaddingAfterSeconds { get; set; } = 0.08;
    public double MinimumSilenceCutSeconds { get; set; } = 0.4;
    public double GeneratedCutFadeSeconds { get; set; } = 0.03;
    public int CaptionWordsPerChunk { get; set; } = 3;
    public double CaptionMinimumDurationSeconds { get; set; } = 0.45;
    public double CaptionMaximumDurationSeconds { get; set; } = 2.5;
    public bool PreserveAudioEvents { get; set; } = true;
    public bool PreserveReactionTails { get; set; } = true;
}
