using System.IO;
using Rushframe.Domain;

namespace Rushframe.Desktop.Controllers;

internal sealed record AgentEditingContextRequest(
    int ItemLimit = 250,
    int MediaAssetLimit = 200,
    bool IncludeCompletedTasks = false);

internal sealed record AgentEditingTaskContext(
    string Id,
    string Title,
    bool Completed);

internal sealed record AgentEditingBriefContext(
    string Purpose,
    string TargetAudience,
    string Platform,
    string AspectRatio,
    double? TargetDurationSeconds,
    string Tone,
    string EditingStyle,
    string Pacing,
    double? HookDeadlineSeconds,
    IReadOnlyList<string> RequiredMessages,
    IReadOnlyList<string> RequiredMediaAssetIds,
    IReadOnlyList<string> ForbiddenMediaAssetIds,
    string CaptionPolicy,
    string MusicPolicy,
    string SoundEffectsPolicy,
    string TransitionPolicy,
    string CallToAction,
    IReadOnlyList<string> BrandColors,
    IReadOnlyList<string> BrandFonts,
    string LogoPolicy,
    string ReferenceNotes);

internal sealed record AgentEditingItemContext(
    string Id,
    string Kind,
    string TrackId,
    string? MediaAssetId,
    string? MediaName,
    double StartSeconds,
    double EndSeconds,
    double DurationSeconds,
    double SourceStartSeconds,
    double SourceDurationSeconds,
    bool Locked,
    bool Muted,
    string? TextPreview,
    int EffectCount,
    int MaskCount,
    int AnimationChannelCount);

internal sealed record AgentEditingTrackContext(
    string Id,
    int Index,
    string Kind,
    string Name,
    bool Locked,
    bool Muted,
    bool Solo,
    bool Hidden,
    int TotalItemCount,
    bool ItemsTruncated,
    IReadOnlyList<AgentEditingItemContext> Items);

internal sealed record AgentEditingMediaContext(
    string Id,
    string Name,
    string Kind,
    double DurationSeconds,
    int PixelWidth,
    int PixelHeight,
    bool Offline,
    bool Analyzed,
    int TimelineUsageCount,
    bool RequiredByBrief,
    bool ForbiddenByBrief,
    string? LicenseName,
    bool RequiresAttribution);

internal sealed record AgentEditingMarkerContext(
    string Id,
    double TimeSeconds,
    double DurationSeconds,
    string Label,
    string? Note);

internal sealed record AgentEditingTransitionContext(
    string LeftItemId,
    string RightItemId,
    string Kind,
    double DurationSeconds,
    double Alignment);

internal sealed record AgentEditingSequenceContext(
    string Id,
    string Name,
    int Width,
    int Height,
    int FrameRateNumerator,
    int FrameRateDenominator,
    double DurationSeconds,
    int TrackCount,
    int ItemCount,
    IReadOnlyList<AgentEditingMarkerContext> Markers,
    bool MarkersTruncated,
    IReadOnlyList<AgentEditingTransitionContext> Transitions,
    bool TransitionsTruncated);

internal sealed record AgentEditingSkillSummary(
    string CatalogVersion,
    int ActionCount,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Categories,
    string ContractEndpoint);

internal sealed record AgentEditingContext(
    string ContextSchemaVersion,
    long Revision,
    string ProjectName,
    string CampaignDescription,
    AgentEditingBriefContext EditingBrief,
    IReadOnlyList<AgentEditingTaskContext> Tasks,
    bool TasksTruncated,
    double PlayheadSeconds,
    string? SelectedItemId,
    AgentEditingSequenceContext Sequence,
    IReadOnlyList<AgentEditingTrackContext> Tracks,
    bool TimelineItemsTruncated,
    IReadOnlyList<AgentEditingMediaContext> MediaAssets,
    bool MediaAssetsTruncated,
    IReadOnlyList<TimelineQualityIssue> QualityIssues,
    bool QualityIssuesTruncated,
    AgentEditingSkillSummary EditSkills,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> RecommendedWorkflow);

/// <summary>
/// Builds a bounded, path-safe snapshot that gives an editing agent enough
/// project intent and target state to plan edits without downloading the full
/// persisted project or guessing action payloads.
/// </summary>
internal static class AgentEditingContextBuilder
{
    public const string SchemaVersion = "2.0";
    private const int MaximumQualityIssues = 50;
    private const int MaximumTasks = 100;
    private const int MaximumMarkers = 50;
    private const int MaximumTransitions = 100;
    private const int MaximumBriefListItems = 100;
    private const int MaximumTextLength = 4000;
    private const int MaximumListTextLength = 500;

    public static AgentEditingContext Build(
        Project project,
        Sequence sequence,
        double playheadSeconds,
        TimelineItemId? selectedItemId,
        AgentEditingContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(request);

        var itemLimit = Math.Clamp(request.ItemLimit, 25, 500);
        var mediaAssetLimit = Math.Clamp(request.MediaAssetLimit, 1, 500);
        var allItems = sequence.Tracks
            .SelectMany((track, trackIndex) => track.Items.Select(item => new ItemLocation(track, trackIndex, item)))
            .ToArray();
        var selectedFirst = allItems
            .Where(location => selectedItemId.HasValue && location.Item.Id == selectedItemId.Value)
            .Concat(allItems
                .Where(location => !selectedItemId.HasValue || location.Item.Id != selectedItemId.Value)
                .OrderBy(location => location.Item.TimelineStart)
                .ThenBy(location => location.TrackIndex))
            .Take(itemLimit)
            .Select(location => location.Item.Id)
            .ToHashSet();
        var mediaNames = project.MediaLibrary.ToDictionary(
            asset => asset.Id,
            asset => TrimText(Path.GetFileName(asset.OriginalPath), MaximumListTextLength));
        var tracks = sequence.Tracks.Select((track, index) =>
        {
            var included = track.Items
                .Where(item => selectedFirst.Contains(item.Id))
                .OrderBy(item => item.TimelineStart)
                .Select(item => BuildItem(track, item, mediaNames))
                .ToArray();
            return new AgentEditingTrackContext(
                track.Id.ToString(),
                index,
                track.Kind.ToString(),
                TrimText(track.Name, MaximumListTextLength),
                track.Locked,
                track.Muted,
                track.Solo,
                track.Hidden,
                track.Items.Count,
                included.Length < track.Items.Count,
                included);
        }).ToArray();

        var usageCounts = allItems
            .Where(location => location.Item.MediaAssetId.HasValue)
            .GroupBy(location => location.Item.MediaAssetId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var mediaAssets = project.MediaLibrary
            .OrderByDescending(asset => project.EditingBrief.RequiredMediaAssetIds.Contains(asset.Id)
                                        || project.EditingBrief.ForbiddenMediaAssetIds.Contains(asset.Id))
            .ThenByDescending(asset => usageCounts.ContainsKey(asset.Id))
            .ThenByDescending(asset => project.MediaIntelligence.Any(analysis => analysis.MediaAssetId == asset.Id))
            .ThenBy(asset => asset.Kind)
            .ThenBy(asset => Path.GetFileName(asset.OriginalPath), StringComparer.OrdinalIgnoreCase)
            .Take(mediaAssetLimit)
            .Select(asset => new AgentEditingMediaContext(
                asset.Id.ToString(),
                TrimText(Path.GetFileName(asset.OriginalPath), MaximumListTextLength),
                asset.Kind.ToString(),
                asset.Duration.Seconds,
                asset.PixelWidth,
                asset.PixelHeight,
                asset.IsOffline,
                project.MediaIntelligence.Any(analysis => analysis.MediaAssetId == asset.Id),
                usageCounts.GetValueOrDefault(asset.Id),
                project.EditingBrief.RequiredMediaAssetIds.Contains(asset.Id),
                project.EditingBrief.ForbiddenMediaAssetIds.Contains(asset.Id),
                string.IsNullOrWhiteSpace(asset.LicenseName) ? null : asset.LicenseName,
                asset.RequiresAttribution))
            .ToArray();

        var taskCandidates = project.Tasks
            .Where(task => request.IncludeCompletedTasks || !task.IsCompleted)
            .ToArray();
        var tasks = taskCandidates
            .Take(MaximumTasks)
            .Select(task => new AgentEditingTaskContext(task.Id.ToString(), TrimText(task.Title, MaximumListTextLength), task.IsCompleted))
            .ToArray();
        var quality = TimelineQualityAnalyzer.Analyze(project, sequence);
        var markers = sequence.Markers
            .OrderBy(marker => marker.Time)
            .Take(MaximumMarkers)
            .Select(marker => new AgentEditingMarkerContext(
                marker.Id.ToString(),
                marker.Time.Seconds,
                marker.Duration.Seconds,
                TrimText(marker.Label, MaximumListTextLength),
                TrimNullableText(marker.Note, MaximumListTextLength)))
            .ToArray();
        var transitions = sequence.Transitions
            .Take(MaximumTransitions)
            .Select(transition => new AgentEditingTransitionContext(
                transition.LeftItemId.ToString(),
                transition.RightItemId.ToString(),
                transition.Kind.ToString(),
                transition.Duration.Seconds,
                transition.Alignment))
            .ToArray();
        var categories = AgentEditSkillCatalog.Skills
            .GroupBy(skill => skill.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary<IGrouping<string, AgentEditSkillDefinition>, string, IReadOnlyList<string>>(
                group => group.Key,
                group => group.Select(skill => skill.Action).OrderBy(action => action, StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new AgentEditingContext(
            SchemaVersion,
            project.Revision,
            TrimText(project.Name, MaximumListTextLength),
            TrimText(project.CampaignDescription, MaximumTextLength),
            BuildBrief(project.EditingBrief),
            tasks,
            taskCandidates.Length > tasks.Length,
            Math.Max(0, playheadSeconds),
            selectedItemId?.ToString(),
            new AgentEditingSequenceContext(
                sequence.Id.ToString(),
                sequence.Name,
                sequence.Width,
                sequence.Height,
                sequence.FrameRate.Numerator,
                sequence.FrameRate.Denominator,
                sequence.Duration.Seconds,
                sequence.Tracks.Count,
                allItems.Length,
                markers,
                markers.Length < sequence.Markers.Count,
                transitions,
                transitions.Length < sequence.Transitions.Count),
            tracks,
            selectedFirst.Count < allItems.Length,
            mediaAssets,
            mediaAssets.Length < project.MediaLibrary.Count,
            quality.Take(MaximumQualityIssues).ToArray(),
            quality.Count > MaximumQualityIssues,
            new AgentEditingSkillSummary(
                AgentEditSkillCatalog.SchemaVersion,
                AgentEditSkillCatalog.Skills.Count,
                categories,
                "capabilities.editPlan.skills"),
            [
                "Use only media assets registered in this open project.",
                "Never modify original source files.",
                "Treat locked tracks and items as protected.",
                "Preview plans before application and require the current base_revision.",
                "Apply one logical multi-step change as one atomic, undoable edit plan.",
                "Manual edits win; refresh context after every successful mutation or revision conflict.",
            ],
            [
                "Read this editing context and the action contracts from capabilities.",
                "Search analyzed media moments only when the brief requires source selection.",
                "Submit a creative beat sheet and operations to preview_edit_plan.",
                "Use review_edit_plan for an isolated corrective pass when quality warnings matter.",
                "Apply the approved plan with apply_edit_plan using the unchanged base_revision.",
                "Refresh editing context after the project revision changes.",
            ]);
    }

    private static AgentEditingBriefContext BuildBrief(EditingBrief brief) =>
        new(
            TrimText(brief.Purpose, MaximumTextLength),
            TrimText(brief.TargetAudience, MaximumTextLength),
            TrimText(brief.Platform, MaximumListTextLength),
            TrimText(brief.AspectRatio, MaximumListTextLength),
            brief.TargetDurationSeconds,
            TrimText(brief.Tone, MaximumListTextLength),
            TrimText(brief.EditingStyle, MaximumListTextLength),
            TrimText(brief.Pacing, MaximumTextLength),
            brief.HookDeadlineSeconds,
            TrimList(brief.RequiredMessages),
            brief.RequiredMediaAssetIds.Take(MaximumBriefListItems).Select(id => id.ToString()).ToArray(),
            brief.ForbiddenMediaAssetIds.Take(MaximumBriefListItems).Select(id => id.ToString()).ToArray(),
            TrimText(brief.CaptionPolicy, MaximumTextLength),
            TrimText(brief.MusicPolicy, MaximumTextLength),
            TrimText(brief.SoundEffectsPolicy, MaximumTextLength),
            TrimText(brief.TransitionPolicy, MaximumTextLength),
            TrimText(brief.CallToAction, MaximumTextLength),
            TrimList(brief.BrandColors),
            TrimList(brief.BrandFonts),
            TrimText(brief.LogoPolicy, MaximumTextLength),
            TrimText(brief.ReferenceNotes, MaximumTextLength));

    private static IReadOnlyList<string> TrimList(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(MaximumBriefListItems)
            .Select(value => TrimText(value, MaximumListTextLength))
            .ToArray();

    private static string TrimText(string? value, int maximumLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maximumLength ? text : $"{text[..Math.Max(0, maximumLength - 3)]}...";
    }

    private static string? TrimNullableText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : TrimText(value, maximumLength);

    private static AgentEditingItemContext BuildItem(
        Track track,
        TimelineItem item,
        IReadOnlyDictionary<MediaAssetId, string> mediaNames) =>
        new(
            item.Id.ToString(),
            item.Kind.ToString(),
            track.Id.ToString(),
            item.MediaAssetId?.ToString(),
            item.MediaAssetId is { } mediaAssetId && mediaNames.TryGetValue(mediaAssetId, out var mediaName) ? mediaName : null,
            item.TimelineStart.Seconds,
            item.TimelineEnd.Seconds,
            item.Duration.Seconds,
            item.SourceStart.Seconds,
            item.SourceDuration.Seconds,
            item.Locked,
            item.Muted,
            BuildTextPreview(item.TextContent),
            item.Effects.Count,
            item.Masks.Count,
            item.AnimationChannels.Count);

    private static string? BuildTextPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var compact = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 160 ? compact : $"{compact[..157]}...";
    }

    private sealed record ItemLocation(Track Track, int TrackIndex, TimelineItem Item);
}
