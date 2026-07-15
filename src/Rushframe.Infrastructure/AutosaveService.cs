using Rushframe.Domain;
using Rushframe.Domain.Serialization;

namespace Rushframe.Infrastructure;

public sealed class AutosaveService
{
    private readonly string _autosaveDir;
    private const long MaxTotalBytes = 256L * 1024 * 1024;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public AutosaveService(string autosaveDir)
    {
        _autosaveDir = autosaveDir;
        Directory.CreateDirectory(autosaveDir);
    }

    public string Save(Project project) =>
        SaveSerialized(project.Id, ProjectSerializer.Serialize(project));

    public string SaveSerialized(ProjectId projectId, string json)
    {
        var temp = CreateTemporaryPath(projectId);
        var final = Path.Combine(_autosaveDir, $"{projectId}.autosave");
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, final, overwrite: true);
            EnforceRetention();
            return final;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task<string> SaveSerializedAsync(
        ProjectId projectId,
        string json,
        CancellationToken cancellationToken = default)
    {
        var temp = CreateTemporaryPath(projectId);
        var final = Path.Combine(_autosaveDir, $"{projectId}.autosave");
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

            File.Move(temp, final, overwrite: true);
            EnforceRetention();
            return final;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public Project? LoadMostRecent()
    {
        var latest = GetMostRecentPath();
        return latest != null ? ProjectSerializer.Deserialize(File.ReadAllText(latest)) : null;
    }

    public async Task<Project?> LoadMostRecentAsync(CancellationToken cancellationToken = default)
    {
        var latest = GetMostRecentPath();
        if (latest == null) return null;
        var json = await File.ReadAllTextAsync(latest, cancellationToken);
        return await Task.Run(() => ProjectSerializer.Deserialize(json), cancellationToken);
    }

    private string? GetMostRecentPath() =>
        Directory.GetFiles(_autosaveDir, "*.autosave")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    public void StartBackground(Project project, TimeSpan interval, Action<string>? saved = null, Action<Exception>? failed = null)
    {
        StopBackground();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _backgroundTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        saved?.Invoke(Save(project));
                    }
                    catch (Exception ex)
                    {
                        failed?.Invoke(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }, token);
    }

    public void StopBackground()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
        _backgroundTask = null;
    }

    public async Task StopBackgroundAsync()
    {
        var cts = _cts;
        var task = _backgroundTask;
        _cts = null;
        _backgroundTask = null;
        cts?.Cancel();
        try
        {
            if (task != null) await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private string CreateTemporaryPath(ProjectId projectId) =>
        Path.Combine(_autosaveDir, $".{projectId}.{Guid.NewGuid():N}.tmp");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private void EnforceRetention()
    {
        var files = Directory.GetFiles(_autosaveDir, "*.autosave")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        long retainedBytes = 0;
        foreach (var file in files)
        {
            retainedBytes += file.Length;
            if (retainedBytes <= MaxTotalBytes) continue;
            try { file.Delete(); } catch { }
        }
    }
}
