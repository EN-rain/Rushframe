using Rushframe.Domain;
using Rushframe.Domain.Serialization;

namespace Rushframe.Infrastructure;

public sealed class ProjectRepository
{
    public void Save(Project project, string path) =>
        SaveSerialized(ProjectSerializer.Serialize(project), path);

    public void SaveSerialized(string json, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = CreateTemporaryPath(path);
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task SaveSerializedAsync(
        string json,
        string path,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = CreateTemporaryPath(path);
        try
        {
            await using (var stream = new FileStream(
                             temp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public Project? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return ProjectSerializer.Deserialize(json);
    }

    public bool Exists(string path) => File.Exists(path);

    private static string CreateTemporaryPath(string path) =>
        $"{path}.tmp-{Guid.NewGuid():N}";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
