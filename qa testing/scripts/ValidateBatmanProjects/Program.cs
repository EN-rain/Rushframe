using Rushframe.Domain;
using Rushframe.Infrastructure;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Rushframe.ValidateBatmanProjects <edits-folder>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
    throw new DirectoryNotFoundException(root);

var repository = new ProjectRepository();
var projects = Directory.GetFiles(root, "*.rushframe", SearchOption.AllDirectories)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (projects.Length != 1)
    throw new InvalidDataException($"Expected exactly one replacement Batman project, found {projects.Length}.");

var path = projects[0];
var project = repository.Load(path)
              ?? throw new InvalidDataException($"Project load returned null: {path}");
var sequence = project.MainSequence
               ?? throw new InvalidDataException($"Project has no main sequence: {path}");
var analysis = project.MediaIntelligence.SingleOrDefault()
               ?? throw new InvalidDataException($"Project does not contain exactly one AI analysis: {path}");
if (project.Name != "Batman - The Last Hand")
    throw new InvalidDataException($"Unexpected replacement project name: {project.Name}");
if (analysis.Transcript.Count < 8 || analysis.Scenes.Count == 0)
    throw new InvalidDataException(
        $"AI data is incomplete: scenes={analysis.Scenes.Count}, transcript={analysis.Transcript.Count}.");
if (project.Tasks.Count == 0 || project.Tasks.Any(task => !task.IsCompleted))
    throw new InvalidDataException("Project tasks are incomplete.");
if (sequence.Width != 720 || sequence.Height != 1280)
    throw new InvalidDataException($"Unexpected sequence dimensions {sequence.Width}x{sequence.Height}.");
if (sequence.Tracks.Count != 4)
    throw new InvalidDataException($"Expected four creative tracks, found {sequence.Tracks.Count}.");
if (sequence.Markers.Count != 8)
    throw new InvalidDataException($"Expected eight AI beat markers, found {sequence.Markers.Count}.");
if (sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Duration.Seconds <= 0))
    throw new InvalidDataException("Project contains a non-positive item duration.");
var sourceClips = sequence.Tracks
    .SelectMany(track => track.Items)
    .Where(item => item.Kind == ItemKind.Clip && item.MediaAssetId.HasValue)
    .ToArray();
if (sourceClips.Length == 0 || sourceClips.Any(item => Math.Abs(item.Transform.PositionY) > 0.001))
    throw new InvalidDataException("All Batman source clips must remain vertically centered at PositionY=0.");
if (sequence.Duration.Seconds is < 10 or > 20)
    throw new InvalidDataException($"Unexpected sequence duration {sequence.Duration.Seconds:0.###}.");

Console.WriteLine(
    $"PROJECT_PASS|{Path.GetFileName(path)}|revision={project.Revision}|tracks={sequence.Tracks.Count}|items={sequence.Tracks.Sum(track => track.Items.Count)}|markers={sequence.Markers.Count}|transitions={sequence.Transitions.Count}|scenes={analysis.Scenes.Count}|transcript={analysis.Transcript.Count}|audioEvents={analysis.Audio.Events.Count}|tasks={project.Tasks.Count}");
Console.WriteLine("RESULT=PASS|PROJECTS=1|EDIT=Batman: The Last Hand");
return 0;
