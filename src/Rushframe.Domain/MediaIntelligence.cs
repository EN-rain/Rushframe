namespace Rushframe.Domain;

public sealed class MediaIntelligenceAnalysis
{
    public MediaAssetId MediaAssetId { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string SourceChecksum { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1.0";
    public int AnalysisVersion { get; init; } = 1;
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public MediaIntelligenceTechnicalMetadata Metadata { get; init; } = new();
    public List<MediaIntelligenceScene> Scenes { get; init; } = [];
    public List<MediaIntelligenceTranscriptSegment> Transcript { get; init; } = [];
    public MediaIntelligenceAudioAnalysis Audio { get; init; } = new();
    public List<MediaIntelligenceMoment> Moments { get; init; } = [];
    public List<MediaIntelligenceDuplicateTakeGroup> DuplicateTakeGroups { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class MediaIntelligenceTechnicalMetadata
{
    public MediaTime Duration { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FramesPerSecond { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public int? AudioChannels { get; init; }
    public int? SampleRate { get; init; }
    public long? BitRate { get; init; }
    public string? Orientation { get; init; }
    public bool? VariableFrameRate { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
}

public sealed class MediaIntelligenceQualityScores
{
    public double? VisualQuality { get; init; }
    public double? AudioClarity { get; init; }
    public double? Sharpness { get; init; }
    public double? Exposure { get; init; }
    public double? Stability { get; init; }
    public double? FaceVisibility { get; init; }
    public double? TextReadability { get; init; }
}

public sealed class MediaIntelligenceScene
{
    public string SceneId { get; init; } = string.Empty;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public string? FramePath { get; init; }
    public List<string> FramePaths { get; init; } = [];
    public string? Description { get; init; }
    public string? Summary { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<string> Subjects { get; init; } = [];
    public List<string> Actions { get; init; } = [];
    public List<string> VisibleText { get; init; } = [];
    public string? Location { get; init; }
    public string? ShotType { get; init; }
    public string? CameraMotion { get; init; }
    public string? Mood { get; init; }
    public double? VisualEnergy { get; init; }
    public bool Usable { get; init; } = true;
    public double? Confidence { get; init; }
    public List<string> EditingRoles { get; init; } = [];
    public MediaIntelligenceQualityScores Quality { get; init; } = new();
}

public sealed class MediaIntelligenceWord
{
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public string Text { get; init; } = string.Empty;
    public double? Confidence { get; init; }
}

public sealed class MediaIntelligenceTranscriptSegment
{
    public string SegmentId { get; init; } = string.Empty;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public string Text { get; init; } = string.Empty;
    public List<MediaIntelligenceWord> Words { get; init; } = [];
    public string? Speaker { get; init; }
    public double? Confidence { get; init; }
    public string? Emotion { get; init; }
    public string? Language { get; init; }
    public bool ContainsFiller { get; init; }
    public bool RepeatedTake { get; init; }
    public double? HookScore { get; init; }
    public List<string> RecommendedUse { get; init; } = [];
}

public sealed class MediaIntelligenceMusicAnalysis
{
    public double? TempoBpm { get; init; }
    public List<double> BeatTimes { get; init; } = [];
    public List<double> OnsetTimes { get; init; } = [];
    public List<double> RmsTimes { get; init; } = [];
    public List<double> RmsEnergy { get; init; } = [];
    public string? Key { get; init; }
    public double? Energy { get; init; }
}

public sealed class MediaIntelligenceSilenceRange
{
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public MediaTime Duration { get; init; }
}

public sealed class MediaIntelligenceAudioEvent
{
    public string EventId { get; init; } = string.Empty;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? Label { get; init; }
    public double? Confidence { get; init; }
    public string? Speaker { get; init; }
    public double? Clarity { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = [];
}

public sealed class MediaIntelligenceAudioAnalysis
{
    public double? IntegratedLoudnessLufs { get; init; }
    public double? TruePeakDb { get; init; }
    public double? MeanVolumeDb { get; init; }
    public double? MaxVolumeDb { get; init; }
    public bool ClippingDetected { get; init; }
    public List<MediaIntelligenceSilenceRange> Silence { get; init; } = [];
    public List<MediaIntelligenceAudioEvent> Events { get; init; } = [];
    public MediaIntelligenceMusicAnalysis? Music { get; init; }
}

public sealed class MediaIntelligenceMomentScores
{
    public double Importance { get; init; }
    public double HookPotential { get; init; }
    public double EmotionalIntensity { get; init; }
    public double Novelty { get; init; }
    public double BrollUsefulness { get; init; }
    public double Continuity { get; init; }
    public double BrandRelevance { get; init; }
    public double Overall { get; init; }
}

public sealed class MediaIntelligenceMoment
{
    public string MomentId { get; init; } = string.Empty;
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<string> SceneIds { get; init; } = [];
    public List<string> TranscriptSegmentIds { get; init; } = [];
    public List<string> AudioEventIds { get; init; } = [];
    public string? Visual { get; init; }
    public string? Speech { get; init; }
    public string? Audio { get; init; }
    public List<string> EditingRoles { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    public MediaIntelligenceMomentScores Scores { get; init; } = new();
    public double Confidence { get; init; }
    public List<string> Evidence { get; init; } = [];
    public Dictionary<string, string> Facts { get; init; } = [];
    public Dictionary<string, string> Interpretation { get; init; } = [];
}

public sealed class MediaIntelligenceDuplicateTakeCandidate
{
    public string MomentId { get; init; } = string.Empty;
    public double Score { get; init; }
    public bool Recommended { get; init; }
}

public sealed class MediaIntelligenceDuplicateTakeGroup
{
    public string GroupId { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public List<MediaIntelligenceDuplicateTakeCandidate> Candidates { get; init; } = [];
}
