using Rushframe.Domain;

namespace Rushframe.Application;

public sealed record MediaAgentContextRequest(
    string Query = "",
    MediaAssetId? MediaAssetId = null,
    IReadOnlyCollection<string>? Roles = null,
    int Limit = 20,
    double MinimumOverallScore = 0);

public sealed record MediaAgentSourceContext(
    MediaAssetId MediaAssetId,
    string SourcePath,
    string SourceChecksum,
    double DurationSeconds,
    string? Orientation,
    int? Width,
    int? Height,
    double? FramesPerSecond,
    bool HasVideo,
    bool HasAudio);

public sealed record MediaAgentContextSummary(
    MediaTime SourceDuration,
    int SceneCount,
    int TranscriptSegmentCount,
    int EditingMomentCount,
    int DuplicateTakeGroupCount,
    IReadOnlyDictionary<string, int> RoleCounts,
    IReadOnlyList<string> BestHookIds);

public sealed record MediaAgentContext(
    string ContextSchemaVersion,
    MediaAgentSourceContext Source,
    MediaAgentContextSummary Summary,
    IReadOnlyList<MediaMomentSearchResult> Moments,
    IReadOnlyList<MediaIntelligenceDuplicateTakeGroup> DuplicateTakeGroups,
    IReadOnlyList<string> Warnings);

public sealed class MediaAgentContextBuilder(MediaIntelligenceSearchService searchService)
{
    public MediaAgentContextBuilder() : this(new MediaIntelligenceSearchService())
    {
    }

    public MediaAgentContext Build(
        MediaIntelligenceAnalysis analysis,
        MediaAgentContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(request);

        if (request.MediaAssetId is { } requestedAssetId && requestedAssetId != analysis.MediaAssetId)
            throw new ArgumentException("The requested media asset does not match the supplied analysis.", nameof(request));

        var moments = searchService.Search(
            analysis,
            new MediaMomentSearchQuery(
                request.Query,
                analysis.MediaAssetId,
                request.Roles,
                request.MinimumOverallScore,
                MaximumDurationSeconds: null,
                Limit: Math.Clamp(request.Limit, 1, 100)));
        var roleCounts = analysis.Moments
            .SelectMany(moment => moment.EditingRoles)
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var bestHookIds = analysis.Moments
            .Where(moment => moment.Scores.HookPotential > 0.35)
            .OrderByDescending(moment => moment.Scores.HookPotential)
            .ThenByDescending(moment => moment.Scores.Overall)
            .Take(5)
            .Select(moment => moment.MomentId)
            .ToList();

        return new MediaAgentContext(
            "1.0",
            new MediaAgentSourceContext(
                analysis.MediaAssetId,
                analysis.SourcePath,
                analysis.SourceChecksum,
                analysis.Metadata.Duration.Seconds,
                analysis.Metadata.Orientation,
                analysis.Metadata.Width,
                analysis.Metadata.Height,
                analysis.Metadata.FramesPerSecond,
                analysis.Metadata.HasVideo,
                analysis.Metadata.HasAudio),
            new MediaAgentContextSummary(
                analysis.Metadata.Duration,
                analysis.Scenes.Count,
                analysis.Transcript.Count,
                analysis.Moments.Count,
                analysis.DuplicateTakeGroups.Count,
                roleCounts,
                bestHookIds),
            moments,
            analysis.DuplicateTakeGroups,
            analysis.Warnings);
    }
}
