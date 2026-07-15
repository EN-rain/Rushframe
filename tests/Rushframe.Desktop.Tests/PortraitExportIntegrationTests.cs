using System.Diagnostics;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Media.Native;

namespace Rushframe.Desktop.Tests;

public sealed class PortraitExportIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rushframe-portrait-export-{Guid.NewGuid():N}");
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public PortraitExportIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        var repositoryRoot = FindRepositoryRoot();
        _ffmpeg = Path.Combine(repositoryRoot, ".tools", "bin", "ffmpeg.exe");
        _ffprobe = "ffprobe";
        Assert.True(File.Exists(_ffmpeg), $"FFmpeg was not found at {_ffmpeg}");
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task portrait_variant_places_landscape_video_in_vertical_center()
    {
        var source = Path.Combine(_root, "source.mp4");
        await RunAsync(_ffmpeg,
        [
            "-y", "-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=30",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", source,
        ]);
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Width = 320;
        sequence.Height = 180;
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = source,
            RelativeProjectPath = source,
            Duration = MediaTime.FromSeconds(1),
            PixelWidth = 320,
            PixelHeight = 180,
        };
        project.MediaLibrary.Add(asset);
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(1),
            SourceDuration = MediaTime.FromSeconds(1),
            Muted = true,
        };
        item.Transform.PositionY = 70;
        sequence.Tracks.Add(new Track { Kind = TrackKind.Video, Name = "V1", Items = { item } });
        var variant = new ExportVariant
        {
            Name = "Portrait",
            SequenceId = sequence.Id,
            Width = 180,
            Height = 320,
        };
        var (renderProject, renderSequence) = VariantRenderContextService.Create(project, variant);
        var output = Path.Combine(_root, "portrait.mp4");
        var media = new FfmpegMediaService(_ffmpeg, _ffprobe);

        await media.ExportTimelineAsync(renderProject, renderSequence, output, outputWidth: 180, outputHeight: 320);

        var center = await ReadPixelAsync(output, 90, 160);
        var bottom = await ReadPixelAsync(output, 90, 300);
        Assert.True(center.R > 180 && center.G < 90 && center.B < 90, $"Expected red video at portrait center, got {center}.");
        Assert.True(bottom.R < 40 && bottom.G < 40 && bottom.B < 40, $"Expected letterbox below centered video, got {bottom}.");
    }

    private async Task<(byte R, byte G, byte B)> ReadPixelAsync(string input, int x, int y)
    {
        var startInfo = CreateStartInfo(_ffmpeg,
        [
            "-v", "error", "-ss", "0.2", "-i", input,
            "-vf", $"crop=1:1:{x}:{y},format=rgb24",
            "-frames:v", "1", "-f", "rawvideo", "pipe:1",
        ]);
        startInfo.RedirectStandardOutput = true;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg pixel probe did not start.");
        var pixel = new byte[3];
        var read = await process.StandardOutput.BaseStream.ReadAsync(pixel);
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(3, read);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return (pixel[0], pixel[1], pixel[2]);
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        using var process = Process.Start(CreateStartInfo(executable, arguments))
                            ?? throw new InvalidOperationException($"{executable} did not start.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
