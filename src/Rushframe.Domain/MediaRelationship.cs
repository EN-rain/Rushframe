namespace Rushframe.Domain;

public enum MediaRelationshipKind
{
    BrollRelevance,
    ActionReaction,
    SubjectContinuity,
    LocationContinuity,
    MatchingMotion,
    ScreenDirectionCompatibility,
    AlternateTake,
}

public sealed class MediaMomentReference
{
    public MediaAssetId MediaAssetId { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

public sealed class MediaRelationship
{
    public string RelationshipId { get; init; } = Guid.NewGuid().ToString("N");
    public MediaRelationshipKind Kind { get; set; }
    public MediaMomentReference Source { get; set; } = new();
    public MediaMomentReference Target { get; set; } = new();
    public double Score { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> Evidence { get; init; } = [];
}

public static class MediaRelationshipBuilder
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "your", "you", "are", "was", "were",
        "have", "has", "had", "but", "not", "all", "can", "will", "just", "then", "than", "its", "our",
    };

    public static List<MediaRelationship> Build(IEnumerable<MediaIntelligenceAnalysis> analyses)
    {
        var moments = analyses.SelectMany(analysis => analysis.Moments.Select(moment => new MomentContext(
                analysis.MediaAssetId,
                moment,
                ResolveScenes(analysis, moment))))
            .ToArray();
        var relationships = new List<MediaRelationship>();

        for (var leftIndex = 0; leftIndex < moments.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < moments.Length; rightIndex++)
            {
                var left = moments[leftIndex];
                var right = moments[rightIndex];
                if (left.AssetId == right.AssetId && left.Moment.MomentId == right.Moment.MomentId) continue;

                AddIfStrong(relationships, left, right, MediaRelationshipKind.BrollRelevance, BrollScore(left, right),
                    "Visual tags and subjects support the other moment's spoken message.");
                AddIfStrong(relationships, left, right, MediaRelationshipKind.ActionReaction, ActionReactionScore(left, right),
                    "One moment contains an action while the other contains a compatible reaction or emotional response.");
                AddIfStrong(relationships, left, right, MediaRelationshipKind.SubjectContinuity, SubjectScore(left, right),
                    "Moments share one or more detected subjects.");
                AddIfStrong(relationships, left, right, MediaRelationshipKind.LocationContinuity, LocationScore(left, right),
                    "Moments share the same detected location.");
                AddIfStrong(relationships, left, right, MediaRelationshipKind.MatchingMotion, MotionScore(left, right),
                    "Moments have compatible detected camera or action motion.");
                AddIfStrong(relationships, left, right, MediaRelationshipKind.ScreenDirectionCompatibility, DirectionScore(left, right),
                    "Moments have compatible inferred screen direction.");
            }
        }

        foreach (var analysis in analyses)
        {
            foreach (var group in analysis.DuplicateTakeGroups)
            {
                var candidates = group.Candidates.ToArray();
                for (var index = 0; index < candidates.Length; index++)
                {
                    for (var other = index + 1; other < candidates.Length; other++)
                    {
                        var left = analysis.Moments.FirstOrDefault(moment => moment.MomentId == candidates[index].MomentId);
                        var right = analysis.Moments.FirstOrDefault(moment => moment.MomentId == candidates[other].MomentId);
                        if (left == null || right == null) continue;
                        relationships.Add(Create(
                            MediaRelationshipKind.AlternateTake,
                            new MomentContext(analysis.MediaAssetId, left, ResolveScenes(analysis, left)),
                            new MomentContext(analysis.MediaAssetId, right, ResolveScenes(analysis, right)),
                            Math.Clamp(Math.Max(candidates[index].Score, candidates[other].Score), 0, 1),
                            string.IsNullOrWhiteSpace(group.Purpose) ? "Moments belong to the same duplicate-take group." : group.Purpose));
                    }
                }
            }
        }

        return relationships
            .GroupBy(value => $"{value.Kind}|{value.Source.MediaAssetId}|{value.Source.MomentId}|{value.Target.MediaAssetId}|{value.Target.MomentId}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(value => value.Score).First())
            .OrderByDescending(value => value.Score)
            .Take(5000)
            .ToList();
    }

    private static void AddIfStrong(List<MediaRelationship> output, MomentContext left, MomentContext right, MediaRelationshipKind kind, double score, string reason)
    {
        if (score < 0.55) return;
        output.Add(Create(kind, left, right, score, reason));
    }

    private static MediaRelationship Create(MediaRelationshipKind kind, MomentContext left, MomentContext right, double score, string reason) => new()
    {
        Kind = kind,
        Source = Reference(left),
        Target = Reference(right),
        Score = Math.Round(Math.Clamp(score, 0, 1), 3),
        Reason = reason,
        Evidence = { left.Moment.Summary, right.Moment.Summary },
    };

    private static MediaMomentReference Reference(MomentContext value) => new()
    {
        MediaAssetId = value.AssetId,
        MomentId = value.Moment.MomentId,
        StartSeconds = value.Moment.Start.Seconds,
        EndSeconds = value.Moment.End.Seconds,
    };

    private static double BrollScore(MomentContext left, MomentContext right)
    {
        var leftVisual = Tokens(string.Join(' ', left.Moment.Visual, left.Moment.Summary, string.Join(' ', left.Moment.Tags), string.Join(' ', left.Scenes.SelectMany(scene => scene.Subjects.Concat(scene.Actions).Concat(scene.Tags)))));
        var rightSpeech = Tokens(string.Join(' ', right.Moment.Speech, right.Moment.Summary));
        var reverse = Similarity(Tokens(string.Join(' ', right.Moment.Visual, right.Moment.Summary, string.Join(' ', right.Moment.Tags))), Tokens(string.Join(' ', left.Moment.Speech, left.Moment.Summary)));
        var forward = Similarity(leftVisual, rightSpeech);
        var roleBonus = left.Moment.EditingRoles.Any(role => role.Contains("b-roll", StringComparison.OrdinalIgnoreCase))
                        || right.Moment.EditingRoles.Any(role => role.Contains("b-roll", StringComparison.OrdinalIgnoreCase)) ? 0.2 : 0;
        return Math.Min(1, Math.Max(forward, reverse) + roleBonus);
    }

    private static double ActionReactionScore(MomentContext left, MomentContext right)
    {
        var leftActions = Tokens(string.Join(' ', left.Scenes.SelectMany(scene => scene.Actions)));
        var rightActions = Tokens(string.Join(' ', right.Scenes.SelectMany(scene => scene.Actions)));
        var emotional = left.Moment.Scores.EmotionalIntensity > 0.55 || right.Moment.Scores.EmotionalIntensity > 0.55;
        var reactionWords = Tokens(string.Join(' ', left.Moment.Summary, right.Moment.Summary, left.Moment.Speech, right.Moment.Speech));
        var reaction = reactionWords.Overlaps(new[] { "react", "reaction", "laugh", "smile", "shock", "surprise", "respond", "look" });
        return leftActions.Count > 0 && rightActions.Count > 0 && (emotional || reaction) ? 0.65 + Similarity(leftActions, rightActions) * 0.2 : 0;
    }

    private static double SubjectScore(MomentContext left, MomentContext right) =>
        Similarity(Tokens(string.Join(' ', left.Scenes.SelectMany(scene => scene.Subjects))), Tokens(string.Join(' ', right.Scenes.SelectMany(scene => scene.Subjects))));

    private static double LocationScore(MomentContext left, MomentContext right)
    {
        var leftLocations = left.Scenes.Select(scene => scene.Location).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightLocations = right.Scenes.Select(scene => scene.Location).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return leftLocations.Count > 0 && leftLocations.Overlaps(rightLocations) ? 0.9 : 0;
    }

    private static double MotionScore(MomentContext left, MomentContext right)
    {
        var leftMotion = Tokens(string.Join(' ', left.Scenes.Select(scene => scene.CameraMotion).Where(value => value != null)!));
        var rightMotion = Tokens(string.Join(' ', right.Scenes.Select(scene => scene.CameraMotion).Where(value => value != null)!));
        return Similarity(leftMotion, rightMotion);
    }

    private static double DirectionScore(MomentContext left, MomentContext right)
    {
        var leftDirection = Direction(left);
        var rightDirection = Direction(right);
        if (leftDirection == 0 || rightDirection == 0) return 0;
        return leftDirection == rightDirection ? 0.8 : 0;
    }

    private static int Direction(MomentContext value)
    {
        var text = string.Join(' ', value.Moment.Visual, value.Moment.Summary, string.Join(' ', value.Scenes.SelectMany(scene => scene.Actions)), string.Join(' ', value.Scenes.Select(scene => scene.CameraMotion)));
        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("left to right") || normalized.Contains("moves right") || normalized.Contains("pan right")) return 1;
        if (normalized.Contains("right to left") || normalized.Contains("moves left") || normalized.Contains("pan left")) return -1;
        return 0;
    }

    private static IReadOnlyList<MediaIntelligenceScene> ResolveScenes(MediaIntelligenceAnalysis analysis, MediaIntelligenceMoment moment)
    {
        var ids = moment.SceneIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return analysis.Scenes.Where(scene => ids.Contains(scene.SceneId)
            || (scene.End.Seconds > moment.Start.Seconds && scene.Start.Seconds < moment.End.Seconds)).ToArray();
    }

    private static HashSet<string> Tokens(string? text) => (text ?? string.Empty)
        .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Trim().ToLowerInvariant())
        .Where(value => value.Length >= 3 && !StopWords.Contains(value))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static double Similarity(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Count(right.Contains);
        return intersection == 0 ? 0 : intersection / (double)Math.Min(left.Count, right.Count);
    }

    private sealed record MomentContext(MediaAssetId AssetId, MediaIntelligenceMoment Moment, IReadOnlyList<MediaIntelligenceScene> Scenes);
}
