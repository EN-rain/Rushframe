using System.Globalization;
using System.IO;

namespace Rushframe.Domain;

public sealed class ProjectOverview
{
    public DateTimeOffset GeneratedUtc { get; set; }
    public int SequenceCount { get; set; }
    public int MediaAssetCount { get; set; }
    public int TrackCount { get; set; }
    public int TimelineItemCount { get; set; }
    public double TotalDurationSeconds { get; set; }
    public List<string> EffectTypes { get; init; } = [];
    public List<ProjectModifierCount> ModifierSummary { get; init; } = [];
    public string ActiveWorkflowStageId { get; set; } = string.Empty;
    public int AnalyzedMediaCount { get; set; }
    public int AgentEditPlanCount { get; set; }
    public int AutomationProviderCount { get; set; }
    public int RenderJobCount { get; set; }
    public int RenderReceiptCount { get; set; }
    public List<string> ReviewHints { get; init; } = [];
    public List<WorkflowStageOverview> WorkflowStages { get; init; } = [];
    public List<ExportVariantOverview> ExportVariants { get; init; } = [];
    public List<ExternalCompositionOverview> ExternalCompositions { get; init; } = [];
    public List<AutomationProviderOverview> AutomationProviders { get; init; } = [];
    public List<ProductionCostOverview> CostEvents { get; init; } = [];
    public List<AgentEditPlanOverview> AgentEditPlans { get; init; } = [];
    public List<RenderJobOverview> RenderJobs { get; init; } = [];
    public List<RenderReceiptOverview> RenderReceipts { get; init; } = [];
    public List<MediaAnalysisOverview> MediaAnalyses { get; init; } = [];
    public List<SequenceEditOverview> Sequences { get; init; } = [];
}

public sealed class WorkflowStageOverview
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ProductionStageStatus Status { get; init; }
    public bool RequiresApproval { get; init; }
    public bool Approved { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = [];
    public List<string> Artifacts { get; init; } = [];
}

public sealed class ExportVariantOverview
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public ExportVariantStatus Status { get; init; }
    public string? LastOutputFile { get; init; }
    public string? LastReceiptFile { get; init; }
    public Dictionary<string, string> Overrides { get; init; } = [];
}

public sealed class ExternalCompositionOverview
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ExternalCompositionKind Kind { get; init; }
    public ExternalCompositionStatus Status { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DurationSeconds { get; init; }
    public string? OutputFile { get; init; }
    public string? OutputSha256 { get; init; }
    public string? LastError { get; init; }
}

public sealed class AutomationProviderOverview
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; }
    public bool Local { get; init; }
    public bool Paid { get; init; }
    public List<string> Capabilities { get; init; } = [];
    public string? Endpoint { get; init; }
}

public sealed class ProductionCostOverview
{
    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public required string Operation { get; init; }
    public decimal EstimatedUsd { get; init; }
    public decimal ActualUsd { get; init; }
    public ProductionCostStatus Status { get; init; }
    public bool UserApproved { get; init; }
    public string? Error { get; init; }
}

public sealed class AgentEditPlanOverview
{
    public required string PlanId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public AgentEditPlanStatus Status { get; init; }
    public long BaseRevision { get; init; }
    public long? AppliedRevision { get; init; }
    public int OperationCount { get; init; }
    public int AffectedRangeCount { get; init; }
    public List<string> Warnings { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class RenderJobOverview
{
    public required string JobId { get; init; }
    public RenderJobKind Kind { get; init; }
    public RenderJobStatus Status { get; init; }
    public string OutputFile { get; init; } = string.Empty;
    public string? VariantId { get; init; }
    public string? CompositionId { get; init; }
    public long SourceRevision { get; init; }
    public int AttemptCount { get; init; }
    public string? ReceiptId { get; init; }
    public string? LastError { get; init; }
}

public sealed class RenderReceiptOverview
{
    public required string ReceiptId { get; init; }
    public string OutputFile { get; init; } = string.Empty;
    public string ReceiptFile { get; init; } = string.Empty;
    public RenderVerificationStatus Status { get; init; }
    public long ProjectRevision { get; init; }
    public string? OutputSha256 { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MediaAnalysisOverview
{
    public required string MediaAssetId { get; init; }
    public string? MediaFile { get; init; }
    public int SceneCount { get; init; }
    public int TranscriptSegmentCount { get; init; }
    public int WordCount { get; init; }
    public int EditingMomentCount { get; init; }
    public int DuplicateTakeGroupCount { get; init; }
    public int WarningCount { get; init; }
}

public sealed class ProjectModifierCount
{
    public required string Modifier { get; init; }
    public int ItemCount { get; init; }
}

public sealed class SequenceEditOverview
{
    public required string Name { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FramesPerSecond { get; init; }
    public double DurationSeconds { get; init; }
    public List<string> Transitions { get; init; } = [];
    public List<TrackEditOverview> Tracks { get; init; } = [];
}

public sealed class TrackEditOverview
{
    public required string Name { get; init; }
    public TrackKind Kind { get; init; }
    public bool Muted { get; init; }
    public bool Solo { get; init; }
    public bool Locked { get; init; }
    public bool Hidden { get; init; }
    public List<TimelineItemEditOverview> Items { get; init; } = [];
}

public sealed class TimelineItemEditOverview
{
    public required string ItemId { get; init; }
    public ItemKind Kind { get; init; }
    public string? MediaFile { get; init; }
    public double TimelineStartSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public double SourceStartSeconds { get; init; }
    public List<string> Modifiers { get; init; } = [];
    public List<string> Effects { get; init; } = [];
}

public static class ProjectOverviewBuilder
{
    private const double Epsilon = 0.0001;

    public static ProjectOverview Build(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var mediaById = project.MediaLibrary.ToDictionary(asset => asset.Id);
        var overview = new ProjectOverview
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SequenceCount = project.Sequences.Count,
            MediaAssetCount = project.MediaLibrary.Count,
            TrackCount = project.Sequences.Sum(sequence => sequence.Tracks.Count),
            TimelineItemCount = project.Sequences.Sum(sequence => sequence.Tracks.Sum(track => track.Items.Count)),
            TotalDurationSeconds = project.Sequences.Count == 0
                ? 0
                : project.Sequences.Max(sequence => sequence.Duration.Seconds),
            ActiveWorkflowStageId = project.Workflow.ActiveStageId,
            AnalyzedMediaCount = project.MediaIntelligence.Count,
            AgentEditPlanCount = project.AgentEditPlans.Count,
            AutomationProviderCount = project.AutomationProviders.Count,
            RenderJobCount = project.RenderJobs.Count,
            RenderReceiptCount = project.RenderReceipts.Count,
        };

        AddAutomationOverview(project, mediaById, overview);

        var modifierCounts = new Dictionary<string, HashSet<TimelineItemId>>(StringComparer.OrdinalIgnoreCase);
        var effectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sequence in project.Sequences)
        {
            var sequenceOverview = new SequenceEditOverview
            {
                Name = sequence.Name,
                Width = sequence.Width,
                Height = sequence.Height,
                FramesPerSecond = sequence.FrameRate.Value,
                DurationSeconds = sequence.Duration.Seconds,
            };

            foreach (var transition in sequence.Transitions)
            {
                sequenceOverview.Transitions.Add(
                    $"{transition.Kind}: {transition.LeftItemId}->{transition.RightItemId}, " +
                    $"duration {Format(transition.Duration.Seconds)}s, alignment {Format(transition.Alignment)}");
            }

            foreach (var track in sequence.Tracks.OrderBy(track => track.Order))
            {
                var trackOverview = new TrackEditOverview
                {
                    Name = string.IsNullOrWhiteSpace(track.Name) ? track.Kind.ToString() : track.Name,
                    Kind = track.Kind,
                    Muted = track.Muted,
                    Solo = track.Solo,
                    Locked = track.Locked,
                    Hidden = track.Hidden,
                };

                foreach (var item in track.Items.OrderBy(item => item.TimelineStart))
                {
                    mediaById.TryGetValue(item.MediaAssetId ?? default, out var asset);
                    var modifiers = BuildModifiers(item);
                    var effects = item.Effects.Select(effect => DescribeEffect(effect)).ToList();
                    foreach (var modifier in modifiers)
                        AddModifierCount(modifierCounts, ModifierCategory(modifier), item.Id);
                    foreach (var effect in item.Effects)
                    {
                        effectTypes.Add(effect.EffectTypeId);
                        AddModifierCount(modifierCounts, "effects", item.Id);
                    }

                    trackOverview.Items.Add(new TimelineItemEditOverview
                    {
                        ItemId = item.Id.ToString(),
                        Kind = item.Kind,
                        MediaFile = asset == null || string.IsNullOrWhiteSpace(asset.OriginalPath)
                            ? null
                            : Path.GetFileName(asset.OriginalPath),
                        TimelineStartSeconds = item.TimelineStart.Seconds,
                        DurationSeconds = item.Duration.Seconds,
                        SourceStartSeconds = item.SourceStart.Seconds,
                        Modifiers = modifiers,
                        Effects = effects,
                    });

                    AddReviewHints(overview.ReviewHints, sequence, track, item, asset);
                }

                sequenceOverview.Tracks.Add(trackOverview);
            }

            overview.Sequences.Add(sequenceOverview);
        }

        overview.EffectTypes.AddRange(effectTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        overview.ModifierSummary.AddRange(modifierCounts
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ProjectModifierCount { Modifier = pair.Key, ItemCount = pair.Value.Count }));
        Deduplicate(overview.ReviewHints);
        return overview;
    }

    private static void AddAutomationOverview(
        Project project,
        IReadOnlyDictionary<MediaAssetId, MediaAsset> mediaById,
        ProjectOverview overview)
    {
        project.Workflow.EnsureDefaults();
        foreach (var stage in project.Workflow.Stages)
        {
            overview.WorkflowStages.Add(new WorkflowStageOverview
            {
                Id = stage.Id,
                Name = stage.Name,
                Status = stage.Status,
                RequiresApproval = stage.RequiresApproval,
                Approved = stage.ApprovedUtc.HasValue,
                Summary = stage.Summary,
                Warnings = [.. stage.Warnings],
                Artifacts = [.. stage.ArtifactPaths],
            });
            if (stage.Status == ProductionStageStatus.Failed)
                overview.ReviewHints.Add($"Workflow/{stage.Name}: stage failed. {stage.Summary}".Trim());
            if (stage.Status == ProductionStageStatus.AwaitingApproval)
                overview.ReviewHints.Add($"Workflow/{stage.Name}: waiting for human approval.");
            if (stage.RequiresApproval
                && stage.Status == ProductionStageStatus.Completed
                && !stage.ApprovedUtc.HasValue)
                overview.ReviewHints.Add($"Workflow/{stage.Name}: completed without a recorded approval.");
            foreach (var warning in stage.Warnings)
                overview.ReviewHints.Add($"Workflow/{stage.Name}: {warning}");
        }

        if (project.Workflow.BudgetLimitUsd.HasValue
            && project.Workflow.ActualSpendUsd > project.Workflow.BudgetLimitUsd.Value)
        {
            overview.ReviewHints.Add(
                $"Workflow budget exceeded: spent ${project.Workflow.ActualSpendUsd:0.00} of ${project.Workflow.BudgetLimitUsd.Value:0.00}.");
        }
        if (project.Workflow.PaidProvidersEnabled && !project.Workflow.BudgetLimitUsd.HasValue)
            overview.ReviewHints.Add("Paid automation providers are enabled without a project budget limit.");

        foreach (var provider in project.AutomationProviders)
        {
            overview.AutomationProviders.Add(new AutomationProviderOverview
            {
                Id = provider.Id,
                Name = provider.Name,
                Enabled = provider.Enabled,
                Local = provider.Local,
                Paid = provider.Paid,
                Capabilities = [.. provider.Capabilities],
                Endpoint = provider.Endpoint,
            });
            if (provider.Enabled && provider.Paid && provider.ApprovedUtc == null)
                overview.ReviewHints.Add($"Provider/{provider.Name}: paid provider is enabled without a recorded approval.");
            if (provider.Enabled && !provider.Local && string.IsNullOrWhiteSpace(provider.Endpoint))
                overview.ReviewHints.Add($"Provider/{provider.Name}: remote provider has no endpoint.");
        }
        foreach (var cost in project.Workflow.CostEvents.TakeLast(100))
        {
            overview.CostEvents.Add(new ProductionCostOverview
            {
                Id = cost.Id,
                ProviderId = cost.ProviderId,
                Operation = cost.Operation,
                EstimatedUsd = cost.EstimatedUsd,
                ActualUsd = cost.ActualUsd,
                Status = cost.Status,
                UserApproved = cost.UserApproved,
                Error = cost.Error,
            });
            if (cost.Status == ProductionCostStatus.Failed)
                overview.ReviewHints.Add($"Cost/{cost.ProviderId}/{cost.Operation}: failed after ${cost.ActualUsd:0.00}. {cost.Error}".Trim());
            if (cost.Status == ProductionCostStatus.Reserved && !cost.UserApproved && cost.ReservedUsd > project.Workflow.SingleActionApprovalThresholdUsd)
                overview.ReviewHints.Add($"Cost/{cost.ProviderId}/{cost.Operation}: ${cost.ReservedUsd:0.00} reserved without recorded approval.");
        }

        foreach (var variant in project.ExportVariants)
        {
            overview.ExportVariants.Add(new ExportVariantOverview
            {
                Id = variant.Id,
                Name = variant.Name,
                Width = variant.Width,
                Height = variant.Height,
                Format = variant.Format,
                Quality = variant.Quality,
                Status = variant.Status,
                LastOutputFile = FileNameOrNull(variant.LastOutputPath),
                LastReceiptFile = FileNameOrNull(variant.LastReceiptPath),
                Overrides = new Dictionary<string, string>(variant.Overrides, StringComparer.OrdinalIgnoreCase),
            });
            if (variant.Status == ExportVariantStatus.Failed)
                overview.ReviewHints.Add($"Variant/{variant.Name}: the most recent render failed.");

        }

        foreach (var composition in project.ExternalCompositions)
        {
            overview.ExternalCompositions.Add(new ExternalCompositionOverview
            {
                Id = composition.Id,
                Name = composition.Name,
                Kind = composition.Kind,
                Status = composition.Status,
                Width = composition.Width,
                Height = composition.Height,
                DurationSeconds = composition.DurationSeconds,
                OutputFile = FileNameOrNull(composition.OutputPath),
                OutputSha256 = composition.LastOutputSha256,
                LastError = composition.LastError,
            });
            if (composition.Status is ExternalCompositionStatus.Failed or ExternalCompositionStatus.Offline)
                overview.ReviewHints.Add($"Composition/{composition.Name}: {composition.Status}. {composition.LastError}".Trim());

        }

        foreach (var plan in project.AgentEditPlans.TakeLast(50))
        {
            overview.AgentEditPlans.Add(new AgentEditPlanOverview
            {
                PlanId = plan.PlanId,
                Summary = plan.Summary,
                Status = plan.Status,
                BaseRevision = plan.BaseRevision,
                AppliedRevision = plan.AppliedRevision,
                OperationCount = plan.Operations.Count,
                AffectedRangeCount = plan.AffectedRanges.Count,
                Warnings = [.. plan.Warnings],
                Error = plan.Error,
            });
            if (plan.Status is AgentEditPlanStatus.Failed or AgentEditPlanStatus.Conflict)
                overview.ReviewHints.Add($"Agent plan/{plan.PlanId}: {plan.Status}. {plan.Error}".Trim());
            foreach (var warning in plan.Warnings)
                overview.ReviewHints.Add($"Agent plan/{plan.PlanId}: {warning}");
        }

        foreach (var job in project.RenderJobs.TakeLast(50))
        {
            overview.RenderJobs.Add(new RenderJobOverview
            {
                JobId = job.JobId,
                Kind = job.Kind,
                Status = job.Status,
                OutputFile = FileNameOrNull(job.OutputPath) ?? string.Empty,
                VariantId = job.VariantId,
                CompositionId = job.CompositionId,
                SourceRevision = job.SourceRevision,
                AttemptCount = job.AttemptCount,
                ReceiptId = job.ReceiptId,
                LastError = job.LastError,
            });
            if (job.Status == RenderJobStatus.Failed)
                overview.ReviewHints.Add($"Render job/{job.JobId}: failed and can be retried. {job.LastError}".Trim());
            if (job.Status is RenderJobStatus.Rendering or RenderJobStatus.Verifying
                && job.StartedUtc.HasValue
                && DateTimeOffset.UtcNow - job.StartedUtc.Value > TimeSpan.FromHours(6))
                overview.ReviewHints.Add($"Render job/{job.JobId}: appears stale in {job.Status} state.");
        }

        foreach (var receipt in project.RenderReceipts.TakeLast(50))
        {
            overview.RenderReceipts.Add(new RenderReceiptOverview
            {
                ReceiptId = receipt.ReceiptId,
                OutputFile = FileNameOrNull(receipt.OutputPath) ?? string.Empty,
                ReceiptFile = FileNameOrNull(receipt.ReceiptPath) ?? string.Empty,
                Status = receipt.VerificationStatus,
                ProjectRevision = receipt.ProjectRevision,
                OutputSha256 = receipt.OutputSha256,
                CreatedUtc = receipt.CreatedUtc,
            });
            if (receipt.VerificationStatus == RenderVerificationStatus.Failed)
                overview.ReviewHints.Add($"Render receipt/{receipt.ReceiptId}: export verification failed for {Path.GetFileName(receipt.OutputPath)}.");

        }

        foreach (var analysis in project.MediaIntelligence)
        {
            mediaById.TryGetValue(analysis.MediaAssetId, out var asset);
            overview.MediaAnalyses.Add(new MediaAnalysisOverview
            {
                MediaAssetId = analysis.MediaAssetId.ToString(),
                MediaFile = asset == null ? null : Path.GetFileName(asset.OriginalPath),
                SceneCount = analysis.Scenes.Count,
                TranscriptSegmentCount = analysis.Transcript.Count,
                WordCount = analysis.Transcript.Sum(segment => segment.Words.Count),
                EditingMomentCount = analysis.Moments.Count,
                DuplicateTakeGroupCount = analysis.DuplicateTakeGroups.Count,
                WarningCount = analysis.Warnings.Count,
            });
            foreach (var warning in analysis.Warnings)
                overview.ReviewHints.Add($"Analysis/{Path.GetFileName(asset?.OriginalPath) ?? analysis.MediaAssetId.ToString()}: {warning}");
        }
    }

    private static string? FileNameOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Path.GetFileName(value);

    private static List<string> BuildModifiers(TimelineItem item)
    {
        var result = new List<string>();
        var transform = item.Transform;
        if (Math.Abs(transform.PositionX) > Epsilon || Math.Abs(transform.PositionY) > Epsilon)
            result.Add($"transform position ({Format(transform.PositionX)}, {Format(transform.PositionY)})");
        if (Math.Abs(transform.ScaleX - 1) > Epsilon || Math.Abs(transform.ScaleY - 1) > Epsilon)
            result.Add($"transform scale ({Format(transform.ScaleX)}, {Format(transform.ScaleY)})");
        if (Math.Abs(transform.RotationDegrees) > Epsilon)
            result.Add($"transform rotation {Format(transform.RotationDegrees)}deg");
        if (Math.Abs(item.Opacity - 1) > Epsilon)
            result.Add($"opacity {Format(item.Opacity * 100)}%");
        if (item.CropLeft > Epsilon || item.CropTop > Epsilon || item.CropRight > Epsilon || item.CropBottom > Epsilon)
            result.Add($"crop L{Format(item.CropLeft)} T{Format(item.CropTop)} R{Format(item.CropRight)} B{Format(item.CropBottom)}");
        if (Math.Abs((item.SpeedCurve?.ConstantSpeed ?? item.Speed) - 1) > Epsilon)
            result.Add($"speed {Format(item.SpeedCurve?.ConstantSpeed ?? item.Speed)}x");
        if (item.Reversed) result.Add("reverse playback");
        if (Math.Abs(item.Volume - 1) > Epsilon) result.Add($"volume {Format(item.Volume * 100)}%");
        if (item.Muted) result.Add("muted");
        if (Math.Abs(item.Pan) > Epsilon) result.Add($"pan {Format(item.Pan * 100)}%");
        if (item.FadeInDuration > MediaTime.Zero) result.Add($"audio fade in {Format(item.FadeInDuration.Seconds)}s");
        if (item.FadeOutDuration > MediaTime.Zero) result.Add($"audio fade out {Format(item.FadeOutDuration.Seconds)}s");
        if (item.BlendMode != default) result.Add($"blend mode {item.BlendMode}");
        if (item.ColorCorrection != null) result.Add(DescribeColor(item.ColorCorrection));
        if (item.Stabilization?.Enabled == true) result.Add($"stabilization strength {Format(item.Stabilization.Strength)}");
        if (item.Masks.Count > 0) result.Add($"masks {item.Masks.Count}: {string.Join(", ", item.Masks.Select(mask => mask.Shape))}");
        if (item.AnimationChannels.Count > 0)
        {
            var channels = item.AnimationChannels.Select(channel => $"{channel.PropertyName}({channel.Keyframes.Count} keyframes)");
            result.Add($"animation {string.Join(", ", channels)}");
        }
        if (item.AnimatedProperty != null)
            result.Add($"legacy animation {item.AnimatedProperty.PropertyName}({item.AnimatedProperty.Keyframes.Count} keyframes)");
        if (item.ChromaKey != null) result.Add($"chroma key {item.ChromaKey.Color ?? "unspecified"}");
        if (item.Kind == ItemKind.Text)
        {
            result.Add(
                $"text style {item.FontFamily ?? "Arial"} {Format(item.FontSize)}px " +
                $"{(item.FontBold ? "bold " : string.Empty)}{item.FontAlign}, fill {item.FillColor ?? "#FFFFFF"}, " +
                $"outline {item.OutlineColor ?? "#000000"}/{Format(item.OutlineWidth)}px");
        }
        if (item.Locked) result.Add("item locked");
        return result;
    }

    private static string DescribeEffect(EffectInstance effect)
    {
        var state = effect.Enabled ? "enabled" : "disabled";
        if (effect.Parameters.Count == 0) return $"{effect.EffectTypeId} ({state})";
        var parameters = string.Join(", ", effect.Parameters
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        return $"{effect.EffectTypeId} ({state}; {parameters})";
    }

    private static string DescribeColor(ColorCorrection color) =>
        "color " + string.Join(", ", new[]
        {
            $"brightness {Format(color.Brightness)}",
            $"contrast {Format(color.Contrast)}",
            $"saturation {Format(color.Saturation)}",
            $"exposure {Format(color.Exposure)}",
            color.BlackAndWhite ? "black-and-white" : null,
        }.Where(value => value != null));

    private static void AddReviewHints(
        List<string> hints,
        Sequence sequence,
        Track track,
        TimelineItem item,
        MediaAsset? asset)
    {
        var location = $"{sequence.Name}/{track.Name}/{item.Id}";
        if (item.Duration <= MediaTime.Zero)
            hints.Add($"{location}: item duration is zero or negative.");
        if (item.MediaAssetId.HasValue && asset == null)
            hints.Add($"{location}: referenced media asset is missing from the project library.");
        if (asset?.IsOffline == true)
            hints.Add($"{location}: source media is marked offline.");
        if (item.Kind == ItemKind.Text && string.IsNullOrWhiteSpace(item.TextContent))
            hints.Add($"{location}: text clip has no text content.");

        var audioOnly = asset?.Kind == MediaKind.Audio || track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice;
        if (audioOnly && (item.ColorCorrection != null || item.Stabilization?.Enabled == true || item.Masks.Count > 0))
            hints.Add($"{location}: audio-only item contains visual modifiers; review whether they were applied accidentally.");
        if (item.FadeInDuration.Seconds + item.FadeOutDuration.Seconds > item.Duration.Seconds + Epsilon)
            hints.Add($"{location}: combined audio fades exceed the item duration.");
    }

    private static string ModifierCategory(string modifier)
    {
        var separator = modifier.IndexOf(' ');
        return separator > 0 ? modifier[..separator] : modifier;
    }

    private static void AddModifierCount(
        Dictionary<string, HashSet<TimelineItemId>> counts,
        string category,
        TimelineItemId itemId)
    {
        if (!counts.TryGetValue(category, out var items))
        {
            items = [];
            counts[category] = items;
        }
        items.Add(itemId);
    }

    private static void Deduplicate(List<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        values.Clear();
        values.AddRange(distinct);
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
