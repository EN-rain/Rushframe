using Rushframe.Domain;
using Rushframe.Infrastructure;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Rushframe.BatmanTikTokEdit <source.mov> <output-folder> <ffmpeg.exe>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputFolder = Path.GetFullPath(args[1]);
var ffmpegPath = Path.GetFullPath(args[2]);

if (!File.Exists(sourcePath))
    throw new FileNotFoundException("Source clip was not found.", sourcePath);
if (!File.Exists(ffmpegPath))
    throw new FileNotFoundException("FFmpeg executable was not found.", ffmpegPath);

Directory.CreateDirectory(outputFolder);
var projectPath = Path.Combine(outputFolder, "batman_entry_tiktok_edit.rushframe");
var outputPath = Path.Combine(outputFolder, "batman_entry_tiktok_edit.mp4");
var fontPath = ResolveFont();

var project = new Project
{
    Name = "Batman Entry - TikTok Creative Cut",
    CampaignDescription = "Fast comedic TikTok cut: cold-open payoff, rewind, escalating Batman reveal, bold burned-in captions, punch-ins, and a full-frame blurred backdrop.",
};

var sequence = project.MainSequence
    ?? throw new InvalidOperationException("The project did not create a main sequence.");
sequence.Name = "TikTok Main";
sequence.Width = 1080;
sequence.Height = 1920;
sequence.FrameRate = FrameRate.Fps30;
sequence.Background = new CanvasBackground
{
    Kind = CanvasBackgroundKind.LinearGradient,
    PrimaryColor = "#05070B",
    SecondaryColor = "#111827",
    GradientAngleDegrees = 90,
    Opacity = 1,
};
sequence.LayoutGuides.Add(new LayoutGuide
{
    Kind = LayoutGuideKind.TikTok,
    Name = "TikTok safe area",
    Enabled = true,
    Left = 0.06,
    Top = 0.12,
    Right = 0.12,
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
    FileFingerprint = "sha256:5e9fd47dd03b8b466ecf2bfe41918fd7abda987716fce9b91953a7f242910e23",
    Duration = MediaTime.FromSeconds(31.37),
    PixelWidth = 1280,
    PixelHeight = 720,
};
project.MediaLibrary.Add(asset);

var foregroundTrack = new Track
{
    Kind = TrackKind.Video,
    Name = "Story Cut",
    Order = 0,
};
var adjustmentTrack = new Track
{
    Kind = TrackKind.Overlay,
    Name = "Impact FX",
    Order = 1,
};
var textTrack = new Track
{
    Kind = TrackKind.Text,
    Name = "TikTok Captions",
    Order = 2,
};
sequence.Tracks.AddRange([foregroundTrack, adjustmentTrack, textTrack]);

var segments = new[]
{
    new EditSegment("Cold-open payoff", 24.16, 1.88, 0.12),
    new EditSegment("My turn to deal", 0.00, 1.70, 0.05),
    new EditSegment("Shock and chair", 2.44, 2.70, 0.10),
    new EditSegment("Batman calm down", 5.90, 4.74, 0.08),
    new EditSegment("Superhero reveal", 11.10, 3.66, 0.11),
    new EditSegment("Batman in building", 18.54, 3.14, 0.10),
    new EditSegment("Justice payoff", 23.14, 2.90, 0.14),
    new EditSegment("Go fight them", 28.70, 1.66, 0.08),
};

var timelineCursor = 0.0;
foreach (var segment in segments)
{
    foregroundTrack.Items.Add(CreateForegroundClip(asset.Id, timelineCursor, segment));
    timelineCursor += segment.Duration;
}

// A very short impact treatment sells the cold open and the rewind without
// covering the captions that are composited on the later text track.
AddImpact(adjustmentTrack, 0.00, 0.10, flash: 0.22, glitch: 0.20);
AddImpact(adjustmentTrack, 1.76, 0.20, flash: 0.34, glitch: 0.68);
AddImpact(adjustmentTrack, 14.60, 0.16, flash: 0.16, glitch: 0.42);
AddImpact(adjustmentTrack, 17.76, 0.14, flash: 0.20, glitch: 0.30);

AddCaption(textTrack, "BATMAN JOINED\nTHE POKER TABLE", 0.00, 1.76, 84, 74, 112, "#FFD400", fontPath, outline: 9);
AddCaption(textTrack, "I BROUGHT JUSTICE\nTO THE TABLE.", 0.00, 1.88, 74, 1185, 66, "#FFFFFF", fontPath, outline: 9);
AddCaption(textTrack, "15 SECONDS EARLIER...", 1.86, 1.20, 58, 160, 52, "#7DD3FC", fontPath, outline: 7);

AddCaption(textTrack, "MY TURN TO DEAL.", 1.88, 1.70, 72, 1205, 68, "#FFFFFF", fontPath, outline: 9);
AddCaption(textTrack, "OH, S***.", 3.58, 0.62, 110, 1195, 82, "#FFD400", fontPath, outline: 10);
AddCaption(textTrack, "GET OUT OF MY\nF***ING CHAIR.", 4.12, 2.16, 78, 1180, 68, "#FFFFFF", fontPath, outline: 9);

AddCaption(textTrack, "BATMAN, CALM DOWN.", 6.28, 1.92, 65, 1200, 64, "#FFFFFF", fontPath, outline: 9);
AddCaption(textTrack, "CALM DOWN...", 8.20, 2.82, 112, 1205, 78, "#FFD400", fontPath, outline: 10);

AddCaption(textTrack, "MY SUPERHERO\nIS HERE.", 11.02, 1.50, 110, 1180, 72, "#FFFFFF", fontPath, outline: 9);
AddCaption(textTrack, "HAPPY TO SEE\nBATMAN?", 12.52, 2.16, 120, 1180, 72, "#7DD3FC", fontPath, outline: 9);

AddCaption(textTrack, "BATMAN IN\nTHE BUILDING.", 14.68, 1.46, 130, 1180, 76, "#FFD400", fontPath, outline: 10);
AddCaption(textTrack, "I'M TIRED OF\nTHESE DEALERS.", 16.14, 1.68, 115, 1180, 70, "#FFFFFF", fontPath, outline: 9);

AddCaption(textTrack, "IT'S ABOUT TIME.", 17.82, 1.18, 95, 1200, 72, "#7DD3FC", fontPath, outline: 9);
AddCaption(textTrack, "I BROUGHT JUSTICE\nTO THE TABLE.", 19.00, 1.72, 74, 1180, 68, "#FFFFFF", fontPath, outline: 9);
AddCaption(textTrack, "JUSTICE.", 19.38, 0.82, 252, 1430, 112, "#FFD400", fontPath, outline: 11);
AddCaption(textTrack, "GO FIGHT THEM\nFOR ME.", 20.72, 1.66, 165, 1180, 72, "#FFFFFF", fontPath, outline: 9);

project.IncrementRevision();
new ProjectRepository().Save(project, projectPath);

Console.WriteLine($"SOURCE={sourcePath}");
Console.WriteLine($"PROJECT={projectPath}");
Console.WriteLine($"OUTPUT={outputPath}");
Console.WriteLine($"FFMPEG={ffmpegPath}");
Console.WriteLine($"DURATION={sequence.Duration.Seconds:0.###}");
Console.WriteLine($"FONT={(fontPath ?? "FFmpeg default")}");

var media = new FfmpegMediaService(ffmpegPath, null);
var progress = new Progress<MediaJobProgress>(update =>
    Console.WriteLine($"PROGRESS|{update.Percent:0.##}|{update.Message}"));
await media.ExportTimelineAsync(
    project,
    sequence,
    outputPath,
    progress,
    exportOptions: new TimelineExportOptions(
        TimelineExportFormat.Mp4,
        TimelineExportQuality.Standard,
        IncludeAudio: true,
        HardwareEncoding: false));

var result = new FileInfo(outputPath);
Console.WriteLine($"RESULT=PASS|SIZE={result.Length}|PATH={result.FullName}");
return 0;

static TimelineItem CreateForegroundClip(MediaAssetId assetId, double timelineStart, EditSegment segment)
{
    const double baseScale = 0.84375;
    var item = CreateBaseClip(assetId, timelineStart, segment);
    item.Transform.PositionY = -170;
    item.Transform.ScaleX = baseScale;
    item.Transform.ScaleY = baseScale;
    item.ColorCorrection = new ColorCorrection
    {
        Brightness = 0.025,
        Contrast = 0.13,
        Saturation = 1.10,
        Exposure = 0.015,
    };
    item.Effects.Add(new EffectInstance
    {
        EffectTypeId = "vignette",
    });

    var endScale = baseScale + segment.PunchAmount;
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.ScaleX, baseScale, endScale, segment.Duration));
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.ScaleY, baseScale, endScale, segment.Duration));
    item.AnimationChannels.Add(CreateChannel(AnimationPropertyNames.PositionY, -170, -182, segment.Duration));
    return item;
}

static TimelineItem CreateBaseClip(MediaAssetId assetId, double timelineStart, EditSegment segment) => new()
{
    Kind = ItemKind.Clip,
    MediaAssetId = assetId,
    TimelineStart = MediaTime.FromSeconds(timelineStart),
    Duration = MediaTime.FromSeconds(segment.Duration),
    SourceStart = MediaTime.FromSeconds(segment.SourceStart),
    SourceDuration = MediaTime.FromSeconds(segment.Duration),
    Speed = 1,
    Volume = 1,
    Opacity = 1,
};

static AnimationChannel CreateChannel(string propertyName, double from, double to, double duration)
{
    var channel = new AnimationChannel
    {
        PropertyName = propertyName,
        DefaultValue = from,
    };
    channel.Keyframes.Add(new Keyframe
    {
        Time = MediaTime.Zero,
        Value = from,
        Interpolation = InterpolationType.EaseOut,
    });
    channel.Keyframes.Add(new Keyframe
    {
        Time = MediaTime.FromSeconds(duration),
        Value = to,
        Interpolation = InterpolationType.EaseOut,
    });
    return channel;
}

static void AddImpact(Track track, double start, double duration, double flash, double glitch)
{
    var item = new TimelineItem
    {
        Kind = ItemKind.AdjustmentLayer,
        TimelineStart = MediaTime.FromSeconds(start),
        Duration = MediaTime.FromSeconds(duration),
        SourceStart = MediaTime.Zero,
        SourceDuration = MediaTime.FromSeconds(duration),
    };
    if (flash > 0)
    {
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "spark_flash",
            Parameters = { ["intensity"] = flash },
        });
    }
    if (glitch > 0)
    {
        item.Effects.Add(new EffectInstance
        {
            EffectTypeId = "glitch",
            Parameters = { ["intensity"] = glitch },
        });
    }
    track.Items.Add(item);
}

static void AddCaption(
    Track track,
    string text,
    double start,
    double duration,
    double x,
    double y,
    double size,
    string fill,
    string? fontPath,
    double outline)
{
    track.Items.Add(new TimelineItem
    {
        Kind = ItemKind.Text,
        TimelineStart = MediaTime.FromSeconds(start),
        Duration = MediaTime.FromSeconds(duration),
        SourceStart = MediaTime.Zero,
        SourceDuration = MediaTime.FromSeconds(duration),
        TextContent = text,
        FontFamily = fontPath,
        FontSize = size,
        FontBold = true,
        FontAlign = "center",
        FillColor = fill,
        OutlineColor = "#050505",
        OutlineWidth = outline,
        ShadowColor = "#000000",
        ShadowOffsetX = 5,
        ShadowOffsetY = 7,
        ShadowOpacity = 0.82,
        Transform =
        {
            PositionX = x,
            PositionY = y,
        },
        FadeInDuration = MediaTime.FromSeconds(0.06),
        FadeOutDuration = MediaTime.FromSeconds(0.06),
    });
}

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

internal sealed record EditSegment(string Name, double SourceStart, double Duration, double PunchAmount);
