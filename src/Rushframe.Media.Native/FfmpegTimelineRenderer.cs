using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;

namespace Rushframe.Media.Native;

public sealed partial class FfmpegMediaService
{
    public static IReadOnlySet<string> SupportedEffectTypeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mono", "sepia", "blur", "brightness", "contrast", "vignette",
        "noise_reduction", "motion_blur", "glitch", "film_grain", "food_pop",
        "night_lift", "nature_green", "spark_flash", "love_soft", "lens_punch",
        "body_smooth_motion", "stabilize",
    };

    public static IReadOnlySet<MaskShape> SupportedMaskShapes { get; } = new HashSet<MaskShape>
    {
        MaskShape.Rectangle, MaskShape.Ellipse, MaskShape.Linear, MaskShape.Mirror,
        MaskShape.Star, MaskShape.Polygon, MaskShape.Split, MaskShape.Diamond,
        MaskShape.Heart, MaskShape.Text, MaskShape.Custom,
    };

    public async Task ExportTimelineAsync(
        Project project,
        Sequence sequence,
        string outputPath,
        IProgress<MediaJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int? outputWidth = null,
        int? outputHeight = null,
        TimelineExportOptions? exportOptions = null,
        double? rangeStartSeconds = null,
        double? rangeDurationSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        ValidateRenderCapabilities(sequence);

        var renderWidth = EnsureEven(Math.Max(2, outputWidth ?? sequence.Width));
        var renderHeight = EnsureEven(Math.Max(2, outputHeight ?? sequence.Height));
        var timelineDuration = Math.Max(sequence.Duration.Seconds, 1 / Math.Max(sequence.FrameRate.Value, 1));
        var renderStart = Math.Clamp(rangeStartSeconds ?? 0, 0, timelineDuration);
        var renderDuration = Math.Clamp(
            rangeDurationSeconds ?? (timelineDuration - renderStart),
            1 / Math.Max(sequence.FrameRate.Value, 1),
            Math.Max(1 / Math.Max(sequence.FrameRate.Value, 1), timelineDuration - renderStart));
        var visualEntries = BuildVisualEntries(project, sequence);
        var transitionWindows = BuildTransitionWindows(sequence);
        var options = exportOptions ?? InferExportOptions(outputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        progress?.Report(new MediaJobProgress(0, "Building timeline render graph"));

        var temporaryTextFiles = new List<string>();
        try
        {
        var arguments = new List<string> { "-y" };
        var visualInputs = new Dictionary<TimelineItemId, VisualInput>();
        var inputIndex = 0;

        foreach (var entry in visualEntries)
        {
            if (entry.Item.Kind is ItemKind.Text or ItemKind.AdjustmentLayer)
                continue;

            var window = ResolveVisualWindow(entry, transitionWindows);
            if (entry.SourcePath == null)
            {
                if (!IsBuiltInSticker(entry.Item)) continue;
                arguments.Add("-f");
                arguments.Add("lavfi");
                arguments.Add("-i");
                arguments.Add($"color=c=black@0.0:s=512x512:r={sequence.FrameRate}:d={FormatNumber(window.RenderDuration)}");
                visualInputs[entry.Item.Id] = new VisualInput($"[{inputIndex}:v]", entry, window);
                inputIndex++;
                continue;
            }

            if (entry.IsImage)
            {
                arguments.Add("-loop");
                arguments.Add("1");
                arguments.Add("-framerate");
                arguments.Add(sequence.FrameRate.ToString());
                arguments.Add("-t");
                arguments.Add(FormatNumber(window.RenderDuration));
            }
            arguments.Add("-i");
            arguments.Add(entry.SourcePath);
            visualInputs[entry.Item.Id] = new VisualInput($"[{inputIndex}:v]", entry, window);
            inputIndex++;
        }

        var audioEntries = await BuildAudioEntriesAsync(project, sequence, visualEntries, cancellationToken);
        var audioInputs = new List<AudioInput>();
        foreach (var entry in audioEntries)
        {
            arguments.Add("-i");
            arguments.Add(entry.SourcePath);
            audioInputs.Add(new AudioInput($"[{inputIndex}:a]", entry));
            inputIndex++;
        }

        var filters = new List<string>();
        var currentBase = "base";
        filters.Add(BuildBackgroundFilter(sequence, timelineDuration, currentBase));
        var layerNumber = 0;

        foreach (var entry in visualEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = entry.Item;

            if (item.Kind == ItemKind.AdjustmentLayer)
            {
                var enabled = $"between(t,{FormatNumber(item.TimelineStart.Seconds)},{FormatNumber(item.TimelineEnd.Seconds)})";
                var adjustmentFilters = new List<string>();
                AppendCoreVisualEffects(adjustmentFilters, item, enabled);
                if (adjustmentFilters.Count > 0)
                {
                    var output = $"adjust{layerNumber}";
                    filters.Add($"[{currentBase}]{string.Join(",", adjustmentFilters)}[{output}]");
                    currentBase = output;
                    layerNumber++;
                }
                continue;
            }

            if (item.Kind == ItemKind.Text)
            {
                var textFilePath = Path.Combine(Path.GetTempPath(), $"rushframe-text-{Guid.NewGuid():N}.txt");
                File.WriteAllText(textFilePath, item.TextContent ?? "Text", new UTF8Encoding(false));
                temporaryTextFiles.Add(textFilePath);

                var textLayer = AppendTextLayer(filters, item, transitionWindows, textFilePath, sequence, timelineDuration, layerNumber);
                transitionWindows.Right.TryGetValue(item.Id, out var rightTransition);
                var textOverlayX = BuildOverlayPositionExpression(item, AnimationPropertyNames.PositionX, item.Transform.PositionX, "main_w", "overlay_w");
                var textOverlayY = BuildOverlayPositionExpression(item, AnimationPropertyNames.PositionY, item.Transform.PositionY, "main_h", "overlay_h");
                ApplyTransitionPosition(ref textOverlayX, ref textOverlayY, rightTransition, sequence);
                var textEnable = $"between(t,{FormatNumber(rightTransition?.Start ?? item.TimelineStart.Seconds)},{FormatNumber(item.TimelineEnd.Seconds)})";
                var output = $"text{layerNumber}";
                var textBlendMode = BlendModeToFfmpeg(item.BlendMode);
                if (textBlendMode == null)
                {
                    filters.Add($"[{currentBase}][{textLayer}]overlay=x='{textOverlayX}':y='{textOverlayY}':enable='{textEnable}':eof_action=pass:shortest=0[{output}]");
                }
                else
                {
                    var canvas = $"textblendcanvas{layerNumber}";
                    var fullLayer = $"textblendlayer{layerNumber}";
                    filters.Add($"color=c=black@0.0:s={sequence.Width}x{sequence.Height}:r={sequence.FrameRate}:d={FormatNumber(timelineDuration)},format=rgba[{canvas}]");
                    filters.Add($"[{canvas}][{textLayer}]overlay=x='{textOverlayX}':y='{textOverlayY}':enable='{textEnable}':eof_action=pass[{fullLayer}]");
                    filters.Add($"[{currentBase}][{fullLayer}]blend=all_mode={textBlendMode}:enable='{textEnable}'[{output}]");
                }
                currentBase = output;
                layerNumber++;
                continue;
            }

            if (!visualInputs.TryGetValue(item.Id, out var visualInput))
                continue;

            var layerLabel = $"layer{layerNumber}";
            var localOffset = visualInput.Window.RenderStart - item.TimelineStart.Seconds;
            var localTime = OffsetTimeExpression("t", localOffset);
            var alphaTime = OffsetTimeExpression("T", localOffset);
            var scaleX = BuildAnimatedExpression(item, AnimationPropertyNames.ScaleX, item.Transform.ScaleX, localTime);
            var scaleY = BuildAnimatedExpression(item, AnimationPropertyNames.ScaleY, item.Transform.ScaleY, localTime);
            ApplyTransitionScale(ref scaleX, ref scaleY, visualInput.Window.RightTransition);
            var rotation = BuildAnimatedExpression(item, AnimationPropertyNames.Rotation, item.Transform.RotationDegrees, localTime);

            var videoFilters = new List<string>
            {
                $"trim=start={FormatNumber(visualInput.Window.SourceStart)}:duration={FormatNumber(visualInput.Window.SourceDuration)}",
                "setpts=PTS-STARTPTS",
            };
            if (item.Reversed) videoFilters.Add("reverse");
            var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
            if (Math.Abs(speed - 1) > 0.0001)
                videoFilters.Add($"setpts=PTS/{FormatNumber(speed)}");
            if (visualInput.Window.PaddingAfter > 0)
                videoFilters.Add($"tpad=stop_mode=clone:stop_duration={FormatNumber(visualInput.Window.PaddingAfter)}");

            if (IsBuiltInSticker(item))
            {
                var fill = ParseHexColor(item.FillColor ?? "#FFFFFF", 1).Ffmpeg;
                var outline = ParseHexColor(item.OutlineColor ?? "#000000", 1).Ffmpeg;
                videoFilters.Add("format=rgba");
                videoFilters.Add("colorchannelmixer=aa=0");
                videoFilters.Add(
                    $"drawtext=text='{EscapeDrawText(BuiltInStickerGlyph(item.StickerId))}':" +
                    $"fontsize=360:fontcolor={fill}:borderw={FormatNumber(Math.Max(0, item.OutlineWidth))}:" +
                    $"bordercolor={outline}:x=(w-text_w)/2:y=(h-text_h)/2");
            }

            AppendCrop(videoFilters, item);
            videoFilters.Add($"scale=w='max(2,iw*({scaleX}))':h='max(2,ih*({scaleY}))':eval=frame");
            if (item.GetAnimationChannel(AnimationPropertyNames.Rotation) != null || Math.Abs(item.Transform.RotationDegrees) > 0.0001)
                videoFilters.Add($"rotate='({rotation})*PI/180':ow=rotw(iw):oh=roth(ih):c=none");
            AppendCoreVisualEffects(videoFilters, item);

            var alphaExpression = BuildAlphaExpression(item, visualInput.Window, alphaTime);
            if (!string.Equals(alphaExpression, "1", StringComparison.Ordinal))
            {
                videoFilters.Add("format=rgba");
                videoFilters.Add($"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*clip({alphaExpression},0,1)'");
            }
            videoFilters.Add($"setpts=PTS+{FormatNumber(visualInput.Window.RenderStart)}/TB");
            filters.Add($"{visualInput.Label}{string.Join(",", videoFilters)}[{layerLabel}]");

            var composed = $"video{layerNumber}";
            var overlayX = BuildOverlayPositionExpression(item, AnimationPropertyNames.PositionX, item.Transform.PositionX, "main_w", "overlay_w");
            var overlayY = BuildOverlayPositionExpression(item, AnimationPropertyNames.PositionY, item.Transform.PositionY, "main_h", "overlay_h");
            ApplyTransitionPosition(ref overlayX, ref overlayY, visualInput.Window.RightTransition, sequence);
            var enable = $"between(t,{FormatNumber(visualInput.Window.RenderStart)},{FormatNumber(visualInput.Window.RenderEnd)})";
            var blendMode = BlendModeToFfmpeg(item.BlendMode);

            if (blendMode == null)
            {
                filters.Add($"[{currentBase}][{layerLabel}]overlay=x='{overlayX}':y='{overlayY}':enable='{enable}':eof_action=pass:shortest=0[{composed}]");
            }
            else
            {
                var canvas = $"blendcanvas{layerNumber}";
                var fullLayer = $"blendlayer{layerNumber}";
                filters.Add($"color=c=black@0.0:s={sequence.Width}x{sequence.Height}:r={sequence.FrameRate}:d={FormatNumber(timelineDuration)},format=rgba[{canvas}]");
                filters.Add($"[{canvas}][{layerLabel}]overlay=x='{overlayX}':y='{overlayY}':enable='{enable}':eof_action=pass[{fullLayer}]");
                filters.Add($"[{currentBase}][{fullLayer}]blend=all_mode={blendMode}:enable='{enable}'[{composed}]");
            }

            currentBase = composed;
            layerNumber++;
        }

        var audioOutput = BuildAudioGraph(filters, audioInputs, transitionWindows, options.IncludeAudio);

        if (renderWidth != sequence.Width || renderHeight != sequence.Height)
        {
            const string scaledOutput = "vout";
            filters.Add($"[{currentBase}]scale={renderWidth}:{renderHeight}:force_original_aspect_ratio=decrease,pad={renderWidth}:{renderHeight}:(ow-iw)/2:(oh-ih)/2:color=black[{scaledOutput}]");
            currentBase = scaledOutput;
        }

        var filterGraph = string.Join(";", filters);
        if (filterGraph.Length >= 24_000)
        {
            var graphFilePath = Path.Combine(Path.GetTempPath(), $"rushframe-filter-{Guid.NewGuid():N}.txt");
            File.WriteAllText(graphFilePath, filterGraph, new UTF8Encoding(false));
            temporaryTextFiles.Add(graphFilePath);
            arguments.Add("-filter_complex_script");
            arguments.Add(graphFilePath);
        }
        else
        {
            arguments.Add("-filter_complex");
            arguments.Add(filterGraph);
        }
        arguments.Add("-map");
        arguments.Add($"[{currentBase}]");
        if (audioOutput != null)
        {
            arguments.Add("-map");
            arguments.Add($"[{audioOutput}]");
        }
        if (renderStart > 0)
        {
            arguments.Add("-ss");
            arguments.Add(FormatNumber(renderStart));
        }
        if (rangeDurationSeconds.HasValue || rangeStartSeconds.HasValue)
        {
            arguments.Add("-t");
            arguments.Add(FormatNumber(renderDuration));
        }
        ConfigureOutput(arguments, sequence, options, audioOutput != null, outputPath);

        progress?.Report(new MediaJobProgress(5, "Rendering timeline"));
        await RunAsync(_ffmpegPath, arguments, cancellationToken);
        progress?.Report(new MediaJobProgress(100, "Timeline export complete"));
        }
        finally
        {
            foreach (var textFilePath in temporaryTextFiles)
            {
                try { File.Delete(textFilePath); }
                catch { }
            }
        }
    }

    public Task ExportTimelineRangeAsync(
        Project project,
        Sequence sequence,
        string outputPath,
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken = default,
        int? outputWidth = null,
        int? outputHeight = null,
        TimelineExportOptions? exportOptions = null) =>
        ExportTimelineAsync(
            project,
            sequence,
            outputPath,
            cancellationToken: cancellationToken,
            outputWidth: outputWidth,
            outputHeight: outputHeight,
            exportOptions: exportOptions,
            rangeStartSeconds: startSeconds,
            rangeDurationSeconds: durationSeconds);

    public static void ValidateRenderCapabilities(Sequence sequence)
    {
        var unsupportedEffects = sequence.Tracks.SelectMany(track => track.Items)
            .SelectMany(item => item.Effects)
            .Where(effect => effect.Enabled && !SupportedEffectTypeIds.Contains(effect.EffectTypeId))
            .Select(effect => effect.EffectTypeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unsupportedEffects.Length > 0)
            throw new NotSupportedException($"Unsupported render effects: {string.Join(", ", unsupportedEffects)}");

        var unsupportedMasks = sequence.Tracks.SelectMany(track => track.Items)
            .SelectMany(item => item.Masks)
            .Where(mask => !SupportedMaskShapes.Contains(mask.Shape))
            .Select(mask => mask.Shape)
            .Distinct()
            .ToArray();
        if (unsupportedMasks.Length > 0)
            throw new NotSupportedException($"Unsupported render masks: {string.Join(", ", unsupportedMasks)}");
    }

    private static List<VisualEntry> BuildVisualEntries(Project project, Sequence sequence) =>
        sequence.Tracks
            .Where(track => track.Kind is TrackKind.Video or TrackKind.Overlay or TrackKind.Text && !track.Hidden)
            .OrderBy(track => track.Order)
            .SelectMany(track => track.Items.Select(item => new VisualEntry(
                track,
                item,
                ResolveVisualSource(project, item),
                IsImageSource(project, item))))
            .Where(entry => entry.Item.Kind is ItemKind.Clip or ItemKind.Image or ItemKind.Text or ItemKind.Sticker or ItemKind.AdjustmentLayer)
            .OrderBy(entry => entry.Track.Order)
            .ThenBy(entry => entry.Item.TimelineStart.Ticks)
            .ToList();

    private static string? ResolveVisualSource(Project project, TimelineItem item)
    {
        if (item.MediaAssetId is { } mediaId)
        {
            var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == mediaId);
            if (asset != null && File.Exists(asset.OriginalPath)) return asset.OriginalPath;
        }

        if (item.Kind == ItemKind.Sticker && !string.IsNullOrWhiteSpace(item.StickerId))
        {
            var asset = project.AssetProviders.SelectMany(provider => provider.Assets)
                .FirstOrDefault(candidate => candidate.Id == item.StickerId);
            if (asset != null && File.Exists(asset.LocalPath)) return asset.LocalPath;
        }
        return null;
    }

    private static bool IsBuiltInSticker(TimelineItem item) =>
        item.Kind == ItemKind.Sticker
        && item.StickerId?.StartsWith("builtin.shape.", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuiltInStickerGlyph(string? id) => id switch
    {
        "builtin.shape.star" => "★",
        "builtin.shape.circle" => "●",
        "builtin.shape.triangle" => "▲",
        "builtin.shape.diamond" => "◆",
        "builtin.shape.arrow" => "➜",
        "builtin.shape.heart" => "♥",
        "builtin.shape.speech" => "▰",
        _ => "◆",
    };

    private static bool IsImageSource(Project project, TimelineItem item)
    {
        if (item.Kind is ItemKind.Image or ItemKind.Sticker) return true;
        if (item.MediaAssetId is not { } mediaId) return false;
        return project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == mediaId)?.Kind == MediaKind.Image;
    }

    private async Task<List<AudioEntry>> BuildAudioEntriesAsync(
        Project project,
        Sequence sequence,
        IReadOnlyList<VisualEntry> visualEntries,
        CancellationToken cancellationToken)
    {
        var entries = sequence.Tracks
            .Where(track => track.Kind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice && !track.Hidden && !track.Muted)
            .OrderBy(track => track.Order)
            .SelectMany(track => track.Items.Select(item => new { track, item }))
            .Where(entry => entry.item.MediaAssetId.HasValue && !entry.item.Muted)
            .Select(entry =>
            {
                var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == entry.item.MediaAssetId!.Value);
                return asset != null && File.Exists(asset.OriginalPath)
                    ? new AudioEntry(entry.track, entry.item, asset.OriginalPath)
                    : null;
            })
            .Where(entry => entry != null)
            .Cast<AudioEntry>()
            .ToList();

        var existingIds = entries.Select(entry => entry.Item.Id).ToHashSet();
        foreach (var visual in visualEntries.Where(entry => entry.Item.Kind == ItemKind.Clip && !entry.Track.Muted && !entry.Item.Muted && entry.SourcePath != null))
        {
            if (existingIds.Contains(visual.Item.Id)) continue;
            try
            {
                var probe = await ProbeAsync(visual.SourcePath!, cancellationToken);
                if (!probe.HasAudio) continue;
                entries.Add(new AudioEntry(visual.Track, visual.Item, visual.SourcePath!));
                existingIds.Add(visual.Item.Id);
            }
            catch
            {
                // Visual rendering remains useful if an optional audio probe fails.
            }
        }

        return entries.OrderBy(entry => entry.Item.TimelineStart.Ticks).ToList();
    }

    private static TransitionMaps BuildTransitionWindows(Sequence sequence)
    {
        var items = sequence.Tracks.SelectMany(track => track.Items).ToDictionary(item => item.Id);
        var left = new Dictionary<TimelineItemId, TransitionWindow>();
        var right = new Dictionary<TimelineItemId, TransitionWindow>();
        foreach (var transition in sequence.Transitions)
        {
            if (!items.TryGetValue(transition.LeftItemId, out var leftItem)
                || !items.TryGetValue(transition.RightItemId, out var rightItem))
                continue;
            var duration = Math.Clamp(transition.Duration.Seconds, 0, Math.Max(leftItem.Duration.Seconds, rightItem.Duration.Seconds));
            if (duration <= 0) continue;
            var cut = rightItem.TimelineStart.Seconds;
            var start = Math.Max(0, cut - (duration * Math.Clamp(transition.Alignment, 0, 1)));
            var window = new TransitionWindow(transition, start, start + duration, duration);
            left[leftItem.Id] = window;
            right[rightItem.Id] = window;
        }
        return new TransitionMaps(left, right);
    }

    private static VisualWindow ResolveVisualWindow(VisualEntry entry, TransitionMaps transitions)
    {
        transitions.Left.TryGetValue(entry.Item.Id, out var leftTransition);
        transitions.Right.TryGetValue(entry.Item.Id, out var rightTransition);
        var item = entry.Item;
        var renderStart = rightTransition?.Start ?? item.TimelineStart.Seconds;
        var renderEnd = Math.Max(item.TimelineEnd.Seconds, leftTransition?.End ?? item.TimelineEnd.Seconds);
        var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
        var preRoll = Math.Max(0, item.TimelineStart.Seconds - renderStart);
        var sourceStart = entry.IsImage ? 0 : Math.Max(0, item.SourceStart.Seconds - (preRoll * speed));
        if (!entry.IsImage && item.SourceStart.Seconds < preRoll * speed)
        {
            preRoll = item.SourceStart.Seconds / speed;
            renderStart = item.TimelineStart.Seconds - preRoll;
        }
        var renderDuration = Math.Max(1 / 1_000.0, renderEnd - renderStart);
        var desiredSourceDuration = entry.IsImage ? renderDuration : renderDuration * speed;
        var availableSourceDuration = item.SourceDuration.Seconds > 0
            ? Math.Max(0, item.SourceStart.Seconds + item.SourceDuration.Seconds - sourceStart)
            : desiredSourceDuration;
        var sourceDuration = entry.IsImage ? renderDuration : Math.Min(desiredSourceDuration, availableSourceDuration);
        var paddingAfter = Math.Max(0, renderDuration - (sourceDuration / speed));
        return new VisualWindow(renderStart, renderEnd, renderDuration, sourceStart, Math.Max(sourceDuration, 0.001), paddingAfter, leftTransition, rightTransition);
    }

    private static string BuildBackgroundFilter(Sequence sequence, double duration, string outputLabel)
    {
        var background = sequence.Background ?? new CanvasBackground();
        var opacity = Math.Clamp(background.Opacity, 0, 1);
        var primary = ParseHexColor(background.PrimaryColor, opacity);
        var secondary = ParseHexColor(background.SecondaryColor, opacity);
        var rate = sequence.FrameRate.ToString();

        if (background.Kind is CanvasBackgroundKind.Solid or CanvasBackgroundKind.BlurSource or CanvasBackgroundKind.Transparent)
        {
            var color = background.Kind == CanvasBackgroundKind.Transparent ? "black@0.0" : primary.Ffmpeg;
            return $"color=c={color}:s={sequence.Width}x{sequence.Height}:r={rate}:d={FormatNumber(duration)},format=rgba[{outputLabel}]";
        }

        string progress;
        if (background.Kind == CanvasBackgroundKind.RadialGradient)
        {
            progress = "clip(hypot(X-W/2,Y-H/2)/hypot(W/2,H/2),0,1)";
        }
        else
        {
            var radians = background.GradientAngleDegrees * Math.PI / 180;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            progress = $"clip(((X-W/2)*{FormatNumber(cos)}+(Y-H/2)*{FormatNumber(sin)})/max(W,H)+0.5,0,1)";
        }

        return $"nullsrc=s={sequence.Width}x{sequence.Height}:r={rate}:d={FormatNumber(duration)},format=rgba," +
               $"geq=r='{primary.R}+({secondary.R}-{primary.R})*{progress}':" +
               $"g='{primary.G}+({secondary.G}-{primary.G})*{progress}':" +
               $"b='{primary.B}+({secondary.B}-{primary.B})*{progress}':a='255'[{outputLabel}]";
    }

    private static string AppendTextLayer(
        ICollection<string> filters,
        TimelineItem item,
        TransitionMaps transitions,
        string textFilePath,
        Sequence sequence,
        double timelineDuration,
        int layerNumber)
    {
        var measured = TextLayoutMetrics.Measure(item);
        var width = EnsureEven(Math.Max(2, (int)Math.Ceiling(measured.Width)));
        var height = EnsureEven(Math.Max(2, (int)Math.Ceiling(measured.Height)));
        var canvas = $"textcanvas{layerNumber}";
        var shadowRaw = $"textshadowraw{layerNumber}";
        var shadowLayer = $"textshadow{layerNumber}";
        var drawn = $"textraw{layerNumber}";
        var output = $"textlayer{layerNumber}";
        filters.Add($"color=c=black@0.0:s={width}x{height}:r={sequence.FrameRate}:d={FormatNumber(timelineDuration)},format=rgba[{canvas}]");

        var escapedTextFilePath = EscapeDrawText(textFilePath.Replace("\\", "/"));
        var padding = Math.Max(1, Math.Max(0, item.OutlineWidth) + Math.Max(0, item.ShadowBlur) + Math.Max(Math.Abs(item.ShadowOffsetX), Math.Abs(item.ShadowOffsetY)));
        var textX = item.FontAlign?.Trim().ToLowerInvariant() switch
        {
            "left" => FormatNumber(padding),
            "right" => $"w-text_w-{FormatNumber(padding)}",
            _ => "(w-text_w)/2",
        };
        var textY = "(h-text_h)/2";
        var fontOptions = BuildTextFontOptions(item);
        var baseOptions = new List<string>
        {
            $"textfile='{escapedTextFilePath}'",
            $"fontsize={FormatNumber(Math.Max(1, item.FontSize))}",
            $"x='{textX}'",
            $"y='{textY}'",
        };
        baseOptions.AddRange(fontOptions);

        var drawInput = canvas;
        if (item.ShadowOpacity > 0.0001)
        {
            var shadowColor = ParseHexColor(item.ShadowColor ?? "#000000", Math.Clamp(item.ShadowOpacity, 0, 1)).Ffmpeg;
            var shadowOptions = new List<string>(baseOptions)
            {
                $"fontcolor={shadowColor}",
                $"x='({textX})+{FormatNumber(item.ShadowOffsetX)}'",
                $"y='({textY})+{FormatNumber(item.ShadowOffsetY)}'",
            };
            filters.Add($"[{canvas}]drawtext={string.Join(":", shadowOptions)}[{shadowRaw}]");
            if (item.ShadowBlur > 0.0001)
            {
                filters.Add($"[{shadowRaw}]gblur=sigma={FormatNumber(Math.Clamp(item.ShadowBlur, 0, 50))}[{shadowLayer}]");
                drawInput = shadowLayer;
            }
            else
            {
                drawInput = shadowRaw;
            }
        }

        var mainOptions = new List<string>(baseOptions)
        {
            $"fontcolor={ParseHexColor(item.FillColor ?? "#FFFFFF", 1).Ffmpeg}",
            $"borderw={FormatNumber(Math.Max(0, item.OutlineWidth))}",
            $"bordercolor={ParseHexColor(item.OutlineColor ?? "#000000", 1).Ffmpeg}",
        };
        filters.Add($"[{drawInput}]drawtext={string.Join(":", mainOptions)}[{drawn}]");

        transitions.Right.TryGetValue(item.Id, out var rightTransition);
        var localTime = $"(t-{FormatNumber(item.TimelineStart.Seconds)})";
        var scaleX = BuildAnimatedExpression(item, AnimationPropertyNames.ScaleX, item.Transform.ScaleX, localTime);
        var scaleY = BuildAnimatedExpression(item, AnimationPropertyNames.ScaleY, item.Transform.ScaleY, localTime);
        ApplyTransitionScale(
            ref scaleX,
            ref scaleY,
            rightTransition,
            rightTransition == null ? "t" : $"(t-{FormatNumber(rightTransition.Start)})");
        var rotation = BuildAnimatedExpression(item, AnimationPropertyNames.Rotation, item.Transform.RotationDegrees, localTime);
        var textFilters = new List<string>
        {
            $"scale=w='max(2,iw*({scaleX}))':h='max(2,ih*({scaleY}))':eval=frame",
        };
        if (item.GetAnimationChannel(AnimationPropertyNames.Rotation) != null || Math.Abs(item.Transform.RotationDegrees) > 0.0001)
            textFilters.Add($"rotate='({rotation})*PI/180':ow=rotw(iw):oh=roth(ih):c=none");
        AppendCoreVisualEffects(textFilters, item);

        var alphaTime = $"(T-{FormatNumber(item.TimelineStart.Seconds)})";
        var opacity = BuildAnimatedExpression(item, AnimationPropertyNames.Opacity, item.Opacity, alphaTime);
        opacity = MultiplyExpression(opacity, BuildItemFadeExpression(item, alphaTime));
        foreach (var mask in item.Masks)
            opacity = MultiplyExpression(opacity, BuildMaskExpression(mask));
        if (rightTransition != null && rightTransition.Transition.Kind is TransitionKind.CrossDissolve or TransitionKind.Zoom or TransitionKind.Blur or TransitionKind.Mask)
            opacity = MultiplyExpression(opacity, $"clip((T-{FormatNumber(rightTransition.Start)})/{FormatNumber(rightTransition.Duration)},0,1)");
        if (!string.Equals(opacity, "1", StringComparison.Ordinal))
        {
            textFilters.Add("format=rgba");
            textFilters.Add($"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*clip({opacity},0,1)'");
        }
        filters.Add($"[{drawn}]{string.Join(",", textFilters)}[{output}]");
        return output;
    }

    private static IReadOnlyList<string> BuildTextFontOptions(TimelineItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FontFamily)) return [];
        var fontFile = File.Exists(item.FontFamily)
            ? Path.GetFullPath(item.FontFamily)
            : ResolveInstalledWindowsFontFile(item.FontFamily, item.FontBold);
        if (!string.IsNullOrWhiteSpace(fontFile))
            return [$"fontfile='{EscapeDrawText(fontFile.Replace("\\", "/"))}'"];

        var family = item.FontBold ? $"{item.FontFamily}:style=Bold" : item.FontFamily;
        return [$"font='{EscapeDrawText(family)}'"];
    }

    private static string? ResolveInstalledWindowsFontFile(string familyName, bool bold)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(familyName)) return null;
        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidates = new List<(string Path, int Score)>();
        CollectRegisteredFonts(Registry.LocalMachine, familyName, bold, fontsDirectory, candidates);
        CollectRegisteredFonts(Registry.CurrentUser, familyName, bold, fontsDirectory, candidates);
        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault(File.Exists);
    }

    [SupportedOSPlatform("windows")]
    private static void CollectRegisteredFonts(
        RegistryKey root,
        string familyName,
        bool bold,
        string fontsDirectory,
        ICollection<(string Path, int Score)> candidates)
    {
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", writable: false);
        if (key == null) return;
        foreach (var valueName in key.GetValueNames())
        {
            var registeredName = valueName.Replace("(TrueType)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("(OpenType)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (!registeredName.StartsWith(familyName, StringComparison.OrdinalIgnoreCase)) continue;
            if (key.GetValue(valueName) is not string registeredPath || string.IsNullOrWhiteSpace(registeredPath)) continue;
            var path = Path.IsPathFullyQualified(registeredPath)
                ? registeredPath
                : Path.Combine(fontsDirectory, registeredPath);
            var hasBold = registeredName.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                || registeredName.Contains("Semibold", StringComparison.OrdinalIgnoreCase)
                || registeredName.Contains("Black", StringComparison.OrdinalIgnoreCase);
            var hasItalic = registeredName.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                || registeredName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            var score = string.Equals(registeredName, familyName, StringComparison.OrdinalIgnoreCase) ? 100 : 50;
            score += bold == hasBold ? 30 : -20;
            if (!hasItalic) score += 10;
            candidates.Add((path, score));
        }
    }

    private static void AppendCrop(List<string> filters, TimelineItem item)
    {
        var left = Math.Clamp(item.CropLeft, 0, 0.99);
        var top = Math.Clamp(item.CropTop, 0, 0.99);
        var right = Math.Clamp(item.CropRight, 0, 0.99 - left);
        var bottom = Math.Clamp(item.CropBottom, 0, 0.99 - top);
        if (left + top + right + bottom <= 0.0001) return;
        filters.Add($"crop=iw*{FormatNumber(1 - left - right)}:ih*{FormatNumber(1 - top - bottom)}:iw*{FormatNumber(left)}:ih*{FormatNumber(top)}");
    }

    private static void AppendCoreVisualEffects(List<string> filters, TimelineItem item, string? enable = null)
    {
        string Enabled(string filter) => string.IsNullOrWhiteSpace(enable) ? filter : $"{filter}:enable='{enable}'";

        if (item.ChromaKey is { } chroma && !string.IsNullOrWhiteSpace(chroma.Color))
            filters.Add(Enabled($"chromakey={ParseHexColor(chroma.Color, 1).Ffmpeg}:similarity={FormatNumber(chroma.Similarity)}:blend={FormatNumber(chroma.EdgeSoftness)}"));

        if (item.ColorCorrection is { } color)
        {
            var contrast = Math.Clamp(1 + color.Contrast, 0, 4);
            var brightness = Math.Clamp(color.Brightness + color.Exposure, -1, 1);
            var saturation = color.BlackAndWhite ? 0 : Math.Clamp(color.Saturation, 0, 4);
            filters.Add(Enabled($"eq=contrast={FormatNumber(contrast)}:brightness={FormatNumber(brightness)}:saturation={FormatNumber(saturation)}"));
            if (Math.Abs(color.Tint) > 0.0001)
                filters.Add(Enabled($"hue=h={FormatNumber(Math.Clamp(color.Tint, -180, 180))}"));
        }

        if (item.Stabilization?.Enabled == true)
            filters.Add(Enabled("deshake"));

        foreach (var effect in item.Effects.Where(effect => effect.Enabled))
        {
            switch (effect.EffectTypeId)
            {
                case "mono": filters.Add(Enabled("hue=s=0")); break;
                case "sepia": filters.Add(Enabled("colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131")); break;
                case "blur": filters.Add(Enabled($"gblur=sigma={FormatNumber(Math.Clamp(GetParam(effect, "strength", 5), 0, 50))}")); break;
                case "brightness": filters.Add(Enabled($"eq=brightness={FormatNumber(GetParam(effect, "amount", 0.1))}")); break;
                case "contrast": filters.Add(Enabled($"eq=contrast={FormatNumber(1 + GetParam(effect, "amount", 0.1))}")); break;
                case "vignette": filters.Add(Enabled("vignette")); break;
                case "noise_reduction":
                    var denoise = Math.Clamp(GetParam(effect, "strength", 0.3), 0, 1) * 8;
                    filters.Add(Enabled($"hqdn3d={FormatNumber(denoise)}:{FormatNumber(denoise)}:{FormatNumber(denoise * 1.5)}:{FormatNumber(denoise * 1.5)}"));
                    break;
                case "motion_blur":
                    var frames = Math.Clamp((int)Math.Round(GetParam(effect, "samples", 4)), 2, 12);
                    filters.Add(Enabled($"tmix=frames={frames}"));
                    break;
                case "glitch":
                    var shift = Math.Clamp((int)Math.Round(GetParam(effect, "intensity", 0.3) * 16), 1, 24);
                    filters.Add(Enabled($"rgbashift=rh={shift}:bh=-{shift}"));
                    break;
                case "film_grain": filters.Add(Enabled($"noise=alls={FormatNumber(Math.Clamp(GetParam(effect, "intensity", 0.2) * 40, 0, 100))}:allf=t")); break;
                case "food_pop": filters.Add(Enabled($"eq=saturation={FormatNumber(GetParam(effect, "saturation", 1.25))}:brightness={FormatNumber(GetParam(effect, "warmth", 0.08))}")); break;
                case "night_lift": filters.Add(Enabled($"eq=brightness={FormatNumber(GetParam(effect, "brightness", 0.08))}:contrast={FormatNumber(1 + GetParam(effect, "contrast", 0.15))}")); break;
                case "nature_green": filters.Add(Enabled($"eq=saturation={FormatNumber(1 + GetParam(effect, "intensity", 0.35))}:gamma_g={FormatNumber(1 + GetParam(effect, "intensity", 0.35) * 0.15)}")); break;
                case "spark_flash": filters.Add(Enabled($"eq=brightness={FormatNumber(Math.Clamp(GetParam(effect, "intensity", 0.5), -1, 1))}")); break;
                case "love_soft": filters.Add(Enabled($"gblur=sigma={FormatNumber(Math.Clamp(GetParam(effect, "softness", 0.25) * 4, 0, 10))}")); break;
                case "lens_punch":
                    var zoom = Math.Clamp(GetParam(effect, "zoom", 1.08), 1, 2);
                    filters.Add(Enabled($"crop=iw/{FormatNumber(zoom)}:ih/{FormatNumber(zoom)}:(iw-ow)/2:(ih-oh)/2"));
                    filters.Add(Enabled($"scale=iw*{FormatNumber(zoom)}:ih*{FormatNumber(zoom)}"));
                    filters.Add(Enabled("vignette"));
                    break;
                case "body_smooth_motion": filters.Add(Enabled("minterpolate=fps=60:mi_mode=mci")); break;
                case "stabilize": filters.Add(Enabled("deshake")); break;
            }
        }
    }

    private static string BuildAlphaExpression(TimelineItem item, VisualWindow window, string localTime)
    {
        var expression = BuildAnimatedExpression(item, AnimationPropertyNames.Opacity, item.Opacity, localTime);
        expression = MultiplyExpression(expression, BuildItemFadeExpression(item, localTime));
        foreach (var mask in item.Masks)
            expression = MultiplyExpression(expression, BuildMaskExpression(mask));

        var transition = window.RightTransition;
        if (transition != null)
        {
            var progress = $"clip(T/{FormatNumber(transition.Duration)},0,1)";
            expression = transition.Transition.Kind switch
            {
                TransitionKind.CrossDissolve or TransitionKind.Zoom or TransitionKind.Blur => MultiplyExpression(expression, progress),
                TransitionKind.Wipe => MultiplyExpression(expression, $"lte(X,W*({progress}))"),
                TransitionKind.Mask => MultiplyExpression(expression, $"lte(hypot(X-W/2,Y-H/2),hypot(W/2,H/2)*({progress}))"),
                _ => expression,
            };
        }
        return expression;
    }

    private static string BuildItemFadeExpression(TimelineItem item, string localTime)
    {
        var expression = "1";
        if (item.FadeInDuration.Seconds > 0)
            expression = MultiplyExpression(expression, $"clip(({localTime})/{FormatNumber(item.FadeInDuration.Seconds)},0,1)");
        if (item.FadeOutDuration.Seconds > 0)
        {
            var start = Math.Max(0, item.Duration.Seconds - item.FadeOutDuration.Seconds);
            expression = MultiplyExpression(expression, $"clip(({FormatNumber(item.Duration.Seconds)}-({localTime}))/{FormatNumber(item.FadeOutDuration.Seconds)},0,1)");
        }
        return expression;
    }

    private static string BuildMaskExpression(Mask mask)
    {
        var radians = mask.RotationDegrees * Math.PI / 180;
        var cos = FormatNumber(Math.Cos(radians));
        var sin = FormatNumber(Math.Sin(radians));
        var cx = $"(W/2+{FormatNumber(mask.PositionX)})";
        var cy = $"(H/2+{FormatNumber(mask.PositionY)})";
        var dx = $"((X-{cx})*{cos}+(Y-{cy})*{sin})";
        var dy = $"(-(X-{cx})*{sin}+(Y-{cy})*{cos})";
        var halfWidth = $"max(1,W*{FormatNumber(Math.Max(0.001, mask.ScaleX))}/2+{FormatNumber(mask.Expansion)})";
        var halfHeight = $"max(1,H*{FormatNumber(Math.Max(0.001, mask.ScaleY))}/2+{FormatNumber(mask.Expansion)})";
        var feather = Math.Max(0.001, mask.Feather);
        string Soft(string signedDistance) => $"clip((({signedDistance})+{FormatNumber(feather)})/{FormatNumber(feather)},0,1)";

        var expression = mask.Shape switch
        {
            MaskShape.Rectangle => Soft($"min(({halfWidth})-abs({dx}),({halfHeight})-abs({dy}))"),
            MaskShape.Ellipse => Soft($"(1-hypot(({dx})/({halfWidth}),({dy})/({halfHeight})))*min({halfWidth},{halfHeight})"),
            MaskShape.Diamond => Soft($"(1-abs({dx})/({halfWidth})-abs({dy})/({halfHeight}))*min({halfWidth},{halfHeight})"),
            MaskShape.Linear => Soft($"({halfWidth})-abs({dx})"),
            MaskShape.Split => Soft($"-abs({dx})+({halfWidth})/2"),
            MaskShape.Mirror => Soft($"min(({halfWidth})-abs({dx}),abs({dx})-({halfWidth})*0.2)"),
            MaskShape.Star => BuildRadialMask(dx, dy, halfWidth, halfHeight, 5, true, feather),
            MaskShape.Polygon => BuildRadialMask(dx, dy, halfWidth, halfHeight, Math.Clamp(mask.PolygonSides, 3, 12), false, feather),
            MaskShape.Heart => BuildHeartMask(dx, dy, halfWidth, halfHeight),
            MaskShape.Text => BuildApproximateTextMask(mask, dx, dy, halfWidth, halfHeight, Soft),
            MaskShape.Custom => BuildCustomPolygonMask(mask),
            _ => "1",
        };
        return mask.Inverted ? $"1-({expression})" : expression;
    }

    private static string BuildRadialMask(string dx, string dy, string halfWidth, string halfHeight, int points, bool star, double feather)
    {
        var normalizedX = $"({dx})/({halfWidth})";
        var normalizedY = $"({dy})/({halfHeight})";
        var angle = $"atan2({normalizedY},{normalizedX})";
        var radius = $"hypot({normalizedX},{normalizedY})";
        string boundary;
        if (star)
            boundary = $"0.58+0.42*abs(cos({points}*({angle})))";
        else
            boundary = $"cos(PI/{points})/cos(mod(({angle})+PI/{points},2*PI/{points})-PI/{points})";
        return $"clip((({boundary})-({radius}))*min({halfWidth},{halfHeight})/{FormatNumber(Math.Max(0.001, feather))}+1,0,1)";
    }

    private static string BuildHeartMask(string dx, string dy, string halfWidth, string halfHeight)
    {
        var x = $"({dx})/({halfWidth})";
        var y = $"-({dy})/({halfHeight})";
        var equation = $"pow(({x})*({x})+({y})*({y})-1,3)-({x})*({x})*pow({y},3)";
        return $"lte({equation},0)";
    }

    private static string BuildApproximateTextMask(Mask mask, string dx, string dy, string halfWidth, string halfHeight, Func<string, string> soft)
    {
        var widthFactor = Math.Clamp(Math.Max(1, mask.Text.Length) / 12.0, 0.15, 1);
        return soft($"min(({halfWidth})*{FormatNumber(widthFactor)}-abs({dx}),({halfHeight})*0.5-abs({dy}))");
    }

    private static string BuildCustomPolygonMask(Mask mask)
    {
        if (mask.Points.Count < 3) return "0";
        var terms = new List<string>();
        for (var index = 0; index < mask.Points.Count; index++)
        {
            var first = mask.Points[index];
            var second = mask.Points[(index + 1) % mask.Points.Count];
            var x1 = PointCoordinate(first.X, "W");
            var y1 = PointCoordinate(first.Y, "H");
            var x2 = PointCoordinate(second.X, "W");
            var y2 = PointCoordinate(second.Y, "H");
            terms.Add($"if(abs(gt({y1},Y)-gt({y2},Y)),lt(X,({x2}-{x1})*(Y-{y1})/(({y2}-{y1})+0.000001)+{x1}),0)");
        }
        return $"gt(mod({string.Join("+", terms)},2),0)";
    }

    private static string PointCoordinate(double value, string dimension) =>
        Math.Abs(value) <= 2 ? $"{FormatNumber(value)}*{dimension}" : FormatNumber(value);

    private static string BuildAnimatedExpression(TimelineItem item, string propertyName, double fallback, string localTime)
    {
        var channel = item.GetAnimationChannel(propertyName);
        if (channel == null || channel.Keyframes.Count == 0) return FormatNumber(fallback);
        var sampled = SampleChannel(channel);
        if (sampled.Count == 1) return FormatNumber(sampled[0].Value);

        var expression = FormatNumber(sampled[^1].Value);
        for (var index = sampled.Count - 2; index >= 0; index--)
        {
            var left = sampled[index];
            var right = sampled[index + 1];
            var span = Math.Max(0.000001, right.Time - left.Time);
            var interpolation = $"{FormatNumber(left.Value)}+({FormatNumber(right.Value - left.Value)})*clip((({localTime})-{FormatNumber(left.Time)})/{FormatNumber(span)},0,1)";
            expression = $"if(lt(({localTime}),{FormatNumber(right.Time)}),{interpolation},{expression})";
        }
        return $"if(lt(({localTime}),{FormatNumber(sampled[0].Time)}),{FormatNumber(sampled[0].Value)},{expression})";
    }

    private static List<AnimationSample> SampleChannel(AnimationChannel channel)
    {
        var keyframes = channel.Keyframes.OrderBy(keyframe => keyframe.Time.Ticks).ToList();
        if (keyframes.Count == 0) return [new AnimationSample(0, channel.DefaultValue)];
        var samples = new List<AnimationSample>();
        for (var index = 0; index < keyframes.Count - 1; index++)
        {
            var left = keyframes[index];
            var right = keyframes[index + 1];
            var subdivisions = left.Interpolation is InterpolationType.Linear or InterpolationType.Hold ? 1 : 8;
            for (var step = 0; step < subdivisions; step++)
            {
                var fraction = (double)step / subdivisions;
                var time = left.Time.Seconds + ((right.Time.Seconds - left.Time.Seconds) * fraction);
                samples.Add(new AnimationSample(time, channel.GetValueAt(MediaTime.FromSeconds(time))));
            }
        }
        var final = keyframes[^1];
        samples.Add(new AnimationSample(final.Time.Seconds, final.Value));
        return samples
            .GroupBy(sample => Math.Round(sample.Time, 6))
            .Select(group => group.Last())
            .OrderBy(sample => sample.Time)
            .ToList();
    }

    private static string BuildOverlayPositionExpression(TimelineItem item, string propertyName, double fallback, string mainDimension, string overlayDimension)
    {
        var localTime = $"(t-{FormatNumber(item.TimelineStart.Seconds)})";
        var offset = BuildAnimatedExpression(item, propertyName, fallback, localTime);
        return $"({mainDimension}-{overlayDimension})/2+({offset})";
    }

    private static void ApplyTransitionPosition(ref string x, ref string y, TransitionWindow? transition, Sequence sequence)
    {
        if (transition == null) return;
        var progress = $"clip((t-{FormatNumber(transition.Start)})/{FormatNumber(transition.Duration)},0,1)";
        switch (transition.Transition.Kind)
        {
            case TransitionKind.Slide:
                x = $"({x})+{sequence.Width}*(1-({progress}))";
                break;
            case TransitionKind.WhipPan:
                x = $"({x})+{sequence.Width}*(1-({progress}))*(1-({progress}))";
                break;
        }
    }

    private static void ApplyTransitionScale(
        ref string scaleX,
        ref string scaleY,
        TransitionWindow? transition,
        string transitionTime = "t")
    {
        if (transition?.Transition.Kind != TransitionKind.Zoom) return;
        var factor = $"0.82+0.18*clip(({transitionTime})/{FormatNumber(transition.Duration)},0,1)";
        scaleX = $"({scaleX})*({factor})";
        scaleY = $"({scaleY})*({factor})";
    }

    private static string? BuildAudioGraph(
        List<string> filters,
        IReadOnlyList<AudioInput> inputs,
        TransitionMaps transitions,
        bool includeAudio)
    {
        if (!includeAudio || inputs.Count == 0) return null;
        var labels = new List<string>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var item = input.Entry.Item;
            transitions.Left.TryGetValue(item.Id, out var leftTransition);
            transitions.Right.TryGetValue(item.Id, out var rightTransition);
            var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
            var renderStart = rightTransition?.Start ?? item.TimelineStart.Seconds;
            var renderEnd = Math.Max(item.TimelineEnd.Seconds, leftTransition?.End ?? item.TimelineEnd.Seconds);
            var preRoll = Math.Max(0, item.TimelineStart.Seconds - renderStart);
            var sourceStart = Math.Max(0, item.SourceStart.Seconds - preRoll * speed);
            var sourceDuration = Math.Max(0.001, (renderEnd - renderStart) * speed);
            var localOffset = renderStart - item.TimelineStart.Seconds;
            var localTime = OffsetTimeExpression("t", localOffset);
            var audioFilters = new List<string>
            {
                $"atrim=start={FormatNumber(sourceStart)}:duration={FormatNumber(sourceDuration)}",
                "asetpts=PTS-STARTPTS",
            };
            if (item.Reversed) audioFilters.Add("areverse");
            if (Math.Abs(speed - 1) > 0.0001)
                audioFilters.AddRange(BuildAtempoFilters(Math.Clamp(speed, 0.5, 100)));

            var volume = BuildAnimatedExpression(item, AnimationPropertyNames.Volume, item.Volume, localTime);
            audioFilters.Add($"volume='{volume}':eval=frame");
            if (Math.Abs(item.Pan) > 0.0001)
            {
                var pan = Math.Clamp(item.Pan, -1, 1);
                var leftGain = pan <= 0 ? 1 : 1 - pan;
                var rightGain = pan >= 0 ? 1 : 1 + pan;
                audioFilters.Add($"pan=stereo|c0={FormatNumber(leftGain)}*c0|c1={FormatNumber(rightGain)}*c1");
            }
            if (item.FadeInDuration.Seconds > 0)
                audioFilters.Add($"afade=t=in:st={FormatNumber(Math.Max(0, -localOffset))}:d={FormatNumber(item.FadeInDuration.Seconds)}");
            if (item.FadeOutDuration.Seconds > 0)
                audioFilters.Add($"afade=t=out:st={FormatNumber(Math.Max(0, -localOffset + item.Duration.Seconds - item.FadeOutDuration.Seconds))}:d={FormatNumber(item.FadeOutDuration.Seconds)}");
            if (rightTransition != null)
                audioFilters.Add($"afade=t=in:st=0:d={FormatNumber(rightTransition.Duration)}");
            if (leftTransition != null)
                audioFilters.Add($"afade=t=out:st={FormatNumber(Math.Max(0, renderEnd - renderStart - leftTransition.Duration))}:d={FormatNumber(leftTransition.Duration)}");
            var delay = Math.Max(0, (int)Math.Round(renderStart * 1000));
            audioFilters.Add($"adelay={delay}|{delay}");
            var label = $"audio{index}";
            filters.Add($"{input.Label}{string.Join(",", audioFilters)}[{label}]");
            labels.Add($"[{label}]");
        }
        const string output = "aout";
        filters.Add($"{string.Join("", labels)}amix=inputs={labels.Count}:normalize=0:duration=longest[{output}]");
        return output;
    }

    private static TimelineExportOptions InferExportOptions(string outputPath)
    {
        var format = Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".webm" => TimelineExportFormat.WebM,
            ".mov" => TimelineExportFormat.Mov,
            ".mkv" => TimelineExportFormat.Mkv,
            _ => TimelineExportFormat.Mp4,
        };
        return new TimelineExportOptions(format);
    }

    private static void ConfigureOutput(
        List<string> arguments,
        Sequence sequence,
        TimelineExportOptions options,
        bool hasAudio,
        string outputPath)
    {
        var (crf, preset) = options.Quality switch
        {
            TimelineExportQuality.Draft => (32, "ultrafast"),
            TimelineExportQuality.Standard => (24, "veryfast"),
            TimelineExportQuality.High => (18, "medium"),
            TimelineExportQuality.Master => (12, "slow"),
            _ => (18, "medium"),
        };

        arguments.Add("-r");
        arguments.Add(sequence.FrameRate.ToString());
        switch (options.Format)
        {
            case TimelineExportFormat.WebM:
                arguments.Add("-c:v"); arguments.Add("libvpx-vp9");
                arguments.Add("-crf"); arguments.Add(crf.ToString(CultureInfo.InvariantCulture));
                arguments.Add("-b:v"); arguments.Add("0");
                if (hasAudio) { arguments.Add("-c:a"); arguments.Add("libopus"); arguments.Add("-b:a"); arguments.Add("192k"); }
                break;
            default:
                arguments.Add("-c:v"); arguments.Add(options.HardwareEncoding ? "h264_nvenc" : "libx264");
                arguments.Add("-preset"); arguments.Add(preset);
                arguments.Add("-crf"); arguments.Add(crf.ToString(CultureInfo.InvariantCulture));
                if (hasAudio) { arguments.Add("-c:a"); arguments.Add("aac"); arguments.Add("-b:a"); arguments.Add("192k"); }
                break;
        }
        if (!hasAudio) arguments.Add("-an");
        arguments.Add("-pix_fmt"); arguments.Add("yuv420p");
        arguments.Add("-movflags"); arguments.Add("+faststart");
        arguments.Add(outputPath);
    }

    private static string OffsetTimeExpression(string variable, double offset) =>
        Math.Abs(offset) < 0.000001 ? variable : $"({variable}+{FormatNumber(offset)})";

    private static string MultiplyExpression(string left, string right)
    {
        if (left == "1") return right;
        if (right == "1") return left;
        return $"({left})*({right})";
    }

    private static int EnsureEven(int value) => value % 2 == 0 ? value : value + 1;
    private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static ParsedColor ParseHexColor(string? value, double opacity)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "000000" : value.Trim().TrimStart('#');
        if (raw.Length == 3) raw = string.Concat(raw.Select(character => new string(character, 2)));
        if (raw.Length < 6 || !int.TryParse(raw[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            rgb = 0;
        var red = (rgb >> 16) & 0xFF;
        var green = (rgb >> 8) & 0xFF;
        var blue = rgb & 0xFF;
        var alpha = Math.Clamp(opacity, 0, 1);
        return new ParsedColor(red, green, blue, $"0x{rgb:X6}@{FormatNumber(alpha)}");
    }

    private sealed record VisualEntry(Track Track, TimelineItem Item, string? SourcePath, bool IsImage);
    private sealed record AudioEntry(Track Track, TimelineItem Item, string SourcePath);
    private sealed record VisualInput(string Label, VisualEntry Entry, VisualWindow Window);
    private sealed record AudioInput(string Label, AudioEntry Entry);
    private sealed record TransitionMaps(
        IReadOnlyDictionary<TimelineItemId, TransitionWindow> Left,
        IReadOnlyDictionary<TimelineItemId, TransitionWindow> Right);
    private sealed record TransitionWindow(Transition Transition, double Start, double End, double Duration);
    private sealed record VisualWindow(
        double RenderStart,
        double RenderEnd,
        double RenderDuration,
        double SourceStart,
        double SourceDuration,
        double PaddingAfter,
        TransitionWindow? LeftTransition,
        TransitionWindow? RightTransition);
    private sealed record AnimationSample(double Time, double Value);
    private sealed record ParsedColor(int R, int G, int B, string Ffmpeg);
}
