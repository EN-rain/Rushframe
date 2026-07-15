namespace Rushframe.Domain;

public sealed class EditingBrief
{
    public string Purpose { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public double? TargetDurationSeconds { get; set; }
    public string Tone { get; set; } = string.Empty;
    public string EditingStyle { get; set; } = "custom";
    public string Pacing { get; set; } = string.Empty;
    public double? HookDeadlineSeconds { get; set; }
    public List<string> RequiredMessages { get; init; } = [];
    public List<MediaAssetId> RequiredMediaAssetIds { get; init; } = [];
    public List<MediaAssetId> ForbiddenMediaAssetIds { get; init; } = [];
    public string CaptionPolicy { get; set; } = string.Empty;
    public string MusicPolicy { get; set; } = string.Empty;
    public string SoundEffectsPolicy { get; set; } = string.Empty;
    public string TransitionPolicy { get; set; } = string.Empty;
    public string CallToAction { get; set; } = string.Empty;
    public List<string> BrandColors { get; init; } = [];
    public List<string> BrandFonts { get; init; } = [];
    public string LogoPolicy { get; set; } = string.Empty;
    public string ReferenceNotes { get; set; } = string.Empty;

    public void Normalize()
    {
        EditingStyle = string.IsNullOrWhiteSpace(EditingStyle) ? "custom" : EditingStyle.Trim();
        if (TargetDurationSeconds is <= 0) TargetDurationSeconds = null;
        if (HookDeadlineSeconds is <= 0) HookDeadlineSeconds = null;
        Deduplicate(RequiredMessages);
        Deduplicate(BrandColors);
        Deduplicate(BrandFonts);
    }

    private static void Deduplicate(List<string> values)
    {
        var normalized = values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        values.Clear();
        values.AddRange(normalized);
    }
}

public sealed class EditingStyleProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double DefaultHookDeadlineSeconds { get; init; }
    public double MinimumShotDurationSeconds { get; init; }
    public double MaximumAverageShotDurationSeconds { get; init; }
    public double MaximumSilenceSeconds { get; init; }
    public double MaximumTransitionDensityPerMinute { get; init; }
    public int MaximumCaptionCharactersPerLine { get; init; }
    public double MaximumCaptionCharactersPerSecond { get; init; }
    public double SuggestedMusicVolume { get; init; }

    public static IReadOnlyList<EditingStyleProfile> BuiltIns { get; } =
    [
        new() { Id = "talking-head-short", Name = "Talking-head short", DefaultHookDeadlineSeconds = 1.5, MinimumShotDurationSeconds = 0.35, MaximumAverageShotDurationSeconds = 3, MaximumSilenceSeconds = 0.45, MaximumTransitionDensityPerMinute = 4, MaximumCaptionCharactersPerLine = 34, MaximumCaptionCharactersPerSecond = 18, SuggestedMusicVolume = 0.16 },
        new() { Id = "podcast-clip", Name = "Podcast clip", DefaultHookDeadlineSeconds = 2, MinimumShotDurationSeconds = 0.5, MaximumAverageShotDurationSeconds = 5, MaximumSilenceSeconds = 0.7, MaximumTransitionDensityPerMinute = 2, MaximumCaptionCharactersPerLine = 38, MaximumCaptionCharactersPerSecond = 17, SuggestedMusicVolume = 0.10 },
        new() { Id = "product-ad", Name = "Product advertisement", DefaultHookDeadlineSeconds = 1.2, MinimumShotDurationSeconds = 0.3, MaximumAverageShotDurationSeconds = 2.5, MaximumSilenceSeconds = 0.3, MaximumTransitionDensityPerMinute = 8, MaximumCaptionCharactersPerLine = 32, MaximumCaptionCharactersPerSecond = 18, SuggestedMusicVolume = 0.20 },
        new() { Id = "tutorial", Name = "Tutorial", DefaultHookDeadlineSeconds = 3, MinimumShotDurationSeconds = 0.6, MaximumAverageShotDurationSeconds = 6, MaximumSilenceSeconds = 0.9, MaximumTransitionDensityPerMinute = 2, MaximumCaptionCharactersPerLine = 40, MaximumCaptionCharactersPerSecond = 16, SuggestedMusicVolume = 0.08 },
        new() { Id = "cinematic-trailer", Name = "Cinematic trailer", DefaultHookDeadlineSeconds = 2.5, MinimumShotDurationSeconds = 0.2, MaximumAverageShotDurationSeconds = 3.5, MaximumSilenceSeconds = 1.2, MaximumTransitionDensityPerMinute = 7, MaximumCaptionCharactersPerLine = 30, MaximumCaptionCharactersPerSecond = 15, SuggestedMusicVolume = 0.28 },
        new() { Id = "gaming-montage", Name = "Gaming montage", DefaultHookDeadlineSeconds = 1, MinimumShotDurationSeconds = 0.15, MaximumAverageShotDurationSeconds = 2, MaximumSilenceSeconds = 0.25, MaximumTransitionDensityPerMinute = 10, MaximumCaptionCharactersPerLine = 30, MaximumCaptionCharactersPerSecond = 20, SuggestedMusicVolume = 0.25 },
        new() { Id = "documentary", Name = "Documentary", DefaultHookDeadlineSeconds = 4, MinimumShotDurationSeconds = 0.7, MaximumAverageShotDurationSeconds = 7, MaximumSilenceSeconds = 1.2, MaximumTransitionDensityPerMinute = 2, MaximumCaptionCharactersPerLine = 42, MaximumCaptionCharactersPerSecond = 15, SuggestedMusicVolume = 0.12 },
        new() { Id = "social-highlight", Name = "Social-media highlight", DefaultHookDeadlineSeconds = 1.5, MinimumShotDurationSeconds = 0.25, MaximumAverageShotDurationSeconds = 3, MaximumSilenceSeconds = 0.4, MaximumTransitionDensityPerMinute = 6, MaximumCaptionCharactersPerLine = 34, MaximumCaptionCharactersPerSecond = 18, SuggestedMusicVolume = 0.18 },
    ];

    public static EditingStyleProfile Resolve(string? id) =>
        BuiltIns.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? BuiltIns.First(profile => profile.Id == "social-highlight");
}

public sealed class AgentCreativePlan
{
    public string Objective { get; set; } = string.Empty;
    public double? TargetDurationSeconds { get; set; }
    public string PacingStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public string CaptionStrategy { get; set; } = string.Empty;
    public List<string> Assumptions { get; init; } = [];
    public List<AgentEditBeat> Beats { get; init; } = [];
}

public sealed class AgentEditBeat
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> MomentIds { get; init; } = [];
    public List<MediaAssetId> MediaAssetIds { get; init; } = [];
    public string Reason { get; set; } = string.Empty;
}

public sealed class AgentPlanQualityScores
{
    public double BriefCompliance { get; set; }
    public double NarrativeCompleteness { get; set; }
    public double Continuity { get; set; }
    public double Pacing { get; set; }
    public double DialogueQuality { get; set; }
    public double AudioQuality { get; set; }
    public double CaptionQuality { get; set; }
    public double AssetValidity { get; set; }
    public double TechnicalValidity { get; set; }
    public double Overall => Math.Round((BriefCompliance + NarrativeCompleteness + Continuity + Pacing + DialogueQuality + AudioQuality + CaptionQuality + AssetValidity + TechnicalValidity) / 9d, 3);
}

public enum TimelineQualitySeverity { Info, Warning, Error }

public sealed class TimelineQualityIssue
{
    public string Code { get; set; } = string.Empty;
    public TimelineQualitySeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public string? ItemId { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

public static class TimelineQualityAnalyzer
{
    public static List<TimelineQualityIssue> Analyze(Project project, Sequence sequence)
    {
        var brief = project.EditingBrief;
        var profile = EditingStyleProfile.Resolve(brief.EditingStyle);
        var issues = new List<TimelineQualityIssue>();
        var items = sequence.Tracks.SelectMany(track => track.Items.Select(item => (track, item)))
            .OrderBy(pair => pair.item.TimelineStart.Seconds).ToArray();
        var duration = sequence.Duration.Seconds;

        if (brief.TargetDurationSeconds is { } target && Math.Abs(duration - target) > Math.Max(1, target * 0.05))
            issues.Add(Issue("duration-target", TimelineQualitySeverity.Warning, $"Timeline duration {duration:0.##}s differs from the {target:0.##}s brief target.", 0, duration));

        var hookDeadline = brief.HookDeadlineSeconds ?? profile.DefaultHookDeadlineSeconds;
        var firstMeaningful = items.FirstOrDefault(pair => pair.item.Kind != ItemKind.AdjustmentLayer && pair.item.Duration.Seconds >= 0.15);
        if (firstMeaningful.item != null && firstMeaningful.item.TimelineStart.Seconds > hookDeadline)
            issues.Add(Issue("late-hook", TimelineQualitySeverity.Warning, $"The first meaningful item starts after the {hookDeadline:0.##}s hook deadline.", 0, firstMeaningful.item.TimelineStart.Seconds));

        foreach (var (track, item) in items)
        {
            if (item.Duration.Seconds < profile.MinimumShotDurationSeconds)
                issues.Add(Issue("short-item", TimelineQualitySeverity.Warning, $"Item is only {item.Duration.Seconds:0.###}s and may flash or become unreadable.", item.TimelineStart.Seconds, item.TimelineEnd.Seconds, track, item));
            if (item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.TextContent))
            {
                var cps = item.TextContent.Length / Math.Max(0.1, item.Duration.Seconds);
                if (cps > profile.MaximumCaptionCharactersPerSecond)
                    issues.Add(Issue("caption-reading-speed", TimelineQualitySeverity.Warning, $"Text reads at {cps:0.#} characters/second; profile maximum is {profile.MaximumCaptionCharactersPerSecond:0.#}.", item.TimelineStart.Seconds, item.TimelineEnd.Seconds, track, item));
                if (item.TextContent.Split('\n').Any(line => line.Length > profile.MaximumCaptionCharactersPerLine))
                    issues.Add(Issue("caption-line-length", TimelineQualitySeverity.Warning, $"A text line exceeds {profile.MaximumCaptionCharactersPerLine} characters.", item.TimelineStart.Seconds, item.TimelineEnd.Seconds, track, item));
            }
        }

        foreach (var group in items.Where(pair => pair.item.MediaAssetId.HasValue)
                     .GroupBy(pair => (pair.item.MediaAssetId, Start: Math.Round(pair.item.SourceStart.Seconds, 2), Duration: Math.Round(pair.item.SourceDuration.Seconds, 2))))
        {
            if (group.Count() < 2) continue;
            var first = group.First();
            issues.Add(Issue("repeated-source-range", TimelineQualitySeverity.Warning, $"The same source range is used {group.Count()} times.", first.item.TimelineStart.Seconds, first.item.TimelineEnd.Seconds, first.track, first.item));
        }

        foreach (var track in sequence.Tracks.Where(track => !track.Muted && !track.Hidden))
        {
            var ordered = track.Items.OrderBy(item => item.TimelineStart.Seconds).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var gap = ordered[index].TimelineStart.Seconds - ordered[index - 1].TimelineEnd.Seconds;
                if (gap > profile.MaximumSilenceSeconds && track.Kind is TrackKind.Audio or TrackKind.Video)
                    issues.Add(Issue("timeline-gap", TimelineQualitySeverity.Info, $"Gap of {gap:0.##}s may create dead air or a visual pause.", ordered[index - 1].TimelineEnd.Seconds, ordered[index].TimelineStart.Seconds, track));
            }
        }

        var transitionDensity = duration <= 0 ? 0 : sequence.Transitions.Count / (duration / 60d);
        if (transitionDensity > profile.MaximumTransitionDensityPerMinute)
            issues.Add(Issue("transition-density", TimelineQualitySeverity.Warning, $"Transition density is {transitionDensity:0.#}/minute; profile maximum is {profile.MaximumTransitionDensityPerMinute:0.#}.", 0, duration));

        foreach (var forbidden in brief.ForbiddenMediaAssetIds)
        {
            var usage = items.FirstOrDefault(pair => pair.item.MediaAssetId == forbidden);
            if (usage.item != null)
                issues.Add(Issue("forbidden-media", TimelineQualitySeverity.Error, "Timeline uses media forbidden by the editing brief.", usage.item.TimelineStart.Seconds, usage.item.TimelineEnd.Seconds, usage.track, usage.item));
        }
        foreach (var required in brief.RequiredMediaAssetIds)
        {
            if (items.All(pair => pair.item.MediaAssetId != required))
                issues.Add(Issue("required-media-missing", TimelineQualitySeverity.Warning, $"Required media asset {required} is not used.", 0, duration));
        }
        if (!string.IsNullOrWhiteSpace(brief.CallToAction))
        {
            var hasCta = items.Any(pair => pair.item.Kind == ItemKind.Text && pair.item.TextContent?.Contains(brief.CallToAction, StringComparison.OrdinalIgnoreCase) == true);
            if (!hasCta) issues.Add(Issue("cta-missing", TimelineQualitySeverity.Warning, "The configured call to action was not found in timeline text.", Math.Max(0, duration - 5), duration));
        }
        return issues;
    }

    private static TimelineQualityIssue Issue(string code, TimelineQualitySeverity severity, string message, double start, double end, Track? track = null, TimelineItem? item = null) => new()
    {
        Code = code, Severity = severity, Message = message, TrackId = track?.Id.ToString(), ItemId = item?.Id.ToString(), StartSeconds = Math.Max(0, start), EndSeconds = Math.Max(start, end),
    };
}
