using System.Text.RegularExpressions;
using Rushframe.Domain;

namespace Rushframe.Application;

public sealed record MediaMomentSearchQuery(
    string Query,
    MediaAssetId? MediaAssetId = null,
    IReadOnlyCollection<string>? Roles = null,
    double MinimumOverallScore = 0,
    double? MaximumDurationSeconds = null,
    int Limit = 12);

public sealed record MediaMomentSearchResult(
    string MomentId,
    MediaTime Start,
    MediaTime End,
    string Summary,
    double MatchScore,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Evidence);

public sealed class MediaIntelligenceSearchService
{
    private static readonly Regex Tokens = new("[\\p{L}\\p{N}']+", RegexOptions.Compiled);

    public IReadOnlyList<MediaMomentSearchResult> Search(
        MediaIntelligenceAnalysis analysis,
        MediaMomentSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(query);

        var queryTokens = Tokenize(query.Query);
        var requiredRoles = query.Roles?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return analysis.Moments
            .Where(moment => moment.Scores.Overall >= Math.Clamp(query.MinimumOverallScore, 0, 1))
            .Where(moment => query.MaximumDurationSeconds is null
                || moment.End.Subtract(moment.Start).Seconds <= query.MaximumDurationSeconds.Value)
            .Where(moment => requiredRoles is null || requiredRoles.Count == 0
                || moment.EditingRoles.Any(requiredRoles.Contains))
            .Select(moment => new
            {
                Moment = moment,
                Score = Score(moment, queryTokens),
            })
            .Where(candidate => queryTokens.Count == 0 || candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Moment.Scores.Overall)
            .ThenBy(candidate => candidate.Moment.Start.Seconds)
            .Take(Math.Clamp(query.Limit, 1, 100))
            .Select(candidate => new MediaMomentSearchResult(
                candidate.Moment.MomentId,
                candidate.Moment.Start,
                candidate.Moment.End,
                candidate.Moment.Summary,
                candidate.Score,
                candidate.Moment.EditingRoles,
                candidate.Moment.Tags,
                candidate.Moment.Evidence))
            .ToList();
    }

    public IReadOnlyList<MediaMomentSearchResult> FindHooks(
        MediaIntelligenceAnalysis analysis,
        int limit = 5,
        double? maximumDurationSeconds = 8) =>
        Search(analysis, new MediaMomentSearchQuery(
            string.Empty,
            Roles: ["hook"],
            MaximumDurationSeconds: maximumDurationSeconds,
            Limit: limit));

    public IReadOnlyList<MediaMomentSearchResult> FindBroll(
        MediaIntelligenceAnalysis analysis,
        string query,
        int limit = 12) =>
        Search(analysis, new MediaMomentSearchQuery(query, Roles: ["b-roll"], Limit: limit));

    public MediaMomentSearchResult? FindBestTake(
        MediaIntelligenceAnalysis analysis,
        string duplicateTakeGroupId)
    {
        var group = analysis.DuplicateTakeGroups.FirstOrDefault(candidate =>
            string.Equals(candidate.GroupId, duplicateTakeGroupId, StringComparison.OrdinalIgnoreCase));
        if (group == null) return null;
        var recommended = group.Candidates
            .OrderByDescending(candidate => candidate.Recommended)
            .ThenByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (recommended == null) return null;
        var moment = analysis.Moments.FirstOrDefault(candidate => candidate.MomentId == recommended.MomentId);
        return moment == null
            ? null
            : new MediaMomentSearchResult(
                moment.MomentId,
                moment.Start,
                moment.End,
                moment.Summary,
                recommended.Score,
                moment.EditingRoles,
                moment.Tags,
                moment.Evidence);
    }

    private static double Score(MediaIntelligenceMoment moment, HashSet<string> queryTokens)
    {
        if (queryTokens.Count == 0) return moment.Scores.Overall;
        var summary = Tokenize(moment.Summary);
        var speech = Tokenize(moment.Speech ?? string.Empty);
        var visual = Tokenize(moment.Visual ?? string.Empty);
        var audio = Tokenize(moment.Audio ?? string.Empty);
        var roles = moment.EditingRoles.SelectMany(Tokenize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tags = moment.Tags.SelectMany(Tokenize).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var weightedHits = queryTokens.Sum(token =>
            (summary.Contains(token) ? 4.0 : 0)
            + (speech.Contains(token) ? 3.5 : 0)
            + (visual.Contains(token) ? 2.5 : 0)
            + (audio.Contains(token) ? 1.5 : 0)
            + (roles.Contains(token) ? 2.0 : 0)
            + (tags.Contains(token) ? 1.5 : 0));
        var maximum = queryTokens.Count * 15.0;
        var lexical = maximum > 0 ? weightedHits / maximum : 0;
        return Math.Clamp(lexical * 0.75 + moment.Scores.Overall * 0.25, 0, 1);
    }

    private static HashSet<string> Tokenize(string text) =>
        Tokens.Matches(text ?? string.Empty)
            .Select(match => match.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
