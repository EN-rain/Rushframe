using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop.Controllers;

/// <summary>
/// Makes the timeline mutation, applied-plan record, and workflow transition one
/// logical undoable action. Project revision is intentionally owned by MainWindow
/// so execute, undo, and redo each increment it exactly once.
/// </summary>
internal sealed class AgentPlanApplicationCommand : IEditCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Project _project;
    private readonly AgentEditPlanCompilation _plan;
    private readonly IEditCommand _timelineCommand;
    private readonly long _baseRevision;
    private string? _beforeStateJson;
    private string? _afterStateJson;

    public AgentPlanApplicationCommand(
        Project project,
        AgentEditPlanCompilation plan,
        long baseRevision)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _timelineCommand = plan.Command ?? throw new ArgumentException("The plan has no compiled command.", nameof(plan));
        _baseRevision = baseRevision;
    }

    public string Description => $"Apply agent plan: {_plan.Summary}";

    public EditResult Execute(Sequence sequence)
    {
        _beforeStateJson ??= CaptureProjectState();
        var result = _timelineCommand.Execute(sequence);
        if (!result.Success) return result;

        try
        {
            if (_afterStateJson == null)
            {
                ApplyPlanState();
                _afterStateJson = CaptureProjectState();
            }
            else
            {
                RestoreProjectState(_afterStateJson);
                RefreshAppliedMetadata();
            }
            return EditResult.Ok();
        }
        catch (Exception ex)
        {
            RestoreProjectState(_beforeStateJson);
            return EditResult.Fail($"Agent plan state could not be applied: {ex.Message}");
        }
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_beforeStateJson == null) return EditResult.Fail("Agent plan has not been applied");
        var result = _timelineCommand.Undo(sequence);
        if (!result.Success) return result;

        try
        {
            RestoreProjectState(_beforeStateJson);
            return EditResult.Ok();
        }
        catch (Exception ex)
        {
            return EditResult.Fail($"Agent plan state could not be restored: {ex.Message}");
        }
    }

    private void ApplyPlanState()
    {
        _project.AgentEditPlans.RemoveAll(candidate => candidate.PlanId == _plan.PlanId);
        var record = new AgentEditPlanRecord
        {
            PlanId = _plan.PlanId,
            Summary = _plan.Summary,
            BaseRevision = _baseRevision,
            AppliedRevision = checked(_project.Revision + 1),
            Status = AgentEditPlanStatus.Applied,
            AppliedUtc = DateTimeOffset.UtcNow,
            PromptId = _plan.PromptId,
            PromptVersion = _plan.PromptVersion,
            CreativePlan = _plan.CreativePlan,
            QualityScores = _plan.QualityScores,
        };
        record.QualityIssues.AddRange(_plan.QualityIssues);
        record.Operations.AddRange(_plan.Operations);
        record.AffectedRanges.AddRange(_plan.AffectedRanges);
        record.Warnings.AddRange(_plan.Warnings);
        _project.AgentEditPlans.Add(record);

        _project.Workflow.EnsureDefaults();
        var stage = _project.Workflow.Stages.FirstOrDefault(value => value.Id == "agent_draft");
        if (stage == null) return;
        stage.Status = ProductionStageStatus.AwaitingApproval;
        stage.StartedUtc ??= record.CreatedUtc;
        stage.Summary = record.Summary;
        stage.Revision++;
        stage.Outputs.Clear();
        stage.Outputs.Add($"edit-plan:{record.PlanId}");
        stage.Warnings.Clear();
        stage.Warnings.AddRange(record.Warnings);
        _project.Workflow.ActiveStageId = "agent_draft";
    }

    private void RefreshAppliedMetadata()
    {
        var record = _project.AgentEditPlans.FirstOrDefault(candidate => candidate.PlanId == _plan.PlanId);
        if (record == null) return;
        record.AppliedRevision = checked(_project.Revision + 1);
        record.AppliedUtc = DateTimeOffset.UtcNow;
        record.Status = AgentEditPlanStatus.Applied;
    }

    private string CaptureProjectState() => JsonSerializer.Serialize(
        new AgentPlanProjectState
        {
            AgentEditPlans = _project.AgentEditPlans,
            Workflow = WorkflowState.Capture(_project.Workflow),
        },
        JsonOptions);

    private void RestoreProjectState(string json)
    {
        var state = JsonSerializer.Deserialize<AgentPlanProjectState>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Agent plan project state is invalid.");
        _project.AgentEditPlans.Clear();
        _project.AgentEditPlans.AddRange(state.AgentEditPlans);
        _project.Workflow = state.Workflow.Restore();
    }

    private sealed class AgentPlanProjectState
    {
        public List<AgentEditPlanRecord> AgentEditPlans { get; set; } = [];
        public WorkflowState Workflow { get; set; } = new();
    }

    private sealed class WorkflowState
    {
        public int Version { get; set; } = 1;
        public string ActiveStageId { get; set; } = string.Empty;
        public decimal? BudgetLimitUsd { get; set; }
        public decimal EstimatedSpendUsd { get; set; }
        public decimal ActualSpendUsd { get; set; }
        public ProductionBudgetMode BudgetMode { get; set; } = ProductionBudgetMode.Cap;
        public bool LocalFirst { get; set; } = true;
        public bool PaidProvidersEnabled { get; set; }
        public bool RequireApprovalForPaidOperations { get; set; } = true;
        public decimal SingleActionApprovalThresholdUsd { get; set; } = 0.50m;
        public List<ProductionWorkflowStage> Stages { get; set; } = [];
        public List<ProductionDecision> Decisions { get; set; } = [];
        public List<ProductionCostEvent> CostEvents { get; set; } = [];

        public static WorkflowState Capture(ProductionWorkflow workflow) => new()
        {
            Version = workflow.Version,
            ActiveStageId = workflow.ActiveStageId,
            BudgetLimitUsd = workflow.BudgetLimitUsd,
            EstimatedSpendUsd = workflow.EstimatedSpendUsd,
            ActualSpendUsd = workflow.ActualSpendUsd,
            BudgetMode = workflow.BudgetMode,
            LocalFirst = workflow.LocalFirst,
            PaidProvidersEnabled = workflow.PaidProvidersEnabled,
            RequireApprovalForPaidOperations = workflow.RequireApprovalForPaidOperations,
            SingleActionApprovalThresholdUsd = workflow.SingleActionApprovalThresholdUsd,
            Stages = workflow.Stages,
            Decisions = workflow.Decisions,
            CostEvents = workflow.CostEvents,
        };

        public ProductionWorkflow Restore()
        {
            var workflow = new ProductionWorkflow
            {
                Version = Version,
                ActiveStageId = ActiveStageId,
                BudgetLimitUsd = BudgetLimitUsd,
                EstimatedSpendUsd = EstimatedSpendUsd,
                ActualSpendUsd = ActualSpendUsd,
                BudgetMode = BudgetMode,
                LocalFirst = LocalFirst,
                PaidProvidersEnabled = PaidProvidersEnabled,
                RequireApprovalForPaidOperations = RequireApprovalForPaidOperations,
                SingleActionApprovalThresholdUsd = SingleActionApprovalThresholdUsd,
            };
            workflow.Stages.Clear();
            workflow.Stages.AddRange(Stages);
            workflow.Decisions.AddRange(Decisions);
            workflow.CostEvents.AddRange(CostEvents);
            return workflow;
        }
    }
}
