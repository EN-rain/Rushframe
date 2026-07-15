namespace Rushframe.Desktop.Services;

public sealed record MediaIntelligenceRunOptions(
    bool AnalyzeScenes,
    bool TranscribeSpeech,
    bool AnalyzeAudio,
    bool AnalyzeVisuals,
    bool EnableOcr,
    bool AlignWords,
    bool EnableDiarization,
    bool EnableAudioEvents,
    bool BuildEmbeddings)
{
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
        if (EnableOcr) arguments.Add("--ocr");
        if (TranscribeSpeech && AlignWords) arguments.Add("--alignment");
        if (TranscribeSpeech && EnableDiarization) arguments.Add("--diarization");
        if (AnalyzeAudio && EnableAudioEvents) arguments.Add("--audio-events");
        if (BuildEmbeddings) arguments.Add("--embeddings");
    }
}

public static class MediaIntelligenceUiPolicy
{
    public static string NormalizeWhisperModel(string? value) =>
        value?.Trim().ToLowerInvariant() is "base" or "small" or "medium" ? value.Trim().ToLowerInvariant() : "base";

    public static bool IsSupportedVisualProvider(string? value) =>
        value?.Trim().ToLowerInvariant() is "groq" or "cloudflare";

    public static string NormalizeVisualProvider(string? value) =>
        IsSupportedVisualProvider(value) ? value!.Trim().ToLowerInvariant() : "groq";

    public static bool CanUseTranscriptFeature(bool transcribeSpeech, bool operationRunning) =>
        transcribeSpeech && !operationRunning;

    public static bool CanUseAudioFeature(bool analyzeAudio, bool operationRunning) =>
        analyzeAudio && !operationRunning;

    public static bool CanChooseVisualProvider(bool analyzeVisuals, bool operationRunning) =>
        analyzeVisuals && !operationRunning;
}
