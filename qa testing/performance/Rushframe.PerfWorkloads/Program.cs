using Rushframe.Domain;
using Rushframe.Infrastructure;

var outputDirectory = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Path.Combine(Environment.CurrentDirectory, "generated"));
var videoPath = ResolveMedia(args.ElementAtOrDefault(1), "samplevid.mp4");
var audioPath = ResolveMedia(args.ElementAtOrDefault(2), "samplevid_audio.wav");
Directory.CreateDirectory(outputDirectory);

var repository = new ProjectRepository();
var workloads = new[]
{
    BuildTimelineProject("perf-small", 50, 4, videoPath),
    BuildTimelineProject("perf-medium", 500, 12, videoPath),
    BuildTimelineProject("perf-large", 1200, 20, videoPath),
    BuildAnimationProject(videoPath),
    BuildAudioProject(audioPath),
    BuildExactPreviewProject(videoPath),
};

foreach (var workload in workloads)
{
    var path = Path.Combine(outputDirectory, $"{workload.Name}.rushframe");
    repository.Save(workload, path);
    var sequence = workload.MainSequence!;
    var itemCount = sequence.Tracks.Sum(track => track.Items.Count);
    Console.WriteLine($"CREATED|{workload.Name}|tracks={sequence.Tracks.Count}|items={itemCount}|duration={sequence.Duration.Seconds:0.###}|{path}");
}

Console.WriteLine($"OUTPUT={outputDirectory}");
return 0;

static Project BuildTimelineProject(string name, int clipCount, int trackCount, string videoPath)
{
    var project = CreateProject(name, videoPath, MediaKind.Video, out var asset);
    var sequence = project.MainSequence!;
    var perTrack = (int)Math.Ceiling(clipCount / (double)trackCount);
    var created = 0;
    for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
    {
        var track = new Track
        {
            Kind = trackIndex == 0 ? TrackKind.Video : TrackKind.Overlay,
            Name = trackIndex == 0 ? "V1" : $"O{trackIndex}",
            Order = trackIndex,
        };
        TimelineItem? previous = null;
        for (var localIndex = 0; localIndex < perTrack && created < clipCount; localIndex++, created++)
        {
            var duration = 1.5 + ((created % 5) * 0.1);
            var item = new TimelineItem
            {
                Kind = ItemKind.Clip,
                MediaAssetId = asset.Id,
                TimelineStart = MediaTime.FromSeconds(localIndex * 1.75 + (trackIndex * 0.05)),
                Duration = MediaTime.FromSeconds(duration),
                SourceDuration = MediaTime.FromSeconds(duration),
                SourceStart = MediaTime.FromSeconds((created % 10) * 0.05),
                Opacity = 0.85 + ((created % 4) * 0.05),
            };
            item.Transform.PositionX = (trackIndex % 4) * 10;
            item.Transform.PositionY = (trackIndex % 3) * 8;
            track.Items.Add(item);
            if (previous != null && Math.Abs(item.TimelineStart.Seconds - previous.TimelineEnd.Seconds) <= 0.05)
            {
                sequence.Transitions.Add(new Transition
                {
                    LeftItemId = previous.Id,
                    RightItemId = item.Id,
                    Kind = TransitionKind.CrossDissolve,
                    Duration = MediaTime.FromSeconds(0.25),
                });
            }
            previous = item;
        }
        sequence.Tracks.Add(track);
    }

    for (var second = 0; second < sequence.Duration.Seconds; second += 10)
    {
        sequence.Markers.Add(new Marker
        {
            Label = $"M{second:000}",
            Time = MediaTime.FromSeconds(second),
            Color = "#A78BFA",
        });
    }
    project.IncrementRevision();
    return project;
}

static Project BuildAnimationProject(string videoPath)
{
    var project = CreateProject("perf-animation", videoPath, MediaKind.Video, out var asset);
    var sequence = project.MainSequence!;
    var track = new Track { Kind = TrackKind.Video, Name = "V1" };
    for (var itemIndex = 0; itemIndex < 8; itemIndex++)
    {
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            TimelineStart = MediaTime.FromSeconds(itemIndex * 10),
            Duration = MediaTime.FromSeconds(12),
            SourceDuration = MediaTime.FromSeconds(12),
        };
        foreach (var property in new[]
                 {
                     AnimationPropertyNames.PositionX,
                     AnimationPropertyNames.PositionY,
                     AnimationPropertyNames.ScaleX,
                     AnimationPropertyNames.ScaleY,
                     AnimationPropertyNames.Rotation,
                     AnimationPropertyNames.Opacity,
                     AnimationPropertyNames.Volume,
                     AnimationPropertyNames.Pan,
                 })
        {
            var channel = new AnimationChannel { PropertyName = property, DefaultValue = property.Contains("Scale", StringComparison.Ordinal) ? 1 : 0 };
            for (var keyIndex = 0; keyIndex < 100; keyIndex++)
            {
                channel.Keyframes.Add(new Keyframe
                {
                    Time = MediaTime.FromSeconds(keyIndex * 0.1),
                    Value = property switch
                    {
                        AnimationPropertyNames.Opacity => 0.5 + 0.5 * Math.Sin(keyIndex * 0.08),
                        AnimationPropertyNames.ScaleX or AnimationPropertyNames.ScaleY => 1 + 0.15 * Math.Sin(keyIndex * 0.1),
                        AnimationPropertyNames.Volume => 0.7 + 0.3 * Math.Sin(keyIndex * 0.07),
                        AnimationPropertyNames.Pan => Math.Sin(keyIndex * 0.09),
                        _ => Math.Sin(keyIndex * 0.1) * 120,
                    },
                    Interpolation = keyIndex % 8 == 0 ? InterpolationType.Bezier : InterpolationType.Linear,
                });
            }
            item.AnimationChannels.Add(channel);
        }
        track.Items.Add(item);
    }
    sequence.Tracks.Add(track);
    project.IncrementRevision();
    return project;
}

static Project BuildAudioProject(string audioPath)
{
    var project = CreateProject("perf-audio", audioPath, MediaKind.Audio, out var asset);
    var sequence = project.MainSequence!;
    for (var trackIndex = 0; trackIndex < 12; trackIndex++)
    {
        var track = new Track { Kind = TrackKind.Audio, Name = $"A{trackIndex + 1}", Order = trackIndex };
        for (var itemIndex = 0; itemIndex < 24; itemIndex++)
        {
            track.Items.Add(new TimelineItem
            {
                Kind = ItemKind.Clip,
                MediaAssetId = asset.Id,
                TimelineStart = MediaTime.FromSeconds(itemIndex * 4 + trackIndex * 0.1),
                Duration = MediaTime.FromSeconds(4),
                SourceDuration = MediaTime.FromSeconds(4),
                Volume = 0.6 + ((trackIndex % 4) * 0.1),
                FadeInDuration = MediaTime.FromSeconds(0.1),
                FadeOutDuration = MediaTime.FromSeconds(0.15),
            });
        }
        sequence.Tracks.Add(track);
    }
    project.IncrementRevision();
    return project;
}

static Project BuildExactPreviewProject(string videoPath)
{
    var project = CreateProject("perf-exact-preview", videoPath, MediaKind.Video, out var asset);
    var sequence = project.MainSequence!;
    var track = new Track { Kind = TrackKind.Video, Name = "V1" };
    var item = new TimelineItem
    {
        Kind = ItemKind.Clip,
        MediaAssetId = asset.Id,
        Duration = MediaTime.FromSeconds(30),
        SourceDuration = MediaTime.FromSeconds(30),
        ColorCorrection = new ColorCorrection
        {
            Brightness = 0.08,
            Contrast = 0.15,
            Saturation = 1.2,
        },
    };
    item.Masks.Add(new Mask
    {
        Shape = MaskShape.Rectangle,
        ScaleX = 0.85,
        ScaleY = 0.85,
        Feather = 0.05,
    });
    track.Items.Add(item);
    sequence.Tracks.Add(track);
    project.IncrementRevision();
    return project;
}

static Project CreateProject(string name, string mediaPath, MediaKind kind, out MediaAsset asset)
{
    var project = new Project { Name = name };
    var exists = File.Exists(mediaPath);
    asset = new MediaAsset
    {
        Kind = kind,
        OriginalPath = mediaPath,
        RelativeProjectPath = mediaPath,
        Duration = MediaTime.FromSeconds(kind == MediaKind.Audio ? 120 : 60),
        PixelWidth = kind == MediaKind.Video ? 1920 : 0,
        PixelHeight = kind == MediaKind.Video ? 1080 : 0,
        IsOffline = !exists,
    };
    project.MediaLibrary.Add(asset);
    return project;
}

static string ResolveMedia(string? explicitPath, string defaultName)
{
    if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current != null)
    {
        var candidate = Path.Combine(current.FullName, defaultName);
        if (File.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    return Path.GetFullPath(defaultName);
}
