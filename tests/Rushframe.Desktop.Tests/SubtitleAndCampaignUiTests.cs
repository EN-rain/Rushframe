using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class SubtitleAndCampaignUiTests
{
    [Fact]
    public void subtitle_parser_reads_srt_and_vtt_with_validation()
    {
        var srt = """
        1
        00:00:01,250 --> 00:00:03,500
        <i>Hello &amp; welcome</i>

        2
        00:00:04.000 --> 00:00:05.200
        Second line
        continued

        3
        00:00:07,000 --> 00:00:06,000
        invalid
        """;
        var vtt = """
        WEBVTT

        cue-one
        00:00.500 --> 00:02.000 align:center
        First VTT cue
        """;

        var srtCues = SubtitleParser.Parse(srt);
        var vttCues = SubtitleParser.Parse(vtt);

        Assert.Equal(2, srtCues.Count);
        Assert.Equal(1.25, srtCues[0].Start.Seconds, 3);
        Assert.Equal(3.5, srtCues[0].End.Seconds, 3);
        Assert.Equal("Hello & welcome", srtCues[0].Text);
        Assert.Equal("Second line\ncontinued", srtCues[1].Text);
        Assert.Single(vttCues);
        Assert.Equal(0.5, vttCues[0].Start.Seconds, 3);
        Assert.Equal("First VTT cue", vttCues[0].Text);
    }

    [Fact]
    public async Task subtitle_parser_reads_local_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(path, "1\n00:00:00,000 --> 00:00:01,000\nCaption\n");
        try
        {
            var cues = await SubtitleParser.ParseAsync(path);
            Assert.Equal("Caption", Assert.Single(cues).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void workflow_tab_exposes_campaign_description_and_task_controls()
    {
        var xaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var automation = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Automation.cs"));
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var media = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Media.cs"));

        foreach (var id in new[]
                 {
                     "CampaignDescriptionBox", "SaveCampaignDescriptionButton", "CampaignTaskInput",
                     "AddCampaignTaskButton", "CampaignTaskList", "ToggleCampaignTaskButton", "DeleteCampaignTaskButton",
                 })
            Assert.Contains($"x:Name=\"{id}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateCampaignDescriptionCommand", automation, StringComparison.Ordinal);
        Assert.Contains("AddCampaignTaskCommand", automation, StringComparison.Ordinal);
        Assert.Contains("UpdateCampaignTaskCommand", automation, StringComparison.Ordinal);
        Assert.Contains("DeleteCampaignTaskCommand", automation, StringComparison.Ordinal);
        Assert.Contains("campaignDescription = _project.CampaignDescription", window, StringComparison.Ordinal);
        Assert.Contains("tasks = _project.Tasks.Select", window, StringComparison.Ordinal);
        Assert.Contains("*.srt;*.vtt;*.ttf;*.otf", media, StringComparison.Ordinal);
        Assert.Contains("AddSubtitleAssetToTimelineAsync", window, StringComparison.Ordinal);
        Assert.Contains("new CompositeEditCommand($\"Import {cues.Count} subtitle cues\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void manual_timeline_insertion_paths_do_not_directly_add_tracks()
    {
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var assets = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Assets.cs"));

        Assert.DoesNotContain("seq.Tracks.Add(track);", window, StringComparison.Ordinal);
        Assert.DoesNotContain("sequence.Tracks.Add(track);", assets, StringComparison.Ordinal);
        Assert.Contains("AddProjectMediaAssetCommand", assets, StringComparison.Ordinal);
        Assert.Contains("AddPreparedTrackCommand", assets, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] parts) =>
        Path.Combine(FindRepositoryRoot(), Path.Combine(parts));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
