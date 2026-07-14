using System.Collections.Concurrent;
using System.IO;
using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

/// <summary>
/// Deterministic, revision-keyed chunk cache for exact FFmpeg timeline preview.
/// </summary>
public sealed class ExactPreviewCache
{
    public const double DefaultChunkDurationSeconds = 6;
    private const int MaxFiles = 160;
    private const long MaxBytes = 1024L * 1024 * 1024;

    private readonly string _directory;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);

    public ExactPreviewCache(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ExactPreviewChunk Describe(
        Project project,
        Sequence sequence,
        double timelineSeconds,
        int width,
        int height,
        double chunkDurationSeconds = DefaultChunkDurationSeconds)
    {
        var totalDuration = Math.Max(sequence.Duration.Seconds, 1 / Math.Max(1, sequence.FrameRate.Value));
        chunkDurationSeconds = Math.Clamp(chunkDurationSeconds, 1, 30);
        var chunkIndex = Math.Max(0, (long)Math.Floor(Math.Max(0, timelineSeconds) / chunkDurationSeconds));
        var start = Math.Min(chunkIndex * chunkDurationSeconds, Math.Max(0, totalDuration - (1 / Math.Max(1, sequence.FrameRate.Value))));
        var duration = Math.Min(chunkDurationSeconds, Math.Max(1 / Math.Max(1, sequence.FrameRate.Value), totalDuration - start));
        var fileName = $"{project.Id.Value:N}-{sequence.Id.Value:N}-r{project.Revision}-c{chunkIndex}-w{width}-h{height}.mp4";
        return new ExactPreviewChunk(
            Path.Combine(_directory, fileName),
            start,
            duration,
            Math.Min(totalDuration, start + duration),
            project.Revision);
    }

    public async Task<string> GetOrCreateAsync(
        ExactPreviewChunk chunk,
        Func<string, CancellationToken, Task> render,
        CancellationToken cancellationToken)
    {
        if (IsValidPreviewFile(chunk.Path))
        {
            Touch(chunk.Path);
            return chunk.Path;
        }
        TryDelete(chunk.Path);

        var lazy = _inflight.GetOrAdd(
            chunk.Path,
            _ => new Lazy<Task<string>>(
                () => RenderChunkAsync(chunk, render, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _inflight.TryRemove(chunk.Path, out _);
        }
    }

    public void Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
    }

    private async Task<string> RenderChunkAsync(
        ExactPreviewChunk chunk,
        Func<string, CancellationToken, Task> render,
        CancellationToken cancellationToken)
    {
        if (IsValidPreviewFile(chunk.Path)) return chunk.Path;
        TryDelete(chunk.Path);
        Directory.CreateDirectory(_directory);
        var temporaryPath = Path.Combine(
            _directory,
            $".{Path.GetFileNameWithoutExtension(chunk.Path)}-{Guid.NewGuid():N}.tmp.mp4");
        try
        {
            await render(temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValidPreviewFile(temporaryPath))
                throw new InvalidOperationException("Exact preview renderer did not produce a valid MP4 chunk.");
            File.Move(temporaryPath, chunk.Path, overwrite: true);
            Touch(chunk.Path);
            _ = CleanupAsync();
            return chunk.Path;
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static bool IsValidPreviewFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 12) return false;
            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Read(header) < header.Length) return false;
            return header[4] == (byte)'f'
                   && header[5] == (byte)'t'
                   && header[6] == (byte)'y'
                   && header[7] == (byte)'p';
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task CleanupAsync()
    {
        if (!await _cleanupGate.WaitAsync(0)) return;
        try
        {
            await Task.Run(() =>
            {
                var files = Directory.EnumerateFiles(_directory, "*.mp4", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => !file.Name.StartsWith(".", StringComparison.Ordinal))
                    .OrderByDescending(file => file.LastAccessTimeUtc)
                    .ToList();
                long retainedBytes = 0;
                for (var index = 0; index < files.Count; index++)
                {
                    retainedBytes += files[index].Length;
                    if (index < MaxFiles && retainedBytes <= MaxBytes) continue;
                    try { files[index].Delete(); } catch { }
                }

                foreach (var temporary in Directory.EnumerateFiles(_directory, ".*.tmp.mp4"))
                {
                    try
                    {
                        var info = new FileInfo(temporary);
                        if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromHours(1)) info.Delete();
                    }
                    catch { }
                }
            });
        }
        finally
        {
            _cleanupGate.Release();
        }
    }
}

public sealed record ExactPreviewChunk(
    string Path,
    double StartSeconds,
    double DurationSeconds,
    double EndSeconds,
    long Revision);
