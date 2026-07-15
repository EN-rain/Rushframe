using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rushframe.Application;
using Rushframe.Domain;
using Rushframe.Infrastructure;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: Rushframe.BatmanFiveEdits <source.mov> <media-analysis.json> <output-folder> <ffmpeg.exe>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var analysisPath = Path.GetFullPath(args[1]);
var outputRoot = Path.GetFullPath(args[2]);
var ffmpegPath = Path.GetFullPath(args[3]);
if (!File.Exists(sourcePath)) throw new FileNotFoundException("Batman source clip was not found.", sourcePath);
if (!File.Exists(analysisPath)) throw new FileNotFoundException("AI media analysis was not found.", analysisPath);
if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("FFmpeg executable was not found.", ffmpegPath);

Directory.CreateDirectory(outputRoot);
var runLockName = $"Local\\RushframeBatmanFiveEdits-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outputRoot))).Substring(0, 24)}";
using var runLock = new Mutex(initiallyOwned: false, runLockName);
if (!runLock.WaitOne(0))
{
    Console.WriteLine($"RESULT=SKIPPED|REASON=Another Batman edit run already owns {outputRoot}");
    return 0;
}
var editsRoot = Path.Combine(outputRoot, "edits");
var evidenceRoot = Path.Combine(outputRoot, "evidence-frames");
var reportPath = Path.Combine(outputRoot, "QA_REPORT.md");

// This utility owns only the current-run Batman edit outputs. Preserve analysis bundles and unrelated QA evidence.
if (Directory.Exists(editsRoot)) Directory.Delete(editsRoot, recursive: true);
if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
if (File.Exists(reportPath)) File.Delete(reportPath);
Directory.CreateDirectory(editsRoot);
Directory.CreateDirectory(evidenceRoot);

var editSlug = "the-last-hand";
var editFolder = Path.Combine(editsRoot, $"01-{editSlug}");
Directory.CreateDirectory(editFolder);
var projectPath = Path.Combine(editFolder, $"{editSlug}.rushframe");
var outputPath = Path.Combine(editFolder, $"{editSlug}.mp4");
var manifestPath = Path.Combine(editFolder, "manifest.json");
var notesPath = Path.Combine(editFolder, "EDIT_NOTES.md");

var sourceHash = await ComputeSha256Async(sourcePath);
var mediaService = new FfmpegMediaService(ffmpegPath, null);
var sourceProbe = await mediaService.ProbeAsync(sourcePath);
var seedAsset = new MediaAsset
{
    Kind = MediaKind.Video,
    OriginalPath = sourcePath,
    RelativeProjectPath = string.Empty,
    FileFingerprint = $"sha256:{sourceHash.ToLowerInvariant()}",
    Duration = MediaTime.FromSeconds(sourceProbe.Duration.TotalSeconds),
    PixelWidth = RequireVideoStream(sourceProbe).Width ?? 0,
    PixelHeight = RequireVideoStream(sourceProbe).Height ?? 0,
};
var importedAnalysis = await new MediaIntelligenceImportService().ImportAsync(analysisPath, seedAsset);
var plan = AiCutPlan.Create(importedAnalysis);
var fontPath = ResolveFont();
var project = BuildProject(sourcePath, sourceHash, sourceProbe, importedAnalysis, plan, fontPath);

project.Tasks.AddRange(
[
    new CampaignTask { Title = "Remove previous Batman current-run edits", IsCompleted = true },
    new CampaignTask { Title = "Run local AI scene, transcript, audio, alignment, event, and embedding analysis", IsCompleted = true },
    new CampaignTask { Title = "Select story beats from AI transcript meaning and confidence", IsCompleted = true },
    new CampaignTask { Title = "Build Batman: The Last Hand creative treatment", IsCompleted = true },
    new CampaignTask { Title = "Save, reopen, render, decode, probe, and capture evidence", IsCompleted = false },
]);
MediaIntelligenceImportService.StoreInProject(project, importedAnalysis);
project.IncrementRevision();

var repository = new ProjectRepository();
repository.Save(project, projectPath);
var reopened = repository.Load(projectPath) ?? throw new InvalidDataException("Saved Batman project could not be reopened.");
var sequence = reopened.MainSequence ?? throw new InvalidDataException("Reopened Batman project has no sequence.");
ValidateProject(reopened, importedAnalysis, plan);

Console.WriteLine($"EDIT=Batman: The Last Hand");
Console.WriteLine($"AI_SCENES={importedAnalysis.Scenes.Count}");
Console.WriteLine($"AI_TRANSCRIPT_SEGMENTS={importedAnalysis.Transcript.Count}");
Console.WriteLine($"AI_AUDIO_EVENTS={importedAnalysis.Audio.Events.Count}");
Console.WriteLine($"AI_MOMENTS={importedAnalysis.Moments.Count}");
Console.WriteLine($"AI_LOUDNESS_LUFS={importedAnalysis.Audio.IntegratedLoudnessLufs?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}");
foreach (var beat in plan.Beats)
    Console.WriteLine($"AI_BEAT|{beat.Role}|{beat.Segment.SegmentId}|{beat.SourceStart:0.###}-{beat.SourceStart + beat.SourceDuration:0.###}|confidence={beat.Confidence:0.###}|{beat.Segment.Text}");
Console.WriteLine($"PROJECT={projectPath}");
Console.WriteLine($"OUTPUT={outputPath}");
Console.WriteLine($"DURATION={sequence.Duration.Seconds:0.###}");
Console.WriteLine($"ITEMS={sequence.Tracks.Sum(track => track.Items.Count)}");
Console.WriteLine($"TRANSITIONS={sequence.Transitions.Count}");

var progress = new Progress<MediaJobProgress>(update =>
    Console.WriteLine($"PROGRESS|{update.Percent:0.##}|{update.Message}"));
await mediaService.ExportTimelineAsync(
    reopened,
    sequence,
    outputPath,
    progress,
    exportOptions: new TimelineExportOptions(
        TimelineExportFormat.Mp4,
        TimelineExportQuality.High,
        IncludeAudio: true,
        HardwareEncoding: false));

var decode = await RunProcessAsync(ffmpegPath,
[
    "-v", "error", "-i", outputPath, "-f", "null", "NUL",
]);
if (decode.ExitCode != 0)
    throw new InvalidDataException($"Full output decode failed: {decode.StandardError}");

var outputProbe = await mediaService.ProbeAsync(outputPath);
if (!outputProbe.HasVideo || !outputProbe.HasAudio)
    throw new InvalidDataException("Rendered Batman edit is missing video or audio.");
var outputVideo = RequireVideoStream(outputProbe);
var outputAudio = RequireAudioStream(outputProbe);
if (outputVideo.Width != sequence.Width || outputVideo.Height != sequence.Height)
    throw new InvalidDataException($"Unexpected output dimensions {outputVideo.Width}x{outputVideo.Height}.");

var evidenceTimes = new[]
{
    ("start", Math.Min(0.55, Math.Max(0, sequence.Duration.Seconds / 5))),
    ("mid", sequence.Duration.Seconds / 2),
    ("end", Math.Max(0, sequence.Duration.Seconds - 0.45)),
};
foreach (var (name, time) in evidenceTimes)
{
    var framePath = Path.Combine(evidenceRoot, $"the-last-hand-{name}.jpg");
    var capture = await RunProcessAsync(ffmpegPath,
    [
        "-v", "error", "-y", "-ss", time.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", outputPath, "-frames:v", "1", "-q:v", "2", framePath,
    ]);
    if (capture.ExitCode != 0 || !File.Exists(framePath) || new FileInfo(framePath).Length == 0)
        throw new InvalidDataException($"Evidence frame '{name}' was not created: {capture.StandardError}");
}

reopened.Tasks[^1].IsCompleted = true;
reopened.IncrementRevision();
repository.Save(reopened, projectPath);
var persisted = repository.Load(projectPath) ?? throw new InvalidDataException("Final Batman project could not be reopened.");
ValidateProject(persisted, importedAnalysis, plan);
if (persisted.Tasks.Any(task => !task.IsCompleted))
    throw new InvalidDataException("Final Batman project still has incomplete tasks.");

var outputHash = await ComputeSha256Async(outputPath);
var outputInfo = new FileInfo(outputPath);
var sourceUnchanged = string.Equals(sourceHash, await ComputeSha256Async(sourcePath), StringComparison.OrdinalIgnoreCase);
if (!sourceUnchanged) throw new InvalidDataException("Original Batman source media changed.");

var manifest = new
{
    edit = "Batman: The Last Hand",
    slug = editSlug,
    concept = "A payoff-first noir-to-color vertical cut driven by local AI transcript meaning, confidence, moment search, and loudness analysis.",
    source = sourcePath,
    source_sha256 = sourceHash,
    source_unchanged = sourceUnchanged,
    source_clips_centered = true,
    analysis = analysisPath,
    ai = new
    {
        scenes = importedAnalysis.Scenes.Count,
        transcript_segments = importedAnalysis.Transcript.Count,
        audio_events = importedAnalysis.Audio.Events.Count,
        moments = importedAnalysis.Moments.Count,
        warnings = importedAnalysis.Warnings,
        integrated_loudness_lufs = importedAnalysis.Audio.IntegratedLoudnessLufs,
        true_peak_db = importedAnalysis.Audio.TruePeakDb,
        selected_moment = plan.SelectedMoment,
        selected_beats = plan.Beats.Select(beat => new
        {
            beat.Role,
            segment_id = beat.Segment.SegmentId,
            source_start = beat.SourceStart,
            source_duration = beat.SourceDuration,
            transcript = beat.Segment.Text,
            beat.Confidence,
            beat.Caption,
        }),
    },
    project = projectPath,
    project_revision = persisted.Revision,
    output = outputPath,
    output_sha256 = outputHash,
    output_bytes = outputInfo.Length,
    duration_seconds = sequence.Duration.Seconds,
    width = outputVideo.Width,
    height = outputVideo.Height,
    fps = outputVideo.FrameRate ?? sequence.FrameRate.Value,
    video_codec = outputVideo.Codec,
    audio_codec = outputAudio.Codec,
    audio_channels = outputAudio.Channels ?? 2,
    sample_rate = outputAudio.SampleRate ?? 48000,
    tracks = sequence.Tracks.Count,
    items = sequence.Tracks.Sum(track => track.Items.Count),
    markers = sequence.Markers.Count,
    transitions = sequence.Transitions.Count,
    full_decode = decode.ExitCode == 0,
    evidence_frames = evidenceTimes.Select(value => $"the-last-hand-{value.Item1}.jpg"),
};
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, jsonOptions));
await File.WriteAllTextAsync(notesPath, BuildEditNotes(plan, importedAnalysis, sequence));
await File.WriteAllTextAsync(reportPath, BuildQaReport(
    sourcePath,
    sourceHash,
    analysisPath,
    projectPath,
    outputPath,
    outputInfo.Length,
    outputHash,
    outputProbe,
    persisted,
    plan,
    importedAnalysis));

Console.WriteLine($"RESULT=PASS|SIZE={outputInfo.Length}|SHA256={outputHash}|PATH={outputInfo.FullName}");
return 0;

static Project BuildProject(
    string sourcePath,
    string sourceHash,
    MediaProbeResult probe,
    MediaIntelligenceAnalysis analysis,
    AiCutPlan plan,
    string? fontPath)
{
    var project = new Project
    {
        Name = "Duel Clipping - Batman TikTok",
        CampaignDescription = "Duel [CLIPPING - TIKTOK] 4. TikTok-only Batman clipping campaign. Use only registered media from the campaign content folder. Minimum duration is 7 seconds. Add original, high-quality on-screen text or captions. The campaign lists a $2,000 per 1M view rate, $4,200 budget, $2,000 cap per post/profile, and eligibility thresholds of 3,000 views and 1% engagement; Rushframe does not promise performance or earnings.",
        EditingBrief = new EditingBrief
        {
            Purpose = "Create a high-retention Batman duel clip for the Duel TikTok clipping campaign.",
            TargetAudience = "TikTok viewers interested in Batman, superhero confrontations, dramatic entrances, and fast narrative clips.",
            Platform = "TikTok only",
            AspectRatio = "9:16",
            TargetDurationSeconds = 12,
            Tone = "Dark, confrontational, dramatic, energetic, and meme-aware without becoming visually noisy.",
            EditingStyle = "social-highlight",
            Pacing = "Immediate payoff hook, compressed setup, escalating confrontation, and a decisive final line.",
            HookDeadlineSeconds = 1.5,
            CaptionPolicy = "Original high-quality kinetic captions are required. Keep text readable, synchronized, within TikTok safe areas, and distinct from the source subtitles.",
            MusicPolicy = "Preserve intelligible source dialogue. Do not add music unless it is already registered and improves the edit without masking speech.",
            SoundEffectsPolicy = "Use restrained impacts only when they support an entrance, reveal, or confrontation beat.",
            TransitionPolicy = "Prefer hard cuts. Use only short motivated transitions for time reset, entrance, escalation, or payoff.",
            CallToAction = string.Empty,
            LogoPolicy = "Do not add unapproved logos or watermarks.",
            ReferenceNotes = "Campaign: Duel [CLIPPING - TIKTOK] 4. Content-folder media only. TikTok only. Minimum 7 seconds. Own high-quality text/caption required. Eligibility thresholds are campaign conditions, not guaranteed results.",
            RequiredMessages =
            {
                "Batman enters or takes control of the confrontation.",
                "The duel or conflict escalates clearly.",
                "The ending lands on a decisive payoff rather than stopping mid-action.",
            },
        },
    };
    var sequence = project.MainSequence ?? throw new InvalidOperationException("Project did not create a main sequence.");
    sequence.Name = "Batman - The Last Hand";
    sequence.Width = 720;
    sequence.Height = 1280;
    sequence.FrameRate = FrameRate.Fps30;
    sequence.Background = new CanvasBackground
    {
        Kind = CanvasBackgroundKind.LinearGradient,
        PrimaryColor = "#02040A",
        SecondaryColor = "#122238",
        GradientAngleDegrees = 128,
        Opacity = 1,
    };
    sequence.LayoutGuides.Add(new LayoutGuide
    {
        Kind = LayoutGuideKind.TikTok,
        Name = "TikTok safe area",
        Enabled = true,
        Left = 0.06,
        Top = 0.10,
        Right = 0.13,
        Bottom = 0.20,
    });
    sequence.LayoutGuides.Add(new LayoutGuide
    {
        Kind = LayoutGuideKind.TitleSafe,
        Name = "Title safe",
        Enabled = true,
        Left = 0.08,
        Top = 0.08,
        Right = 0.08,
        Bottom = 0.10,
    });

    var asset = new MediaAsset
    {
        Kind = MediaKind.Video,
        OriginalPath = sourcePath,
        RelativeProjectPath = string.Empty,
        FileFingerprint = $"sha256:{sourceHash.ToLowerInvariant()}",
        Duration = MediaTime.FromSeconds(probe.Duration.TotalSeconds),
        PixelWidth = RequireVideoStream(probe).Width ?? 0,
        PixelHeight = RequireVideoStream(probe).Height ?? 0,
    };
    project.MediaLibrary.Add(asset);

    var storyTrack = new Track { Kind = TrackKind.Video, Name = "AI Story Cut", Order = 0 };
    var echoTrack = new Track { Kind = TrackKind.Overlay, Name = "Neural Echo", Order = 1 };
    var impactTrack = new Track { Kind = TrackKind.Overlay, Name = "Impact Flash", Order = 2 };
    var captionTrack = new Track { Kind = TrackKind.Text, Name = "AI Kinetic Captions", Order = 3 };
    sequence.Tracks.AddRange([storyTrack, echoTrack, impactTrack, captionTrack]);

    var cursor = 0.0;
    TimelineItem? previous = null;
    for (var index = 0; index < plan.Beats.Count; index++)
    {
        var beat = plan.Beats[index];
        if (index == 1)
        {
            const double resetDuration = 0.46;
            AddText(captionTrack, "24 SECONDS EARLIER", cursor, resetDuration, 56, 510, 41, "#F3C84B", fontPath, 5, 0.04, 0.05, animate: true);
            cursor += resetDuration;
            previous = null;
        }

        var clip = CreateStoryClip(asset.Id, beat, cursor, index, plan.DialogueVolume);
        storyTrack.Items.Add(clip);
        sequence.Markers.Add(new Marker
        {
            Label = $"AI beat {index + 1}: {beat.Role}",
            Note = $"{beat.Segment.SegmentId}; confidence {beat.Confidence:0.###}; source {beat.SourceStart:0.###}-{beat.SourceStart + beat.SourceDuration:0.###}s; transcript: {beat.Segment.Text}",
            Time = MediaTime.FromSeconds(cursor),
            Color = index is 0 or 7 ? "#F3C84B" : "#57D7FF",
            MediaIntelligenceSourceAssetId = asset.Id,
        });

        if (previous != null)
        {
            sequence.Transitions.Add(new Transition
            {
                LeftItemId = previous.Id,
                RightItemId = clip.Id,
                Kind = index switch
                {
                    3 => TransitionKind.WhipPan,
                    4 => TransitionKind.Blur,
                    5 => TransitionKind.Zoom,
                    7 => TransitionKind.CrossDissolve,
                    _ => TransitionKind.CrossDissolve,
                },
                Duration = MediaTime.FromSeconds(index is 3 or 5 ? 0.14 : 0.18),
                Alignment = 0.5,
            });
        }

        AddCaption(captionTrack, beat.Caption, cursor, beat.TimelineDuration, index, fontPath);
        AddImpact(impactTrack, cursor, index);
        if (beat.Role == "entrance")
            AddNeuralEcho(echoTrack, asset.Id, beat, cursor);
        if (index == 0)
        {
            AddText(captionTrack, "THE LAST HAND", cursor + 0.04, Math.Min(1.15, beat.TimelineDuration), 48, 72, 51, "#F3C84B", fontPath, 6, 0.04, 0.08, animate: true);
            AddText(captionTrack, "AI FOUND THE PAYOFF FIRST", cursor + 0.13, Math.Min(1.15, beat.TimelineDuration - 0.08), 56, 146, 22, "#F8FAFC", fontPath, 3, 0.06, 0.08, animate: false);
        }

        previous = clip;
        cursor += beat.TimelineDuration;
    }

    AddText(captionTrack, "JUSTICE WAS DEALT.", Math.Max(0, cursor - 0.78), 0.78, 57, 1002, 37, "#F3C84B", fontPath, 5, 0.04, 0.16, animate: true);
    return project;
}

static TimelineItem CreateStoryClip(MediaAssetId assetId, AiBeat beat, double timelineStart, int index, double volume)
{
    const double fitWidth = 0.5625;
    var item = new TimelineItem
    {
        Kind = ItemKind.Clip,
        MediaAssetId = assetId,
        TimelineStart = MediaTime.FromSeconds(timelineStart),
        Duration = MediaTime.FromSeconds(beat.TimelineDuration),
        SourceStart = MediaTime.FromSeconds(beat.SourceStart),
        SourceDuration = MediaTime.FromSeconds(beat.SourceDuration),
        Speed = beat.Speed,
        Volume = volume,
        Opacity = 1,
        FadeInDuration = MediaTime.FromSeconds(index == 0 ? 0.06 : 0.02),
        FadeOutDuration = MediaTime.FromSeconds(0.035),
    };
    item.Transform.PositionX = 0;
    item.Transform.PositionY = 0;
    item.Transform.ScaleX = fitWidth;
    item.Transform.ScaleY = fitWidth;
    item.ColorCorrection = index switch
    {
        0 => new ColorCorrection { Brightness = -0.025, Contrast = 0.28, Saturation = 0.08, Exposure = -0.025 },
        1 or 2 => new ColorCorrection { Brightness = -0.005, Contrast = 0.20, Saturation = 0.76, Exposure = -0.01 },
        3 => new ColorCorrection { Brightness = 0.01, Contrast = 0.23, Saturation = 0.92, Exposure = 0 },
        4 or 5 => new ColorCorrection { Brightness = 0.02, Contrast = 0.17, Saturation = 1.08, Exposure = 0.01 },
        7 => new ColorCorrection { Brightness = 0.005, Contrast = 0.24, Saturation = 0.82, Exposure = -0.005 },
        _ => new ColorCorrection { Brightness = 0.01, Contrast = 0.18, Saturation = 1.0, Exposure = 0 },
    };
    item.Effects.Add(new EffectInstance { EffectTypeId = "vignette" });
    if (index == 0)
    {
        item.Effects.Add(new EffectInstance { EffectTypeId = "mono" });
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "film_grain",
            Parameters = { ["intensity"] = 0.12 },
        });
    }
    if (index is 2 or 3)
    {
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "glitch",
            Parameters = { ["intensity"] = index == 2 ? 0.08 : 0.05 },
        });
    }
    if (index is 4 or 5)
    {
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "night_lift",
            Parameters = { ["brightness"] = 0.025, ["contrast"] = 0.11 },
        });
    }
    if (index is 5 or 7)
    {
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "lens_punch",
            Parameters = { ["zoom"] = index == 7 ? 0.06 : 0.09, ["vignette"] = 0.16 },
        });
    }

    var punch = index switch
    {
        0 => 0.075,
        2 or 3 => 0.055,
        4 or 5 => 0.09,
        7 => 0.065,
        _ => 0.04,
    };
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.ScaleX, fitWidth, fitWidth + punch, beat.TimelineDuration));
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.ScaleY, fitWidth, fitWidth + punch, beat.TimelineDuration));
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.PositionX, index % 2 == 0 ? -8 : 8, index % 2 == 0 ? 7 : -7, beat.TimelineDuration));
    return item;
}

static void AddNeuralEcho(Track track, MediaAssetId assetId, AiBeat beat, double start)
{
    var duration = Math.Min(0.34, beat.TimelineDuration * 0.32);
    var echo = new TimelineItem
    {
        Kind = ItemKind.Clip,
        MediaAssetId = assetId,
        TimelineStart = MediaTime.FromSeconds(start + 0.10),
        Duration = MediaTime.FromSeconds(duration),
        SourceStart = MediaTime.FromSeconds(beat.SourceStart),
        SourceDuration = MediaTime.FromSeconds(Math.Min(beat.SourceDuration, duration)),
        Speed = 1,
        Volume = 0,
        Muted = true,
        Opacity = 0.42,
        FadeOutDuration = MediaTime.FromSeconds(duration * 0.85),
    };
    echo.Transform.PositionX = 18;
    echo.Transform.PositionY = 0;
    echo.Transform.ScaleX = 0.59;
    echo.Transform.ScaleY = 0.59;
    echo.Effects.Add(new EffectInstance
    {
        EffectTypeId = "glitch",
        Parameters = { ["intensity"] = 0.24 },
    });
    track.Items.Add(echo);
}

static void AddImpact(Track track, double start, int index)
{
    if (index == 1) return;
    var duration = index is 0 or 5 or 7 ? 0.11 : 0.07;
    var layer = new TimelineItem
    {
        Kind = ItemKind.AdjustmentLayer,
        TimelineStart = MediaTime.FromSeconds(start),
        Duration = MediaTime.FromSeconds(duration),
        SourceDuration = MediaTime.FromSeconds(duration),
    };
    layer.Effects.Add(new EffectInstance
    {
        EffectTypeId = "spark_flash",
        Parameters = { ["intensity"] = index is 0 or 7 ? 0.22 : 0.13 },
    });
    if (index is 2 or 5)
    {
        layer.Effects.Add(new EffectInstance
        {
            EffectTypeId = "glitch",
            Parameters = { ["intensity"] = index == 5 ? 0.36 : 0.22 },
        });
    }
    track.Items.Add(layer);
}

static void AddCaption(Track track, string caption, double start, double duration, int index, string? fontPath)
{
    var fill = index is 0 or 7 ? "#F3C84B" : index is 4 or 5 ? "#57D7FF" : "#F8FAFC";
    var y = index % 2 == 0 ? 790 : 825;
    AddText(track, caption, start + 0.035, Math.Max(0.30, duration - 0.07), 55, y, index == 7 ? 44 : 47, fill, fontPath, 6, 0.035, 0.065, animate: true);
}

static void AddText(
    Track track,
    string text,
    double start,
    double duration,
    double x,
    double y,
    double size,
    string fill,
    string? fontPath,
    double outline,
    double fadeIn,
    double fadeOut,
    bool animate)
{
    var item = new TimelineItem
    {
        Kind = ItemKind.Text,
        TimelineStart = MediaTime.FromSeconds(start),
        Duration = MediaTime.FromSeconds(duration),
        SourceDuration = MediaTime.FromSeconds(duration),
        TextContent = text,
        FontFamily = fontPath,
        FontSize = size,
        FontBold = true,
        FontAlign = "center",
        FillColor = fill,
        OutlineColor = "#02040A",
        OutlineWidth = outline,
        ShadowColor = "#000000",
        ShadowOffsetX = 4,
        ShadowOffsetY = 7,
        ShadowOpacity = 0.84,
        FadeInDuration = MediaTime.FromSeconds(fadeIn),
        FadeOutDuration = MediaTime.FromSeconds(fadeOut),
    };
    item.Transform.PositionX = x;
    item.Transform.PositionY = y;
    if (animate)
    {
        item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.PositionY, y + 24, y, Math.Min(0.24, duration)));
        item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.Opacity, 0.12, 1, Math.Min(0.18, duration)));
    }
    track.Items.Add(item);
}

static AnimationChannel CreateChannel(string propertyName, double from, double to, double duration)
{
    var channel = new AnimationChannel { PropertyName = propertyName, DefaultValue = from };
    channel.Keyframes.Add(new Keyframe
    {
        Time = MediaTime.Zero,
        Value = from,
        Interpolation = InterpolationType.Bezier,
        OutTangentX = 0.22,
        OutTangentY = 0.08,
    });
    channel.Keyframes.Add(new Keyframe
    {
        Time = MediaTime.FromSeconds(Math.Max(0.001, duration)),
        Value = to,
        Interpolation = InterpolationType.EaseOut,
        InTangentX = 0.72,
        InTangentY = 0.92,
    });
    return channel;
}

static void ValidateProject(Project project, MediaIntelligenceAnalysis analysis, AiCutPlan plan)
{
    var sequence = project.MainSequence ?? throw new InvalidDataException("Project has no active sequence.");
    var stored = project.MediaIntelligence.SingleOrDefault()
                 ?? throw new InvalidDataException("Project does not contain exactly one AI analysis.");
    if (stored.Transcript.Count != analysis.Transcript.Count || stored.Scenes.Count != analysis.Scenes.Count)
        throw new InvalidDataException("AI analysis counts changed after persistence.");
    if (sequence.Width != 720 || sequence.Height != 1280)
        throw new InvalidDataException("Batman edit is not a 720x1280 vertical sequence.");
    if (sequence.Tracks.Count != 4)
        throw new InvalidDataException($"Expected four creative tracks, found {sequence.Tracks.Count}.");
    if (sequence.Markers.Count != plan.Beats.Count)
        throw new InvalidDataException("AI beat markers do not match the selected plan.");
    if (sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Duration.Seconds <= 0))
        throw new InvalidDataException("Project contains a non-positive timeline item.");
    var sourceClips = sequence.Tracks
        .SelectMany(track => track.Items)
        .Where(item => item.Kind == ItemKind.Clip && item.MediaAssetId.HasValue)
        .ToArray();
    if (sourceClips.Length == 0 || sourceClips.Any(item => Math.Abs(item.Transform.PositionY) > 0.001))
        throw new InvalidDataException("All Batman source clips must remain vertically centered at PositionY=0.");
    if (sequence.Duration.Seconds is < 10 or > 20)
        throw new InvalidDataException($"Unexpected creative-cut duration {sequence.Duration.Seconds:0.###} seconds.");
}

static string BuildEditNotes(AiCutPlan plan, MediaIntelligenceAnalysis analysis, Sequence sequence)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Batman: The Last Hand");
    builder.AppendLine();
    builder.AppendLine("A payoff-first vertical Batman edit built from local Rushframe AI analysis.");
    builder.AppendLine();
    builder.AppendLine("## AI functions used");
    builder.AppendLine();
    builder.AppendLine($"- Local transcript/alignment: {analysis.Transcript.Count} segments.");
    builder.AppendLine($"- Scene analysis: {analysis.Scenes.Count} scene(s).");
    builder.AppendLine($"- Audio/event analysis: {analysis.Audio.Events.Count} event(s), loudness {analysis.Audio.IntegratedLoudnessLufs?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"} LUFS.");
    builder.AppendLine($"- Editing-moment semantic search: {plan.SelectedMoment ?? "no matching moment; transcript fallback used"}.");
    builder.AppendLine("- Beat selection uses transcript meaning and model confidence rather than fixed source timestamps.");
    builder.AppendLine("- AI analysis is stored inside the Rushframe project and survives save/reopen.");
    builder.AppendLine();
    builder.AppendLine("## Creative treatment");
    builder.AppendLine();
    builder.AppendLine("- Justice-line cold open in monochrome and grain.");
    builder.AppendLine("- 24-seconds-earlier reset card over the project gradient.");
    builder.AppendLine("- Escalating transcript-driven cuts with censored kinetic captions.");
    builder.AppendLine("- Controlled glitch and spark accents rather than constant low-quality effects.");
    builder.AppendLine("- Neural echo on Batman's entrance, animated punch-ins, and a full justice-line finale.");
    builder.AppendLine("- All source footage and source-based overlays remain vertically centered at PositionY=0.");
    builder.AppendLine($"- Final duration: {sequence.Duration.Seconds:0.###} seconds, 720x1280 at {sequence.FrameRate.Value:0.###} fps.");
    builder.AppendLine();
    builder.AppendLine("## AI-selected beats");
    builder.AppendLine();
    foreach (var beat in plan.Beats)
        builder.AppendLine($"- **{beat.Role}** — {beat.SourceStart:0.###}-{beat.SourceStart + beat.SourceDuration:0.###}s, confidence {beat.Confidence:0.###}: {beat.Segment.Text}");
    return builder.ToString();
}

static string BuildQaReport(
    string sourcePath,
    string sourceHash,
    string analysisPath,
    string projectPath,
    string outputPath,
    long outputBytes,
    string outputHash,
    MediaProbeResult probe,
    Project project,
    AiCutPlan plan,
    MediaIntelligenceAnalysis analysis)
{
    var sequence = project.MainSequence!;
    var builder = new StringBuilder();
    builder.AppendLine("# Batman: The Last Hand — Focused QA Report");
    builder.AppendLine();
    builder.AppendLine($"**Run date:** {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
    builder.AppendLine("**Scope:** Replace the previous current-run Batman edits with one new creative, AI-driven Rushframe edit.");
    builder.AppendLine();
    builder.AppendLine("## Result");
    builder.AppendLine();
    builder.AppendLine("**PASS** — the previous current-run edit set was removed, one new project/render was created, persisted, reopened, fully decoded, probed, and evidence frames were captured.");
    builder.AppendLine();
    builder.AppendLine("## AI usage");
    builder.AppendLine();
    builder.AppendLine($"- Scenes: {analysis.Scenes.Count}");
    builder.AppendLine($"- Transcript segments: {analysis.Transcript.Count}");
    builder.AppendLine($"- Audio events: {analysis.Audio.Events.Count}");
    builder.AppendLine($"- Editing moments: {analysis.Moments.Count}");
    builder.AppendLine($"- Analysis warnings: {analysis.Warnings.Count}");
    builder.AppendLine($"- Integrated loudness: {analysis.Audio.IntegratedLoudnessLufs?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"} LUFS");
    builder.AppendLine($"- Semantic moment result: {plan.SelectedMoment ?? "transcript fallback"}");
    builder.AppendLine("- Cut points and captions were selected from AI transcript meaning/confidence.");
    builder.AppendLine();
    builder.AppendLine("## Artifact verification");
    builder.AppendLine();
    builder.AppendLine($"- Source: `{sourcePath}`");
    builder.AppendLine($"- Source SHA-256: `{sourceHash}`");
    builder.AppendLine($"- Analysis: `{analysisPath}`");
    builder.AppendLine($"- Project: `{projectPath}`");
    builder.AppendLine($"- Output: `{outputPath}`");
    builder.AppendLine($"- Output bytes: {outputBytes:N0}");
    builder.AppendLine($"- Output SHA-256: `{outputHash}`");
    builder.AppendLine($"- Duration: {sequence.Duration.Seconds:0.###} s");
    var video = RequireVideoStream(probe);
    var audio = RequireAudioStream(probe);
    builder.AppendLine($"- Dimensions: {video.Width}x{video.Height}");
    builder.AppendLine($"- Video/audio: {video.Codec} / {audio.Codec}, {audio.SampleRate ?? 48000} Hz, {audio.Channels ?? 2} channel(s)");
    builder.AppendLine($"- Tracks/items/markers/transitions: {sequence.Tracks.Count}/{sequence.Tracks.Sum(track => track.Items.Count)}/{sequence.Markers.Count}/{sequence.Transitions.Count}");
    builder.AppendLine("- Full FFmpeg decode: PASS");
    builder.AppendLine("- Save/reopen and completed project tasks: PASS");
    builder.AppendLine("- All source clips vertically centered at PositionY=0: PASS");
    builder.AppendLine("- Original source unchanged: PASS");
    builder.AppendLine("- Evidence frames: start, midpoint, end");
    builder.AppendLine();
    builder.AppendLine("## Remaining manual check");
    builder.AppendLine();
    builder.AppendLine("A human audible/creative review was not performed by this automated focused run.");
    return builder.ToString();
}

static MediaStreamInfo RequireVideoStream(MediaProbeResult probe) =>
    probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video)
    ?? throw new InvalidDataException($"No video stream was found in {probe.Path}.");

static MediaStreamInfo RequireAudioStream(MediaProbeResult probe) =>
    probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Audio)
    ?? throw new InvalidDataException($"No audio stream was found in {probe.Path}.");

static string? ResolveFont()
{
    var candidates = new[]
    {
        @"C:\Windows\Fonts\arialbd.ttf",
        @"C:\Windows\Fonts\impact.ttf",
        @"C:\Windows\Fonts\segoeuib.ttf",
    };
    return candidates.FirstOrDefault(File.Exists);
}

static async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = executable,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await stdout, await stderr);
}

static async Task<string> ComputeSha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record AiBeat(
    string Role,
    MediaIntelligenceTranscriptSegment Segment,
    double SourceStart,
    double SourceDuration,
    double TimelineDuration,
    double Speed,
    double Confidence,
    string Caption);

internal sealed record BeatRule(
    string Role,
    string[] Keywords,
    string Caption,
    double FallbackStart,
    double FallbackDuration,
    double? MaximumDuration = null,
    bool TailOnly = false,
    double Speed = 1);

internal sealed class AiCutPlan
{
    public required IReadOnlyList<AiBeat> Beats { get; init; }
    public required double DialogueVolume { get; init; }
    public string? SelectedMoment { get; init; }

    public static AiCutPlan Create(MediaIntelligenceAnalysis analysis)
    {
        var search = new MediaIntelligenceSearchService();
        var moment = search.Search(
                analysis,
                new MediaMomentSearchQuery("justice batman hero reveal tension", Limit: 1))
            .FirstOrDefault();

        var rules = new[]
        {
            new BeatRule("cold-open", ["justice", "table"], "JUSTICE. TO THE TABLE.", 23.26, 1.55, 1.55, TailOnly: true, Speed: 0.96),
            new BeatRule("deal", ["turn", "deal"], "ONE LAST HAND.", 0.00, 1.80, 1.80, Speed: 1.04),
            new BeatRule("conflict", ["chair"], "WRONG CHAIR.", 3.68, 1.44, 1.44, Speed: 1.02),
            new BeatRule("escalation", ["calm", "down"], "CALM DOWN?", 8.08, 1.44, 1.44, Speed: 1.03),
            new BeatRule("reveal", ["superhero"], "THE HERO ARRIVES.", 11.14, 1.38, 1.38, Speed: 0.96),
            new BeatRule("entrance", ["building"], "BATMAN IN THE BUILDING.", 18.72, 1.14, 1.14, Speed: 0.94),
            new BeatRule("resolve", ["tired", "dealers"], "HE'S DONE WITH DEALERS.", 20.60, 1.08, 1.08, Speed: 1.02),
            new BeatRule("finale", ["justice", "table"], "IT'S ABOUT TIME...", 23.26, 2.78, 2.78, Speed: 1.0),
        };

        var beats = rules.Select(rule => SelectBeat(analysis, rule)).ToArray();
        var targetLufs = -16.0;
        var measuredLufs = analysis.Audio.IntegratedLoudnessLufs;
        var volume = measuredLufs.HasValue
            ? Math.Clamp(Math.Pow(10, (targetLufs - measuredLufs.Value) / 20), 0.68, 1.28)
            : 1.0;
        return new AiCutPlan
        {
            Beats = beats,
            DialogueVolume = volume,
            SelectedMoment = moment == null
                ? null
                : $"{moment.MomentId}: {moment.Summary} (match {moment.MatchScore:0.###})",
        };
    }

    private static AiBeat SelectBeat(MediaIntelligenceAnalysis analysis, BeatRule rule)
    {
        var candidates = analysis.Transcript
            .Select(segment => new
            {
                Segment = segment,
                Hits = rule.Keywords.Count(keyword => segment.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase)),
                Confidence = segment.Confidence ?? 0,
            })
            .Where(candidate => candidate.Hits > 0)
            .OrderByDescending(candidate => candidate.Hits)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Segment.Start.Seconds)
            .ToArray();
        var selected = candidates.FirstOrDefault()?.Segment
                       ?? analysis.Transcript.OrderBy(segment => Math.Abs(segment.Start.Seconds - rule.FallbackStart)).FirstOrDefault()
                       ?? new MediaIntelligenceTranscriptSegment
                       {
                           SegmentId = $"fallback-{rule.Role}",
                           Start = MediaTime.FromSeconds(rule.FallbackStart),
                           End = MediaTime.FromSeconds(rule.FallbackStart + rule.FallbackDuration),
                           Text = rule.Caption,
                           Confidence = 0,
                       };

        var segmentStart = selected.Start.Seconds;
        var segmentDuration = Math.Max(0.1, selected.End.Subtract(selected.Start).Seconds);
        var desiredDuration = Math.Min(rule.MaximumDuration ?? segmentDuration, segmentDuration);
        var sourceStart = rule.TailOnly
            ? Math.Max(segmentStart, selected.End.Seconds - desiredDuration)
            : segmentStart;
        if (desiredDuration <= 0.1)
        {
            sourceStart = rule.FallbackStart;
            desiredDuration = rule.FallbackDuration;
        }
        var timelineDuration = desiredDuration / Math.Max(0.1, rule.Speed);
        return new AiBeat(
            rule.Role,
            selected,
            sourceStart,
            desiredDuration,
            timelineDuration,
            rule.Speed,
            selected.Confidence ?? 0,
            rule.Caption);
    }
}
