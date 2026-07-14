using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class MediaIntelligenceRunProfileTests
{
    [Fact]
    public void video_parser_only_enables_video_structure_analysis()
    {
        var arguments = BuildArguments(MediaIntelligenceRunProfile.VideoParser);

        Assert.Contains("--no-transcript", arguments);
        Assert.Contains("--no-audio", arguments);
        Assert.DoesNotContain("--no-scenes", arguments);
        Assert.DoesNotContain("--visual-provider", arguments);
        Assert.DoesNotContain("--alignment", arguments);
    }

    [Fact]
    public void visual_analysis_uses_selected_provider()
    {
        var arguments = BuildArguments(MediaIntelligenceRunProfile.VisualAnalysis, "qwen");

        Assert.Contains("--no-transcript", arguments);
        Assert.Contains("--no-audio", arguments);
        Assert.Equal("qwen", arguments[arguments.IndexOf("--visual-provider") + 1]);
    }

    [Fact]
    public void precise_words_enables_transcription_and_alignment()
    {
        var arguments = BuildArguments(MediaIntelligenceRunProfile.PreciseWords);

        Assert.Contains("--no-scenes", arguments);
        Assert.Contains("--no-audio", arguments);
        Assert.Contains("--alignment", arguments);
        Assert.DoesNotContain("--no-transcript", arguments);
    }

    [Fact]
    public void audio_to_text_enables_transcription_without_alignment()
    {
        var arguments = BuildArguments(MediaIntelligenceRunProfile.AudioToText);

        Assert.Contains("--no-scenes", arguments);
        Assert.Contains("--no-audio", arguments);
        Assert.DoesNotContain("--no-transcript", arguments);
        Assert.DoesNotContain("--alignment", arguments);
    }

    [Fact]
    public void visual_analysis_rejects_unknown_provider()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MediaIntelligenceRunProfile.VisualAnalysis.AppendArguments([], "unknown"));
    }

    private static List<string> BuildArguments(
        MediaIntelligenceRunProfile profile,
        string visualProvider = "gemini")
    {
        var arguments = new List<string>();
        profile.AppendArguments(arguments, visualProvider);
        return arguments;
    }
}
