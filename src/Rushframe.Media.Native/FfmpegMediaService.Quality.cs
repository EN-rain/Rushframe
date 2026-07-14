using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rushframe.Media.Abstractions;

namespace Rushframe.Media.Native;

public sealed partial class FfmpegMediaService
{
    private static readonly Regex BlackRangeRegex = new(
        @"black_start:(?<start>-?[0-9.]+)\s+black_end:(?<end>-?[0-9.]+)\s+black_duration:(?<duration>[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FreezeStartRegex = new(
        @"freeze_start:\s*(?<start>-?[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FreezeEndRegex = new(
        @"freeze_end:\s*(?<end>-?[0-9.]+)\s*\|\s*freeze_duration:\s*(?<duration>[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SilenceStartRegex = new(
        @"silence_start:\s*(?<start>-?[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SilenceEndRegex = new(
        @"silence_end:\s*(?<end>-?[0-9.]+)\s*\|\s*silence_duration:\s*(?<duration>[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<MediaRuntimeVersionInfo> GetRuntimeVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        var ffmpeg = await FfmpegProcessRunner.RunAsync(_ffmpegPath, ["-version"], cancellationToken, throwOnFailure: false, stdoutLimit: 32 * 1024);
        var ffprobe = await FfmpegProcessRunner.RunAsync(_ffprobePath, ["-version"], cancellationToken, throwOnFailure: false, stdoutLimit: 32 * 1024);
        return new MediaRuntimeVersionInfo(
            FirstLine(ffmpeg.StandardOutput),
            FirstLine(ffprobe.StandardOutput));
    }

    public async Task<MediaExportVerificationReport> VerifyExportAsync(
        string outputPath,
        int? expectedWidth = null,
        int? expectedHeight = null,
        double? expectedDurationSeconds = null,
        string? evidenceDirectory = null,
        IReadOnlyCollection<double>? evidenceTimestamps = null,
        CancellationToken cancellationToken = default)
    {
        var report = new MediaExportVerificationReport
        {
            OutputPath = Path.GetFullPath(outputPath),
            StartedUtc = DateTimeOffset.UtcNow,
        };
        if (!File.Exists(outputPath))
        {
            report.Errors.Add("Export file does not exist.");
            report.Status = MediaExportVerificationStatus.Failed;
            report.CompletedUtc = DateTimeOffset.UtcNow;
            return report;
        }
        report.FileSizeBytes = new FileInfo(outputPath).Length;
        if (report.FileSizeBytes == 0)
            report.Errors.Add("Export file is empty.");

        MediaProbeResult probe;
        try
        {
            probe = await ProbeAsync(outputPath, cancellationToken);
            report.DurationSeconds = probe.Duration.TotalSeconds;
            var video = probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
            var audio = probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Audio);
            report.HasVideo = video != null;
            report.HasAudio = audio != null;
            report.Width = video?.Width;
            report.Height = video?.Height;
            report.FramesPerSecond = video?.FrameRate;
            report.VideoCodec = video?.Codec;
            report.AudioCodec = audio?.Codec;
            if (video == null) report.Errors.Add("Export contains no video stream.");
            if (expectedWidth.HasValue && video?.Width != expectedWidth)
                report.Errors.Add($"Expected width {expectedWidth}, found {video?.Width?.ToString() ?? "none"}.");
            if (expectedHeight.HasValue && video?.Height != expectedHeight)
                report.Errors.Add($"Expected height {expectedHeight}, found {video?.Height?.ToString() ?? "none"}.");
            if (expectedDurationSeconds.HasValue)
            {
                var tolerance = Math.Max(0.25, 2.0 / Math.Max(1, video?.FrameRate ?? 30));
                if (Math.Abs(probe.Duration.TotalSeconds - expectedDurationSeconds.Value) > tolerance)
                    report.Errors.Add($"Expected duration {expectedDurationSeconds:0.###}s, found {probe.Duration.TotalSeconds:0.###}s.");
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add($"FFprobe failed: {ex.Message}");
            report.Status = MediaExportVerificationStatus.Failed;
            report.CompletedUtc = DateTimeOffset.UtcNow;
            return report;
        }

        var decode = await FfmpegProcessRunner.RunAsync(
            _ffmpegPath,
            ["-v", "error", "-xerror", "-i", outputPath, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"],
            cancellationToken,
            throwOnFailure: false);
        report.FullDecodePassed = decode.ExitCode == 0 && string.IsNullOrWhiteSpace(decode.StandardError);
        if (!report.FullDecodePassed)
            report.Errors.Add($"Full decode failed: {Tail(decode.StandardError, 1200)}");

        if (report.HasVideo)
        {
            var black = await FfmpegProcessRunner.RunAsync(
                _ffmpegPath,
                ["-hide_banner", "-nostats", "-i", outputPath, "-an", "-vf", "blackdetect=d=0.25:pix_th=0.10", "-f", "null", "-"],
                cancellationToken,
                throwOnFailure: false,
                stderrLimit: 2 * 1024 * 1024);
            report.BlackIntervals.AddRange(ParseBlackIntervals(black.StandardError));

            var freeze = await FfmpegProcessRunner.RunAsync(
                _ffmpegPath,
                ["-hide_banner", "-nostats", "-i", outputPath, "-an", "-vf", "freezedetect=n=-50dB:d=1", "-f", "null", "-"],
                cancellationToken,
                throwOnFailure: false,
                stderrLimit: 2 * 1024 * 1024);
            report.FreezeIntervals.AddRange(ParseIntervals(freeze.StandardError, FreezeStartRegex, FreezeEndRegex));
            ClassifyTemporalFindings(report);
        }

        if (report.HasAudio)
        {
            var loudness = await FfmpegProcessRunner.RunAsync(
                _ffmpegPath,
                ["-hide_banner", "-nostats", "-i", outputPath, "-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"],
                cancellationToken,
                throwOnFailure: false,
                stderrLimit: 2 * 1024 * 1024);
            ReadLoudness(loudness.StandardError, report);

            var silence = await FfmpegProcessRunner.RunAsync(
                _ffmpegPath,
                ["-hide_banner", "-nostats", "-i", outputPath, "-vn", "-af", "silencedetect=noise=-50dB:d=2", "-f", "null", "-"],
                cancellationToken,
                throwOnFailure: false,
                stderrLimit: 2 * 1024 * 1024);
            report.SilenceIntervals.AddRange(ParseIntervals(silence.StandardError, SilenceStartRegex, SilenceEndRegex));
            ClassifyAudioFindings(report);
        }
        else
        {
            report.Warnings.Add("Export contains no audio stream.");
        }

        if (!string.IsNullOrWhiteSpace(evidenceDirectory) && report.HasVideo)
            await CaptureEvidenceFramesAsync(
                outputPath,
                report.DurationSeconds,
                evidenceDirectory,
                report.EvidenceFrames,
                evidenceTimestamps,
                cancellationToken);

        report.Status = report.Errors.Count > 0
            ? MediaExportVerificationStatus.Failed
            : report.Warnings.Count > 0
                ? MediaExportVerificationStatus.PassedWithWarnings
                : MediaExportVerificationStatus.Passed;
        report.CompletedUtc = DateTimeOffset.UtcNow;
        return report;
    }

    private async Task CaptureEvidenceFramesAsync(
        string source,
        double durationSeconds,
        string evidenceDirectory,
        ICollection<string> outputPaths,
        IReadOnlyCollection<double>? requestedTimestamps,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(evidenceDirectory);
        var times = new[]
        {
            0.05,
            Math.Max(0.05, durationSeconds * 0.25),
            Math.Max(0.05, durationSeconds * 0.50),
            Math.Max(0.05, durationSeconds * 0.75),
            Math.Max(0.05, durationSeconds - 0.05),
        }
            .Concat(requestedTimestamps ?? [])
            .Select(value => Math.Clamp(value, 0.01, Math.Max(0.01, durationSeconds - 0.01)))
            .DistinctBy(value => Math.Round(value, 3))
            .OrderBy(value => value)
            .Take(100)
            .ToArray();
        for (var index = 0; index < times.Length; index++)
        {
            var path = Path.Combine(evidenceDirectory, $"frame-{index + 1:00}-{times[index]:0.000}s.jpg");
            var result = await FfmpegProcessRunner.RunAsync(
                _ffmpegPath,
                ["-y", "-ss", times[index].ToString("0.###", CultureInfo.InvariantCulture), "-i", source, "-frames:v", "1", "-q:v", "2", path],
                cancellationToken,
                throwOnFailure: false);
            if (result.ExitCode == 0 && File.Exists(path)) outputPaths.Add(path);
        }
    }

    private static IEnumerable<MediaVerificationInterval> ParseBlackIntervals(string text)
    {
        foreach (Match match in BlackRangeRegex.Matches(text))
        {
            if (TryReadInterval(match, out var interval)) yield return interval;
        }
    }

    private static IEnumerable<MediaVerificationInterval> ParseIntervals(string text, Regex startRegex, Regex endRegex)
    {
        var starts = startRegex.Matches(text).Select(match => ParseDouble(match.Groups["start"].Value)).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var ends = endRegex.Matches(text).ToArray();
        for (var index = 0; index < Math.Min(starts.Length, ends.Length); index++)
        {
            var end = ParseDouble(ends[index].Groups["end"].Value);
            var duration = ParseDouble(ends[index].Groups["duration"].Value);
            if (end.HasValue && duration.HasValue)
                yield return new MediaVerificationInterval(starts[index], end.Value, duration.Value);
        }
    }

    private static bool TryReadInterval(Match match, out MediaVerificationInterval interval)
    {
        var start = ParseDouble(match.Groups["start"].Value);
        var end = ParseDouble(match.Groups["end"].Value);
        var duration = ParseDouble(match.Groups["duration"].Value);
        if (start.HasValue && end.HasValue && duration.HasValue)
        {
            interval = new MediaVerificationInterval(start.Value, end.Value, duration.Value);
            return true;
        }
        interval = default!;
        return false;
    }

    private static void ReadLoudness(string stderr, MediaExportVerificationReport report)
    {
        var start = stderr.LastIndexOf('{');
        var end = stderr.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            report.Warnings.Add("Audio loudness analysis did not return measurements.");
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(stderr[start..(end + 1)]);
            var root = document.RootElement;
            report.IntegratedLoudnessLufs = ReadJsonDouble(root, "input_i");
            report.TruePeakDb = ReadJsonDouble(root, "input_tp");
            report.LoudnessRangeLufs = ReadJsonDouble(root, "input_lra");
        }
        catch (JsonException)
        {
            report.Warnings.Add("Audio loudness measurements were malformed.");
        }
    }

    private static void ClassifyTemporalFindings(MediaExportVerificationReport report)
    {
        var blackDuration = report.BlackIntervals.Sum(interval => interval.DurationSeconds);
        if (blackDuration > Math.Max(2, report.DurationSeconds * 0.20))
            report.Errors.Add($"Black frames cover {blackDuration:0.##}s of the export.");
        else if (report.BlackIntervals.Count > 0)
            report.Warnings.Add($"Detected {report.BlackIntervals.Count} black interval(s), totaling {blackDuration:0.##}s.");

        var freezeDuration = report.FreezeIntervals.Sum(interval => interval.DurationSeconds);
        if (freezeDuration > Math.Max(3, report.DurationSeconds * 0.25))
            report.Errors.Add($"Frozen frames cover {freezeDuration:0.##}s of the export.");
        else if (report.FreezeIntervals.Count > 0)
            report.Warnings.Add($"Detected {report.FreezeIntervals.Count} frozen interval(s), totaling {freezeDuration:0.##}s.");
    }

    private static void ClassifyAudioFindings(MediaExportVerificationReport report)
    {
        if (report.TruePeakDb is > -0.1)
            report.Warnings.Add($"Audio true peak is {report.TruePeakDb:0.##} dBTP and may clip after platform transcoding.");
        if (report.IntegratedLoudnessLufs is > -9 or < -30)
            report.Warnings.Add($"Integrated loudness is {report.IntegratedLoudnessLufs:0.##} LUFS; verify the intended delivery target.");
        var silenceDuration = report.SilenceIntervals.Sum(interval => interval.DurationSeconds);
        if (silenceDuration > Math.Max(5, report.DurationSeconds * 0.50))
            report.Warnings.Add($"Long silence covers {silenceDuration:0.##}s of the export.");
    }

    private static double? ReadJsonDouble(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : ParseDouble(value.ToString());
    }

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string FirstLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "unknown";

    private static string Tail(string value, int maximumCharacters) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maximumCharacters ? value.Trim() : value[^maximumCharacters..].Trim();
}

public sealed record MediaRuntimeVersionInfo(string Ffmpeg, string Ffprobe);

public enum MediaExportVerificationStatus
{
    Pending,
    Passed,
    PassedWithWarnings,
    Failed,
}

public sealed record MediaVerificationInterval(double StartSeconds, double EndSeconds, double DurationSeconds);

public sealed class MediaExportVerificationReport
{
    public string OutputPath { get; init; } = string.Empty;
    public MediaExportVerificationStatus Status { get; set; } = MediaExportVerificationStatus.Pending;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public long FileSizeBytes { get; set; }
    public bool FullDecodePassed { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FramesPerSecond { get; set; }
    public double DurationSeconds { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public double? IntegratedLoudnessLufs { get; set; }
    public double? TruePeakDb { get; set; }
    public double? LoudnessRangeLufs { get; set; }
    public List<MediaVerificationInterval> BlackIntervals { get; init; } = [];
    public List<MediaVerificationInterval> FreezeIntervals { get; init; } = [];
    public List<MediaVerificationInterval> SilenceIntervals { get; init; } = [];
    public List<string> EvidenceFrames { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}
