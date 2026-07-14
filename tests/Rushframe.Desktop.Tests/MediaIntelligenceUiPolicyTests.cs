using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class MediaIntelligenceUiPolicyTests
{
    [Theory]
    [InlineData("base", "base")]
    [InlineData("Small", "small")]
    [InlineData(" MEDIUM ", "medium")]
    [InlineData("large-v3", "base")]
    [InlineData(null, "base")]
    public void speech_model_is_normalized_to_supported_cli_identifier(string? value, string expected)
    {
        Assert.Equal(expected, MediaIntelligenceUiPolicy.NormalizeWhisperModel(value));
    }

    [Fact]
    public void dependent_flags_are_not_emitted_when_parent_analysis_is_disabled()
    {
        var arguments = new List<string>();
        new MediaIntelligenceRunOptions(
            AnalyzeScenes: true,
            TranscribeSpeech: false,
            AnalyzeAudio: false,
            AnalyzeVisuals: false,
            EnableOcr: false,
            AlignWords: true,
            EnableDiarization: true,
            EnableAudioEvents: true,
            BuildEmbeddings: false)
            .AppendArguments(arguments, "gemini");

        Assert.Contains("--no-transcript", arguments);
        Assert.Contains("--no-audio", arguments);
        Assert.DoesNotContain("--alignment", arguments);
        Assert.DoesNotContain("--diarization", arguments);
        Assert.DoesNotContain("--audio-events", arguments);
    }

    [Fact]
    public void enabled_custom_features_emit_valid_cli_arguments()
    {
        var arguments = new List<string>();
        new MediaIntelligenceRunOptions(
            AnalyzeScenes: false,
            TranscribeSpeech: true,
            AnalyzeAudio: true,
            AnalyzeVisuals: true,
            EnableOcr: true,
            AlignWords: true,
            EnableDiarization: true,
            EnableAudioEvents: true,
            BuildEmbeddings: true)
            .AppendArguments(arguments, "qwen");

        Assert.Contains("--no-scenes", arguments);
        Assert.Contains("--visual-provider", arguments);
        Assert.Equal("qwen", arguments[arguments.IndexOf("--visual-provider") + 1]);
        Assert.Contains("--ocr", arguments);
        Assert.Contains("--alignment", arguments);
        Assert.Contains("--diarization", arguments);
        Assert.Contains("--audio-events", arguments);
        Assert.Contains("--embeddings", arguments);
    }

    [Fact]
    public void dependent_controls_are_disabled_during_an_operation()
    {
        Assert.True(MediaIntelligenceUiPolicy.CanUseTranscriptFeature(true, operationRunning: false));
        Assert.False(MediaIntelligenceUiPolicy.CanUseTranscriptFeature(true, operationRunning: true));
        Assert.False(MediaIntelligenceUiPolicy.CanUseAudioFeature(false, operationRunning: false));
        Assert.False(MediaIntelligenceUiPolicy.CanChooseVisualProvider(true, operationRunning: true));
    }
}
