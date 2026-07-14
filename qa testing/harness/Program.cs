using Rushframe.Infrastructure;
using Rushframe.Media.Native;
using Rushframe.Media.Abstractions;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Rushframe.QaHarness <project.rushframe> <output.mp4> <ffmpeg.exe>");
    return 2;
}

var projectPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var ffmpegPath = Path.GetFullPath(args[2]);

var repository = new ProjectRepository();
var project = repository.Load(projectPath)
    ?? throw new InvalidOperationException($"Unable to load project: {projectPath}");
var sequence = project.MainSequence
    ?? throw new InvalidOperationException("Project has no main sequence.");

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var media = new FfmpegMediaService(ffmpegPath, null);
var progress = new Progress<MediaJobProgress>(update =>
    Console.WriteLine($"PROGRESS|{update.Percent:0.##}|{update.Message}"));

Console.WriteLine($"PROJECT={projectPath}");
Console.WriteLine($"OUTPUT={outputPath}");
Console.WriteLine($"FFMPEG={ffmpegPath}");
Console.WriteLine($"TIMELINE_SECONDS={sequence.Duration.Seconds:0.###}");

await media.ExportTimelineAsync(project, sequence, outputPath, progress);
var info = new FileInfo(outputPath);
Console.WriteLine($"RESULT=PASS|SIZE={info.Length}|PATH={info.FullName}");
return 0;
