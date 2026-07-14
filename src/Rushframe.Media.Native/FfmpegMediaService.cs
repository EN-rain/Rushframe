using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;

namespace Rushframe.Media.Native;

public sealed partial class FfmpegMediaService : IMediaProbeService, IMediaDerivativeService, IMediaExportService
{
    private const int MaxProbeCacheEntries = 256;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly ConcurrentDictionary<ProbeCacheKey, Lazy<Task<MediaProbeResult>>> _probeCache = new();
    private readonly ConcurrentQueue<ProbeCacheKey> _probeCacheOrder = new();

    public FfmpegMediaService(string? ffmpegPath = null, string? ffprobePath = null)
    {
        _ffmpegPath = ResolveTool(ffmpegPath, "ffmpeg");
        _ffprobePath = ResolveTool(ffprobePath, "ffprobe");
    }

    public async Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Media file not found.", path);
        var info = new FileInfo(path);
        var key = new ProbeCacheKey(
            Path.GetFullPath(path),
            info.Length,
            info.LastWriteTimeUtc.Ticks);
        var created = false;
        var lazy = _probeCache.GetOrAdd(key, candidate =>
        {
            created = true;
            return new Lazy<Task<MediaProbeResult>>(
                () => ProbeCoreAsync(candidate.Path, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });
        if (created)
        {
            _probeCacheOrder.Enqueue(key);
            TrimProbeCache();
        }

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _probeCache.TryRemove(key, out _);
            throw;
        }
    }

    private async Task<MediaProbeResult> ProbeCoreAsync(string path, CancellationToken cancellationToken)
    {
        FfmpegProcessRunner.ProcessResult output;
        try
        {
            output = await FfmpegProcessRunner.RunAsync(
                _ffprobePath,
                ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
                cancellationToken);
        }
        catch when (_ffprobePath == "ffprobe")
        {
            return await ProbeWithFfmpegAsync(path, cancellationToken);
        }

        using var doc = JsonDocument.Parse(output.StandardOutput);
        var root = doc.RootElement;
        var streams = new List<MediaStreamInfo>();

        if (root.TryGetProperty("streams", out var streamArray))
        {
            foreach (var stream in streamArray.EnumerateArray())
            {
                var kind = ParseStreamKind(GetString(stream, "codec_type"));
                var codec = GetString(stream, "codec_name") ?? "";
                streams.Add(new MediaStreamInfo(
                    kind,
                    codec,
                    GetInt(stream, "width"),
                    GetInt(stream, "height"),
                    ParseFrameRate(GetString(stream, "avg_frame_rate") ?? GetString(stream, "r_frame_rate")),
                    GetInt(stream, "channels"),
                    GetInt(stream, "sample_rate")));
            }
        }

        var format = root.GetProperty("format");
        var duration = TimeSpan.FromSeconds(GetDouble(format, "duration") ?? 0);
        var size = GetLong(format, "size") ?? new FileInfo(path).Length;

        return new MediaProbeResult(path, duration, size, streams);
    }

    private void TrimProbeCache()
    {
        while (_probeCache.Count > MaxProbeCacheEntries && _probeCacheOrder.TryDequeue(out var oldest))
            _probeCache.TryRemove(oldest, out _);
    }

    public async Task GenerateProxyAsync(
        ProxyRequest request,
        IProgress<MediaJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        progress?.Report(new MediaJobProgress(0, "Generating proxy"));
        await RunAsync(
            _ffmpegPath,
            ["-y", "-i", request.SourcePath, "-vf", $"scale=-2:{request.MaxHeight}",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "24",
             "-c:a", "aac", "-b:a", "128k", request.OutputPath],
            cancellationToken);
        progress?.Report(new MediaJobProgress(100, "Proxy complete"));
    }

    public async Task GenerateThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        await RunAsync(
            _ffmpegPath,
            ["-y", "-ss", FormatSeconds(request.Time), "-i", request.SourcePath,
             "-frames:v", "1", "-q:v", "2", request.OutputPath],
            cancellationToken);
    }

    public async Task GenerateWaveformAsync(WaveformRequest request, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        await RunAsync(
            _ffmpegPath,
            ["-y", "-i", request.SourcePath, "-filter_complex",
             $"aformat=channel_layouts=mono,showwavespic=s={request.Width}x{request.Height}:colors=#56B6C2",
             "-frames:v", "1", request.OutputPath],
            cancellationToken);
    }

    public async Task<IReadOnlyList<float>> GenerateWaveformPeaksAsync(
        string sourcePath,
        int peakCount = 2048,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Media file not found.", sourcePath);
        peakCount = Math.Clamp(peakCount, 64, 16_384);

        await using var jobLease = await FfmpegProcessRunner.AcquireAsync(cancellationToken);
        var startInfo = FfmpegProcessRunner.CreateStartInfo(
            _ffmpegPath,
            ["-v", "error", "-i", sourcePath, "-vn", "-ac", "1", "-ar", "8000",
             "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1"]);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start FFmpeg waveform analysis.");
        var errorTask = FfmpegProcessRunner.ReadBoundedAsync(
            process.StandardError,
            256 * 1024,
            preserveTail: true,
            cancellationToken);

        var accumulator = new StreamingPeakAccumulator(peakCount);
        var buffer = new byte[64 * 1024];
        int? pendingLowByte = null;
        try
        {
            while (true)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                var offset = 0;
                if (pendingLowByte.HasValue && read > 0)
                {
                    accumulator.AddSample((short)(pendingLowByte.Value | (buffer[0] << 8)));
                    pendingLowByte = null;
                    offset = 1;
                }
                for (; offset + 1 < read; offset += 2)
                    accumulator.AddSample((short)(buffer[offset] | (buffer[offset + 1] << 8)));
                if (offset < read) pendingLowByte = buffer[offset];
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            FfmpegProcessRunner.TryKill(process);
            throw;
        }

        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg waveform analysis failed: {error}");
        return accumulator.ToPeaks();
    }

    public async Task ExportAsync(
        ExportRequest request,
        IProgress<MediaJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request.SourcePaths.Count == 0) throw new ArgumentException("At least one source is required.", nameof(request));

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        progress?.Report(new MediaJobProgress(0, "Exporting"));

        if (request.SourcePaths.Count == 1)
        {
            await RunAsync(
                _ffmpegPath,
                ["-y", "-i", request.SourcePaths[0], "-vf",
                 $"scale={request.Width}:{request.Height}:force_original_aspect_ratio=decrease,pad={request.Width}:{request.Height}:(ow-iw)/2:(oh-ih)/2",
                 "-r", request.FrameRate.ToString(CultureInfo.InvariantCulture),
                 "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
                 "-c:a", "aac", "-b:a", "192k", request.OutputPath],
                cancellationToken);
        }
        else
        {
            var listPath = Path.Combine(Path.GetTempPath(), $"rushframe-concat-{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllLinesAsync(
                    listPath,
                    request.SourcePaths.Select(p => $"file '{p.Replace("'", "'\\''")}'"),
                    cancellationToken);
                await RunAsync(
                    _ffmpegPath,
                    ["-y", "-f", "concat", "-safe", "0", "-i", listPath, "-vf",
                     $"scale={request.Width}:{request.Height}:force_original_aspect_ratio=decrease,pad={request.Width}:{request.Height}:(ow-iw)/2:(oh-ih)/2",
                     "-r", request.FrameRate.ToString(CultureInfo.InvariantCulture),
                     "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
                     "-c:a", "aac", "-b:a", "192k", request.OutputPath],
                    cancellationToken);
            }
            finally
            {
                if (File.Exists(listPath)) File.Delete(listPath);
            }
        }

        progress?.Report(new MediaJobProgress(100, "Export complete"));
    }

    private async Task ExportTimelineLegacyAsync(
        Project project,
        Sequence sequence,
        string outputPath,
        IProgress<MediaJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int? outputWidth = null,
        int? outputHeight = null)
    {
        var renderWidth = Math.Max(2, outputWidth ?? sequence.Width);
        var renderHeight = Math.Max(2, outputHeight ?? sequence.Height);
        var visualItems = sequence.Tracks
            .Where(t => (t.Kind is TrackKind.Video or TrackKind.Overlay or TrackKind.Text) && !t.Hidden)
            .OrderBy(t => t.Order)
            .SelectMany(t => t.Items.Select(i => new { Track = t, Item = i }))
            .Where(x => x.Item.Kind is ItemKind.Clip or ItemKind.Image or ItemKind.Text)
            .OrderBy(x => x.Track.Order)
            .ThenBy(x => x.Item.TimelineStart.Seconds)
            .ToList();
        var audioItems = sequence.Tracks
            .Where(t => (t.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice) && !t.Hidden && !t.Muted)
            .OrderBy(t => t.Order)
            .SelectMany(t => t.Items.Select(i => new { Track = t, Item = i }))
            .Where(x => x.Item.MediaAssetId.HasValue && !x.Item.Muted)
            .OrderBy(x => x.Item.TimelineStart.Seconds)
            .ToList();

        foreach (var entry in visualItems.Where(x => x.Item.Kind == ItemKind.Clip && !x.Track.Muted && !x.Item.Muted))
        {
            if (!entry.Item.MediaAssetId.HasValue) continue;
            var asset = project.MediaLibrary.FirstOrDefault(a => a.Id == entry.Item.MediaAssetId.Value);
            if (asset?.Kind != MediaKind.Video || !File.Exists(asset.OriginalPath)) continue;

            try
            {
                var probe = await ProbeAsync(asset.OriginalPath, cancellationToken);
                if (probe.Streams.Any(stream => stream.Kind == MediaStreamKind.Audio))
                    audioItems.Add(entry);
            }
            catch
            {
                // A failed optional audio probe must not prevent rendering the visual stream.
            }
        }

        audioItems = audioItems.OrderBy(x => x.Item.TimelineStart.Seconds).ToList();

        if (visualItems.Count == 0)
            throw new ArgumentException("Timeline has no visual items to export.", nameof(sequence));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        progress?.Report(new MediaJobProgress(0, "Building timeline render graph"));

        var args = new List<string> { "-y" };
        var visualInputLabels = new Dictionary<TimelineItemId, string>();
        var audioInputLabels = new List<string>();
        var audioTimelineItems = new List<TimelineItem>();
        var inputIndex = 0;

        foreach (var entry in visualItems)
        {
            if (entry.Item.Kind == ItemKind.Text) continue;
            if (!entry.Item.MediaAssetId.HasValue) continue;
            var asset = project.MediaLibrary.FirstOrDefault(a => a.Id == entry.Item.MediaAssetId.Value);
            if (asset == null || !File.Exists(asset.OriginalPath)) continue;
            if (entry.Item.Kind == ItemKind.Image || asset.Kind == MediaKind.Image)
            {
                args.Add("-loop");
                args.Add("1");
                args.Add("-t");
                args.Add(entry.Item.Duration.Seconds.ToString(CultureInfo.InvariantCulture));
            }
            args.Add("-i");
            args.Add(asset.OriginalPath);
            visualInputLabels[entry.Item.Id] = $"[{inputIndex}:v]";
            inputIndex++;
        }

        foreach (var entry in audioItems)
        {
            var asset = project.MediaLibrary.FirstOrDefault(a => a.Id == entry.Item.MediaAssetId!.Value);
            if (asset == null || !File.Exists(asset.OriginalPath)) continue;
            args.Add("-i");
            args.Add(asset.OriginalPath);
            audioInputLabels.Add($"[{inputIndex}:a]");
            audioTimelineItems.Add(entry.Item);
            inputIndex++;
        }

        if (inputIndex == 0 && visualItems.All(x => x.Item.Kind != ItemKind.Text))
            throw new ArgumentException("Timeline visual items do not reference online source media.", nameof(sequence));

        var filters = new List<string>();
        var baseLabel = "base";
        var duration = Math.Max(sequence.Duration.Seconds, 1);
        filters.Add($"color=c=black:s={sequence.Width}x{sequence.Height}:r={sequence.Fps.ToString(CultureInfo.InvariantCulture)}:d={duration.ToString(CultureInfo.InvariantCulture)}[{baseLabel}]");

        var currentBase = baseLabel;
        var layerNumber = 0;

        foreach (var entry in visualItems)
        {
            var item = entry.Item;
            var layerLabel = $"layer{layerNumber}";

            if (item.Kind == ItemKind.Text)
            {
                var output = $"v{layerNumber}";
                filters.Add($"[{currentBase}]drawtext=text='{EscapeDrawText(item.TextContent ?? "Text")}':x={FormatExpr(item.Transform.PositionX)}:y={FormatExpr(item.Transform.PositionY)}:fontsize={Math.Max(1, item.FontSize).ToString(CultureInfo.InvariantCulture)}:fontcolor={NormalizeColor(item.FillColor, item.Opacity)}:enable='between(t,{item.TimelineStart.Seconds.ToString(CultureInfo.InvariantCulture)},{item.TimelineEnd.Seconds.ToString(CultureInfo.InvariantCulture)})'[{output}]");
                currentBase = output;
                layerNumber++;
                continue;
            }

            if (!visualInputLabels.TryGetValue(item.Id, out var input)) continue;
            var sourceStart = item.SourceStart.Seconds.ToString(CultureInfo.InvariantCulture);
            var effectiveSpeed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
            var sourceDuration = item.Kind == ItemKind.Image
                ? item.Duration.Seconds
                : item.Duration.Seconds * effectiveSpeed;
            var durationSeconds = sourceDuration.ToString(CultureInfo.InvariantCulture);
            var speed = effectiveSpeed.ToString(CultureInfo.InvariantCulture);
            var vf = new List<string>
            {
                $"trim=start={sourceStart}:duration={durationSeconds}",
                "setpts=PTS-STARTPTS",
            };
            if (item.Reversed) vf.Add("reverse");
            if (Math.Abs(effectiveSpeed - 1.0) > 0.0001) vf.Add($"setpts=PTS/{speed}");
            vf.Add($"scale=iw*{item.Transform.ScaleX.ToString(CultureInfo.InvariantCulture)}:ih*{item.Transform.ScaleY.ToString(CultureInfo.InvariantCulture)}");
            if (Math.Abs(item.Transform.RotationDegrees) > 0.0001)
                vf.Add($"rotate={DegreesToRadians(item.Transform.RotationDegrees).ToString(CultureInfo.InvariantCulture)}:ow=rotw(iw):oh=roth(ih):c=none");
            AppendVisualEffects(vf, item);
            if (item.Opacity < 0.999) vf.Add($"format=rgba,colorchannelmixer=aa={item.Opacity.ToString(CultureInfo.InvariantCulture)}");
            vf.Add($"setpts=PTS+{item.TimelineStart.Seconds.ToString(CultureInfo.InvariantCulture)}/TB");
            filters.Add($"{input}{string.Join(",", vf)}[{layerLabel}]");

            var composed = $"v{layerNumber}";
            var blend = BlendModeToFfmpeg(item.BlendMode);
            var offsetX = item.Transform.PositionX.ToString(CultureInfo.InvariantCulture);
            var offsetY = item.Transform.PositionY.ToString(CultureInfo.InvariantCulture);
            var overlayX = $"(main_w-overlay_w)/2+({offsetX})";
            var overlayY = $"(main_h-overlay_h)/2+({offsetY})";
            var enable = $"between(t,{item.TimelineStart.Seconds.ToString(CultureInfo.InvariantCulture)},{item.TimelineEnd.Seconds.ToString(CultureInfo.InvariantCulture)})";
            filters.Add(blend == null
                ? $"[{currentBase}][{layerLabel}]overlay=x={overlayX}:y={overlayY}:enable='{enable}'[{composed}]"
                : $"[{currentBase}][{layerLabel}]blend=all_mode={blend}:enable='{enable}'[{composed}]");
            currentBase = composed;
            layerNumber++;
        }

        var mixedAudioLabel = "";
        if (audioInputLabels.Count > 0)
        {
            var audioLayerLabels = new List<string>();
            for (var i = 0; i < audioInputLabels.Count; i++)
            {
                var item = audioTimelineItems[i];
                var audioLabel = $"a{i}";
                var delayMs = Math.Max(0, (int)Math.Round(item.TimelineStart.Seconds * 1000));
                var af = new List<string>
                {
                    $"atrim=start={item.SourceStart.Seconds.ToString(CultureInfo.InvariantCulture)}:duration={(item.Duration.Seconds * Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100)).ToString(CultureInfo.InvariantCulture)}",
                    "asetpts=PTS-STARTPTS",
                };
                var speed = item.SpeedCurve?.ConstantSpeed ?? item.Speed;
                if (Math.Abs(speed - 1.0) > 0.0001)
                    af.AddRange(BuildAtempoFilters(Math.Clamp(speed, 0.5, 100)));
                if (Math.Abs(item.Volume - 1.0) > 0.0001)
                    af.Add($"volume={item.Volume.ToString(CultureInfo.InvariantCulture)}");
                if (Math.Abs(item.Pan) > 0.0001)
                {
                    var pan = Math.Clamp(item.Pan, -1, 1);
                    var leftGain = pan <= 0 ? 1.0 : 1.0 - pan;
                    var rightGain = pan >= 0 ? 1.0 : 1.0 + pan;
                    af.Add($"pan=stereo|c0={leftGain.ToString(CultureInfo.InvariantCulture)}*c0|c1={rightGain.ToString(CultureInfo.InvariantCulture)}*c1");
                }
                if (item.FadeInDuration.Seconds > 0)
                    af.Add($"afade=t=in:st=0:d={item.FadeInDuration.Seconds.ToString(CultureInfo.InvariantCulture)}");
                if (item.FadeOutDuration.Seconds > 0)
                {
                    var fadeStart = Math.Max(0, item.Duration.Seconds - item.FadeOutDuration.Seconds);
                    af.Add($"afade=t=out:st={fadeStart.ToString(CultureInfo.InvariantCulture)}:d={item.FadeOutDuration.Seconds.ToString(CultureInfo.InvariantCulture)}");
                }
                af.Add($"adelay={delayMs}|{delayMs}");
                filters.Add($"{audioInputLabels[i]}{string.Join(",", af)}[{audioLabel}]");
                audioLayerLabels.Add($"[{audioLabel}]");
            }

            mixedAudioLabel = "aout";
            filters.Add($"{string.Join("", audioLayerLabels)}amix=inputs={audioLayerLabels.Count}:normalize=0:duration=longest[{mixedAudioLabel}]");
        }

        if (renderWidth != sequence.Width || renderHeight != sequence.Height)
        {
            const string outputVideoLabel = "vout";
            filters.Add($"[{currentBase}]scale={renderWidth}:{renderHeight}:force_original_aspect_ratio=decrease,pad={renderWidth}:{renderHeight}:(ow-iw)/2:(oh-ih)/2:color=black[{outputVideoLabel}]");
            currentBase = outputVideoLabel;
        }

        args.Add("-filter_complex");
        args.Add(string.Join(";", filters));
        args.Add("-map");
        args.Add($"[{currentBase}]");
        if (!string.IsNullOrEmpty(mixedAudioLabel))
        {
            args.Add("-map");
            args.Add($"[{mixedAudioLabel}]");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add("192k");
        }
        else
        {
            args.Add("-an");
        }
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-preset");
        args.Add("veryfast");
        args.Add("-crf");
        args.Add("20");
        args.Add(outputPath);

        progress?.Report(new MediaJobProgress(5, "Rendering timeline"));
        await RunAsync(_ffmpegPath, args, cancellationToken);
        progress?.Report(new MediaJobProgress(100, "Timeline export complete"));
    }

    public async Task ExtractAudioAsync(string sourcePath, string outputPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RunAsync(
            _ffmpegPath,
            ["-y", "-i", sourcePath, "-vn", "-acodec", "pcm_s16le", "-ar", "48000", "-ac", "2", outputPath],
            cancellationToken);
    }

    private static string ResolveTool(string? explicitPath, string toolName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath;

        var executableName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".tools", "bin", executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return toolName;
    }

    private static async Task<(string StdOut, string StdErr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await FfmpegProcessRunner.RunAsync(fileName, arguments, cancellationToken);
        return (result.StandardOutput, result.StandardError);
    }

    private async Task<MediaProbeResult> ProbeWithFfmpegAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunAllowFailureAsync(_ffmpegPath, ["-hide_banner", "-i", path], cancellationToken);
        var text = output.StdErr;
        var duration = TimeSpan.Zero;
        var durationMatch = System.Text.RegularExpressions.Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (durationMatch.Success)
        {
            duration = TimeSpan.FromHours(double.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture))
                + TimeSpan.FromMinutes(double.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture))
                + TimeSpan.FromSeconds(double.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        var streams = new List<MediaStreamInfo>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"Stream #.*?: (Video|Audio): ([^,\r\n]+)(.*)"))
        {
            var kind = match.Groups[1].Value == "Video" ? MediaStreamKind.Video : MediaStreamKind.Audio;
            var codec = match.Groups[2].Value.Trim();
            var rest = match.Groups[3].Value;
            int? width = null;
            int? height = null;
            if (kind == MediaStreamKind.Video)
            {
                var size = System.Text.RegularExpressions.Regex.Match(rest, @"(\d{2,5})x(\d{2,5})");
                if (size.Success)
                {
                    width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
            streams.Add(new MediaStreamInfo(kind, codec, width, height));
        }

        return new MediaProbeResult(path, duration, new FileInfo(path).Length, streams);
    }

    private static async Task<(string StdOut, string StdErr, int ExitCode)> RunAllowFailureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await FfmpegProcessRunner.RunAsync(
            fileName,
            arguments,
            cancellationToken,
            throwOnFailure: false);
        return (result.StandardOutput, result.StandardError, result.ExitCode);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string QuoteArgumentIfNeeded(string value)
    {
        if (value.StartsWith("-") || (value.StartsWith("[") && value.EndsWith("]"))) return value;
        if (value.Length == 1) return value;
        if (value.Contains('\\') || value.Contains('/') || value.Contains(' ') || value.Contains(';') || value.Contains(',') || value.Contains(':'))
            return Quote(value);
        return value;
    }

    private static string EscapeDrawText(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace(":", "\\:");

    private static string FormatExpr(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string NormalizeColor(string? color, double opacity)
    {
        var raw = string.IsNullOrWhiteSpace(color) ? "white" : color.Trim();
        return raw.StartsWith('#') ? $"{raw}@{opacity.ToString(CultureInfo.InvariantCulture)}" : raw;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static IEnumerable<string> BuildAtempoFilters(double speed)
    {
        while (speed > 2.0)
        {
            yield return "atempo=2.0";
            speed /= 2.0;
        }
        while (speed < 0.5)
        {
            yield return "atempo=0.5";
            speed /= 0.5;
        }
        yield return $"atempo={speed.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? BlendModeToFfmpeg(BlendMode mode) => mode switch
    {
        BlendMode.Multiply => "multiply",
        BlendMode.Screen => "screen",
        BlendMode.Overlay => "overlay",
        BlendMode.Darken => "darken",
        BlendMode.Lighten => "lighten",
        BlendMode.Difference => "difference",
        BlendMode.Exclusion => "exclusion",
        _ => null,
    };

    private static void AppendVisualEffects(List<string> filters, TimelineItem item)
    {
        if (item.Masks.FirstOrDefault(m => m.Shape == MaskShape.Rectangle) is { } rectMask)
        {
            var width = Math.Clamp(rectMask.ScaleX, 0.01, 1).ToString(CultureInfo.InvariantCulture);
            var height = Math.Clamp(rectMask.ScaleY, 0.01, 1).ToString(CultureInfo.InvariantCulture);
            var x = Math.Max(0, rectMask.PositionX).ToString(CultureInfo.InvariantCulture);
            var y = Math.Max(0, rectMask.PositionY).ToString(CultureInfo.InvariantCulture);
            filters.Add($"crop=iw*{width}:ih*{height}:{x}:{y}");
        }

        if (item.ChromaKey is { } chroma && !string.IsNullOrWhiteSpace(chroma.Color))
        {
            filters.Add($"chromakey={chroma.Color}:similarity={chroma.Similarity.ToString(CultureInfo.InvariantCulture)}:blend={chroma.EdgeSoftness.ToString(CultureInfo.InvariantCulture)}");
        }

        if (item.ColorCorrection is { } color)
        {
            var contrast = Math.Clamp(1 + color.Contrast, 0, 4).ToString(CultureInfo.InvariantCulture);
            var brightness = Math.Clamp(color.Brightness + color.Exposure, -1, 1).ToString(CultureInfo.InvariantCulture);
            var saturation = (color.BlackAndWhite ? 0 : Math.Clamp(color.Saturation, 0, 4)).ToString(CultureInfo.InvariantCulture);
            filters.Add($"eq=contrast={contrast}:brightness={brightness}:saturation={saturation}");
            if (Math.Abs(color.Tint) > 0.0001)
                filters.Add($"hue=h={Math.Clamp(color.Tint, -180, 180).ToString(CultureInfo.InvariantCulture)}");
        }

        if (item.Stabilization?.Enabled == true)
            filters.Add("deshake");

        if (item.FadeInDuration.Seconds > 0)
            filters.Add($"fade=t=in:st=0:d={item.FadeInDuration.Seconds.ToString(CultureInfo.InvariantCulture)}:alpha=1");
        if (item.FadeOutDuration.Seconds > 0)
        {
            var start = Math.Max(0, item.Duration.Seconds - item.FadeOutDuration.Seconds);
            filters.Add($"fade=t=out:st={start.ToString(CultureInfo.InvariantCulture)}:d={item.FadeOutDuration.Seconds.ToString(CultureInfo.InvariantCulture)}:alpha=1");
        }

        foreach (var effect in item.Effects.Where(e => e.Enabled))
        {
            switch (effect.EffectTypeId)
            {
                case "mono":
                    filters.Add("hue=s=0");
                    break;
                case "sepia":
                    filters.Add("colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131");
                    break;
                case "blur":
                    filters.Add($"boxblur={GetParam(effect, "strength", 5).ToString(CultureInfo.InvariantCulture)}:1");
                    break;
                case "brightness":
                    filters.Add($"eq=brightness={GetParam(effect, "amount", 0.1).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "contrast":
                    filters.Add($"eq=contrast={(1 + GetParam(effect, "amount", 0.1)).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "vignette":
                    filters.Add("vignette");
                    break;
                case "film_grain":
                    filters.Add($"noise=alls={Math.Clamp(GetParam(effect, "intensity", 0.2) * 40, 0, 100).ToString(CultureInfo.InvariantCulture)}:allf=t");
                    break;
                case "food_pop":
                    filters.Add($"eq=saturation={GetParam(effect, "saturation", 1.25).ToString(CultureInfo.InvariantCulture)}:brightness={GetParam(effect, "warmth", 0.08).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "night_lift":
                    filters.Add($"eq=brightness={GetParam(effect, "brightness", 0.08).ToString(CultureInfo.InvariantCulture)}:contrast={(1 + GetParam(effect, "contrast", 0.15)).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "nature_green":
                    filters.Add($"eq=saturation={(1 + GetParam(effect, "intensity", 0.35)).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "spark_flash":
                    filters.Add($"eq=brightness={GetParam(effect, "intensity", 0.5).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "love_soft":
                    filters.Add($"gblur=sigma={Math.Clamp(GetParam(effect, "softness", 0.25) * 4, 0, 10).ToString(CultureInfo.InvariantCulture)}");
                    break;
                case "lens_punch":
                    filters.Add("vignette");
                    break;
                case "body_smooth_motion":
                    filters.Add("minterpolate=fps=60:mi_mode=mci");
                    break;
                case "stabilize":
                    filters.Add("deshake");
                    break;
            }
        }
    }

    private static double GetParam(EffectInstance effect, string name, double fallback)
    {
        if (!effect.Parameters.TryGetValue(name, out var value)) return fallback;
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var d) => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => fallback,
        };
    }
    private static string FormatSeconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static MediaStreamKind ParseStreamKind(string? value) => value switch
    {
        "video" => MediaStreamKind.Video,
        "audio" => MediaStreamKind.Audio,
        "subtitle" => MediaStreamKind.Subtitle,
        _ => MediaStreamKind.Other,
    };

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0") return null;
        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
            return num / den;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private readonly record struct ProbeCacheKey(string Path, long Length, long LastWriteTicks);

    private sealed class StreamingPeakAccumulator
    {
        private readonly float[] _buckets;
        private long _bucketSize = 1;
        private long _sampleCount;

        public StreamingPeakAccumulator(int peakCount)
        {
            _buckets = new float[peakCount];
        }

        public void AddSample(short sample)
        {
            var bucketIndex = _sampleCount / _bucketSize;
            if (bucketIndex >= _buckets.Length)
            {
                CollapseBuckets();
                bucketIndex = _sampleCount / _bucketSize;
            }

            var normalized = Math.Clamp(Math.Abs((int)sample) / 32768f, 0, 1);
            var index = (int)Math.Min(_buckets.Length - 1, bucketIndex);
            if (normalized > _buckets[index]) _buckets[index] = normalized;
            _sampleCount++;
        }

        public IReadOnlyList<float> ToPeaks()
        {
            if (_sampleCount == 0) return new float[_buckets.Length];
            var used = Math.Clamp((int)Math.Ceiling(_sampleCount / (double)_bucketSize), 1, _buckets.Length);
            if (used == _buckets.Length) return _buckets;

            var result = new float[_buckets.Length];
            for (var index = 0; index < result.Length; index++)
            {
                var source = Math.Min(used - 1, index * used / result.Length);
                result[index] = _buckets[source];
            }
            return result;
        }

        private void CollapseBuckets()
        {
            var target = 0;
            for (var source = 0; source < _buckets.Length; source += 2)
            {
                var right = Math.Min(source + 1, _buckets.Length - 1);
                _buckets[target++] = Math.Max(_buckets[source], _buckets[right]);
            }
            Array.Clear(_buckets, target, _buckets.Length - target);
            _bucketSize = checked(_bucketSize * 2);
        }
    }
}
