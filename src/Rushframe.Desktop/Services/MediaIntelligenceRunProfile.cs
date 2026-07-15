namespace Rushframe.Desktop.Services;

public sealed record MediaIntelligenceRunProfile(
    string Name,
    string Description,
    bool AnalyzeScenes,
    bool TranscribeSpeech,
    bool AnalyzeAudio,
    bool AnalyzeVisuals,
    bool AlignWords)
{
    public static MediaIntelligenceRunProfile VideoParser { get; } = new(
        "Video parser",
        "Parse metadata, scenes, sampled frames, and visual quality.",
        AnalyzeScenes: true,
        TranscribeSpeech: false,
        AnalyzeAudio: false,
        AnalyzeVisuals: false,
        AlignWords: false);

    public static MediaIntelligenceRunProfile VisualAnalysis { get; } = new(
        "Visual analysis",
        "Describe scene content using the selected visual provider.",
        AnalyzeScenes: true,
        TranscribeSpeech: false,
        AnalyzeAudio: false,
        AnalyzeVisuals: true,
        AlignWords: false);

    public static MediaIntelligenceRunProfile PreciseWords { get; } = new(
        "Precise word timing",
        "Transcribe speech and refine timestamps down to individual words.",
        AnalyzeScenes: false,
        TranscribeSpeech: true,
        AnalyzeAudio: false,
        AnalyzeVisuals: false,
        AlignWords: true);

    public static MediaIntelligenceRunProfile AudioToText { get; } = new(
        "Audio to text",
        "Transcribe spoken audio into timestamped text.",
        AnalyzeScenes: false,
        TranscribeSpeech: true,
        AnalyzeAudio: false,
        AnalyzeVisuals: false,
        AlignWords: false);

    public void AppendArguments(ICollection<string> arguments, string visualProvider)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!AnalyzeScenes) arguments.Add("--no-scenes");
        if (!TranscribeSpeech) arguments.Add("--no-transcript");
        if (!AnalyzeAudio) arguments.Add("--no-audio");
        if (AnalyzeVisuals)
        {
            if (!MediaIntelligenceUiPolicy.IsSupportedVisualProvider(visualProvider))
                throw new ArgumentOutOfRangeException(nameof(visualProvider), visualProvider, "Unsupported visual provider.");
            arguments.Add("--visual-provider");
            arguments.Add(MediaIntelligenceUiPolicy.NormalizeVisualProvider(visualProvider));
        }
        if (AlignWords) arguments.Add("--alignment");
    }
}
