using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace Rushframe.Media.Native;

internal static class FfmpegProcessRunner
{
    private static readonly Meter Meter = new("Rushframe.Media", "1.0.0");
    private static readonly Histogram<double> ProcessDuration =
        Meter.CreateHistogram<double>("rushframe.ffmpeg.process.duration_ms", "ms");
    private static readonly Counter<long> ProcessFailures =
        Meter.CreateCounter<long>("rushframe.ffmpeg.process.failure_count");
    private static readonly UpDownCounter<long> ActiveJobs =
        Meter.CreateUpDownCounter<long>("rushframe.ffmpeg.jobs.active");
    private const int DefaultStdOutLimit = 8 * 1024 * 1024;
    private const int DefaultStdErrLimit = 512 * 1024;
    private static readonly SemaphoreSlim JobGate = new(Math.Max(1, Math.Min(3, Environment.ProcessorCount / 2)));

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnFailure = true,
        int stdoutLimit = DefaultStdOutLimit,
        int stderrLimit = DefaultStdErrLimit)
    {
        await using var lease = await AcquireAsync(cancellationToken);
        ActiveJobs.Add(1);
        var started = Stopwatch.GetTimestamp();
        Process? process = null;
        var exitCode = -1;
        try
        {
            var startInfo = CreateStartInfo(fileName, arguments);
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException($"Failed to start {fileName}.");

            var stdoutTask = ReadBoundedAsync(process.StandardOutput, stdoutLimit, preserveTail: false, cancellationToken);
            var stderrTask = ReadBoundedAsync(process.StandardError, stderrLimit, preserveTail: true, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            exitCode = process.ExitCode;
            var result = new ProcessResult(exitCode, stdout, stderr);
            if (exitCode != 0) ProcessFailures.Add(1);
            if (throwOnFailure && exitCode != 0)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} failed with exit code {exitCode}: {stderr}");
            return result;
        }
        catch
        {
            if (exitCode < 0) ProcessFailures.Add(1);
            throw;
        }
        finally
        {
            process?.Dispose();
            ProcessDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("tool", Path.GetFileNameWithoutExtension(fileName)),
                new KeyValuePair<string, object?>("exit_code", exitCode));
            ActiveJobs.Add(-1);
        }
    }

    public static async ValueTask<JobLease> AcquireAsync(CancellationToken cancellationToken)
    {
        await JobGate.WaitAsync(cancellationToken);
        return new JobLease(JobGate);
    }

    public static ProcessStartInfo CreateStartInfo(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    public static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        bool preserveTail,
        CancellationToken cancellationToken)
    {
        maximumCharacters = Math.Max(1024, maximumCharacters);
        var builder = new StringBuilder(Math.Min(maximumCharacters, 64 * 1024));
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (!preserveTail)
            {
                var remaining = maximumCharacters - builder.Length;
                if (remaining > 0) builder.Append(buffer, 0, Math.Min(read, remaining));
                continue;
            }

            builder.Append(buffer, 0, read);
            if (builder.Length > maximumCharacters)
                builder.Remove(0, builder.Length - maximumCharacters);
        }
        return builder.ToString();
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    internal sealed class JobLease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
