using System.Diagnostics;
using Rushframe.Domain;
using Rushframe.Infrastructure;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

namespace Rushframe.Media.Tests;

public sealed class FfmpegMediaServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rushframe-media-tests-{Guid.NewGuid():N}");
    private readonly string _ffmpegPath;
    private readonly FfmpegMediaService _service;

    public FfmpegMediaServiceTests()
    {
        Directory.CreateDirectory(_root);
        _ffmpegPath = ResolveFfmpeg();
        _service = new FfmpegMediaService(_ffmpegPath);
    }

    [Fact]
    public void EffectRegistry_AllListedEffectsHaveFinalRendererSupport()
    {
        var registry = new EffectRegistry();
        var listed = registry.GetAll().Select(effect => effect.EffectTypeId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(listed.Except(FfmpegMediaService.SupportedEffectTypeIds, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ProbeAsync_GeneratedVideo_ReturnsAudioAndVideoStreams()
    {
        var source = await CreateVideoWithAudioAsync();

        var probe = await _service.ProbeAsync(source);

        Assert.True(probe.Duration.TotalSeconds >= 1.8);
        Assert.True(probe.HasVideo);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_InvalidEmbeddedAudioProbeFailsInsteadOfSilentlyDroppingAudio()
    {
        var invalidSource = Path.Combine(_root, "invalid-media.mp4");
        await File.WriteAllTextAsync(invalidSource, "not media");
        var output = Path.Combine(_root, "invalid-output.mp4");
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = invalidSource,
            RelativeProjectPath = invalidSource,
            Duration = MediaTime.FromSeconds(1),
        };
        project.MediaLibrary.Add(asset);
        var track = new Track { Kind = TrackKind.Video, Name = "V1" };
        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = asset.Id,
            Duration = MediaTime.FromSeconds(1),
            SourceDuration = MediaTime.FromSeconds(1),
        });
        project.MainSequence!.Tracks.Add(track);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _service.ExportTimelineAsync(project, project.MainSequence, output));

        Assert.Contains("silently dropping audio", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_RenderFailurePreservesExistingOutput()
    {
        var invalidSource = Path.Combine(_root, "invalid-existing-source.mp4");
        await File.WriteAllTextAsync(invalidSource, "not media");
        var output = Path.Combine(_root, "existing-output.mp4");
        var originalOutput = new byte[] { 1, 3, 3, 7 };
        await File.WriteAllBytesAsync(output, originalOutput);
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = invalidSource,
            RelativeProjectPath = invalidSource,
            Duration = MediaTime.FromSeconds(1),
        };
        project.MediaLibrary.Add(asset);
        project.MainSequence!.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExportTimelineAsync(project, project.MainSequence, output));

        Assert.Equal(originalOutput, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(_root, ".existing-output.*.rendering.mp4"));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_RejectsOutputThatMatchesRegisteredSource()
    {
        var source = await CreateVideoWithAudioAsync();
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = source,
            RelativeProjectPath = source,
            Duration = MediaTime.FromSeconds(2),
        };
        project.MediaLibrary.Add(asset);
        project.MainSequence!.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });
        var originalLength = new FileInfo(source).Length;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExportTimelineAsync(project, project.MainSequence, source));

        Assert.Contains("registered source", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalLength, new FileInfo(source).Length);
    }

    [Fact]
    public void ValidateRenderCapabilities_RejectsSegmentedSpeedCurves()
    {
        var sequence = new Sequence();
        var item = new TimelineItem
        {
            Kind = ItemKind.Clip,
            Duration = MediaTime.FromSeconds(2),
            SourceDuration = MediaTime.FromSeconds(2),
            SpeedCurve = new SpeedCurve(),
        };
        item.SpeedCurve.Segments.Add(new SpeedSegment
        {
            SourceStart = MediaTime.Zero,
            SourceEnd = MediaTime.FromSeconds(1),
            Speed = 2,
        });
        sequence.Tracks.Add(new Track { Kind = TrackKind.Video, Items = { item } });

        var error = Assert.Throws<NotSupportedException>(() =>
            FfmpegMediaService.ValidateRenderCapabilities(sequence));

        Assert.Contains("Segmented speed curves", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_MutedVisualItemStillRequiresItsVideoSource()
    {
        var missingSource = Path.Combine(_root, "missing-muted-visual.mp4");
        var output = Path.Combine(_root, "missing-muted-visual-output.mp4");
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = missingSource,
            RelativeProjectPath = missingSource,
            Duration = MediaTime.FromSeconds(1),
        };
        project.MediaLibrary.Add(asset);
        project.MainSequence!.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Muted = true,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Muted = true,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.ExportTimelineAsync(project, project.MainSequence, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task Derivatives_GeneratedVideo_CreateFiles()
    {
        var source = await CreateVideoWithAudioAsync();
        var thumb = Path.Combine(_root, "thumb.jpg");
        var proxy = Path.Combine(_root, "proxy.mp4");
        var waveform = Path.Combine(_root, "waveform.png");

        await _service.GenerateThumbnailAsync(new(source, thumb, TimeSpan.FromSeconds(0.5)));
        await _service.GenerateProxyAsync(new(source, proxy, 120));
        await _service.GenerateWaveformAsync(new(source, waveform, 320, 80));

        Assert.True(new FileInfo(thumb).Length > 0);
        Assert.True(new FileInfo(proxy).Length > 0);
        Assert.True(new FileInfo(waveform).Length > 0);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task GenerateWaveformPeaksAsync_LongEnoughAudio_ReturnsRequestedBoundedPeakSet()
    {
        var source = await CreateToneAsync();

        var peaks = await _service.GenerateWaveformPeaksAsync(source, peakCount: 257);

        Assert.Equal(257, peaks.Count);
        Assert.All(peaks, peak => Assert.InRange(peak, 0f, 1f));
        Assert.Contains(peaks, peak => peak > 0.05f);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineRangeAsync_RendersOnlyRequestedChunkDuration()
    {
        var video = await CreateVideoWithAudioAsync();
        var output = Path.Combine(_root, "timeline-range.mp4");
        var project = new Project();
        var asset = new MediaAsset
        {
            Kind = MediaKind.Video,
            OriginalPath = video,
            RelativeProjectPath = video,
            Duration = MediaTime.FromSeconds(2),
        };
        project.MediaLibrary.Add(asset);
        var sequence = project.MainSequence!;
        sequence.Width = 320;
        sequence.Height = 240;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Duration = MediaTime.FromSeconds(2),
                    SourceDuration = MediaTime.FromSeconds(2),
                },
            },
        });

        await _service.ExportTimelineRangeAsync(
            project,
            sequence,
            output,
            startSeconds: 0.5,
            durationSeconds: 0.75,
            outputWidth: 320,
            outputHeight: 240);
        var probe = await _service.ProbeAsync(output);

        Assert.True(File.Exists(output));
        Assert.InRange(probe.Duration.TotalSeconds, 0.60, 1.00);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_WithAudioTrack_ContainsAudioStream()
    {
        var video = await CreateVideoWithAudioAsync();
        var audio = await CreateToneAsync();
        var output = Path.Combine(_root, "timeline.mp4");

        var project = new Project();
        var videoAsset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = video, RelativeProjectPath = video, Duration = MediaTime.FromSeconds(2) };
        var audioAsset = new MediaAsset { Kind = MediaKind.Audio, OriginalPath = audio, RelativeProjectPath = audio, Duration = MediaTime.FromSeconds(2) };
        project.MediaLibrary.Add(videoAsset);
        project.MediaLibrary.Add(audioAsset);
        var seq = project.MainSequence!;
        seq.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = videoAsset.Id,
                    Duration = MediaTime.FromSeconds(2),
                    SourceDuration = MediaTime.FromSeconds(2),
                },
            },
        });
        seq.Tracks.Add(new Track
        {
            Kind = TrackKind.Audio,
            Name = "A1",
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = audioAsset.Id,
                    Duration = MediaTime.FromSeconds(2),
                    SourceDuration = MediaTime.FromSeconds(2),
                    Volume = 0.8,
                    FadeInDuration = MediaTime.FromSeconds(0.2),
                    FadeOutDuration = MediaTime.FromSeconds(0.3),
                },
            },
        });

        await _service.ExportTimelineAsync(project, seq, output);
        var probe = await _service.ProbeAsync(output);

        Assert.True(File.Exists(output));
        Assert.True(probe.HasVideo);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_CustomLandscapeDimensions_UsesRequestedFrameSize()
    {
        var video = await CreateVideoWithAudioAsync();
        var output = Path.Combine(_root, "landscape.mp4");
        var project = new Project();
        var asset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = video, RelativeProjectPath = video, Duration = MediaTime.FromSeconds(2) };
        project.MediaLibrary.Add(asset);
        var seq = project.MainSequence!;
        seq.Width = 1080;
        seq.Height = 1920;
        seq.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Name = "V1",
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = asset.Id,
                    Duration = MediaTime.FromSeconds(2),
                    SourceDuration = MediaTime.FromSeconds(2),
                },
            },
        });

        await _service.ExportTimelineAsync(project, seq, output, outputWidth: 1280, outputHeight: 720);
        var probe = await _service.ProbeAsync(output);
        var videoStream = Assert.Single(probe.Streams, stream => stream.Kind == MediaStreamKind.Video);

        Assert.Equal(1280, videoStream.Width);
        Assert.Equal(720, videoStream.Height);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_LayeredComposition_RendersAdvancedFeatures()
    {
        var video = await CreateVideoWithAudioAsync();
        var image = await CreateImageAsync();
        var output = Path.Combine(_root, "advanced-composition.mp4");
        var project = new Project();
        var videoAsset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = video, RelativeProjectPath = video, Duration = MediaTime.FromSeconds(2) };
        var imageAsset = new MediaAsset { Kind = MediaKind.Image, OriginalPath = image, RelativeProjectPath = image, Duration = MediaTime.FromSeconds(2) };
        project.MediaLibrary.Add(videoAsset);
        project.MediaLibrary.Add(imageAsset);

        var sequence = project.MainSequence!;
        sequence.Width = 320;
        sequence.Height = 240;
        sequence.Background = new CanvasBackground
        {
            Kind = CanvasBackgroundKind.LinearGradient,
            PrimaryColor = "#101020",
            SecondaryColor = "#502080",
            GradientAngleDegrees = 35,
        };
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "V1", Order = 0 };
        var first = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = videoAsset.Id,
            TimelineStart = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(1.2),
            SourceDuration = MediaTime.FromSeconds(1.2),
        };
        var second = new TimelineItem
        {
            Kind = ItemKind.Clip,
            MediaAssetId = videoAsset.Id,
            TimelineStart = MediaTime.FromSeconds(1.2),
            Duration = MediaTime.FromSeconds(1.2),
            SourceStart = MediaTime.FromSeconds(0.4),
            SourceDuration = MediaTime.FromSeconds(1.6),
        };
        videoTrack.Items.Add(first);
        videoTrack.Items.Add(second);
        sequence.Tracks.Add(videoTrack);
        sequence.Transitions.Add(new Transition
        {
            LeftItemId = first.Id,
            RightItemId = second.Id,
            Kind = TransitionKind.CrossDissolve,
            Duration = MediaTime.FromSeconds(0.4),
        });

        var overlayTrack = new Track { Kind = TrackKind.Overlay, Name = "O1", Order = 1 };
        var overlay = new TimelineItem
        {
            Kind = ItemKind.Image,
            MediaAssetId = imageAsset.Id,
            TimelineStart = MediaTime.FromSeconds(0.2),
            Duration = MediaTime.FromSeconds(1.8),
            SourceDuration = MediaTime.FromSeconds(1.8),
            Opacity = 0.9,
        };
        overlay.Transform.ScaleX = 0.7;
        overlay.Transform.ScaleY = 0.7;
        overlay.Masks.Add(new Mask { Shape = MaskShape.Ellipse, ScaleX = 0.9, ScaleY = 0.9, Feather = 8 });
        overlay.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = -60,
            Keyframes =
            {
                new Keyframe { Time = MediaTime.Zero, Value = -60, Interpolation = InterpolationType.Bezier },
                new Keyframe { Time = MediaTime.FromSeconds(1.8), Value = 60 },
            },
        });
        overlayTrack.Items.Add(overlay);
        overlayTrack.Items.Add(new TimelineItem
        {
            Kind = ItemKind.AdjustmentLayer,
            TimelineStart = MediaTime.FromSeconds(0.7),
            Duration = MediaTime.FromSeconds(0.5),
            Effects = { new EffectInstance { EffectTypeId = "mono", Enabled = true } },
        });
        sequence.Tracks.Add(overlayTrack);

        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Text,
            Name = "T1",
            Order = 2,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Text,
                    TimelineStart = MediaTime.FromSeconds(0.1),
                    Duration = MediaTime.FromSeconds(2),
                    TextContent = "Rushframe's exact preview",
                    FontSize = 28,
                    FontFamily = "Arial",
                    FontBold = true,
                    FontAlign = "right",
                    FillColor = "#FFFFFF",
                    OutlineColor = "#000000",
                    OutlineWidth = 2,
                    ShadowColor = "#000000",
                    ShadowOpacity = 0.6,
                    ShadowOffsetX = 3,
                    ShadowOffsetY = 3,
                    ShadowBlur = 2,
                    FadeInDuration = MediaTime.FromSeconds(0.2),
                    Effects = { new EffectInstance { EffectTypeId = "brightness", Parameters = { ["amount"] = 0.05 } } },
                    Transform = { PositionX = 20, PositionY = 20, ScaleX = 0.85, ScaleY = 1.1, RotationDegrees = 8 },
                },
            },
        });

        await _service.ExportTimelineAsync(project, sequence, output);
        var probe = await _service.ProbeAsync(output);

        Assert.True(new FileInfo(output).Length > 0);
        Assert.True(probe.HasVideo);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExportTimelineAsync_BuiltInShapeSticker_RendersWithoutExternalAsset()
    {
        var output = Path.Combine(_root, "builtin-shape.mp4");
        var project = new Project();
        var sequence = project.MainSequence!;
        sequence.Width = 320;
        sequence.Height = 240;
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Overlay,
            Name = "Shapes",
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Sticker,
                    StickerId = "builtin.shape.star",
                    TimelineStart = MediaTime.Zero,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                    FillColor = "#FFD54A",
                    OutlineColor = "#201000",
                    OutlineWidth = 2,
                    Transform = { ScaleX = 0.5, ScaleY = 0.5, RotationDegrees = 12 },
                },
            },
        });

        await _service.ExportTimelineAsync(project, sequence, output);
        var probe = await _service.ProbeAsync(output);

        Assert.True(new FileInfo(output).Length > 0);
        Assert.True(probe.HasVideo);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task ExtractAudioAsync_GeneratedVideo_CreatesWav()
    {
        var source = await CreateVideoWithAudioAsync();
        var output = Path.Combine(_root, "extract.wav");

        await _service.ExtractAudioAsync(source, output);
        var probe = await _service.ProbeAsync(output);

        Assert.True(new FileInfo(output).Length > 0);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task GenerateProxyAsync_Cancelled_ThrowsOperationCanceled()
    {
        var source = await CreateVideoWithAudioAsync();
        var output = Path.Combine(_root, "cancelled-proxy.mp4");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.GenerateProxyAsync(new(source, output, 120), cancellationToken: cts.Token));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task VerifyExportAsync_ValidVideo_DecodesAndCapturesEvidence()
    {
        var source = await CreateVideoWithAudioAsync();
        var evidence = Path.Combine(_root, "verification-evidence");

        var report = await _service.VerifyExportAsync(
            source,
            expectedWidth: 160,
            expectedHeight: 120,
            expectedDurationSeconds: 2,
            evidenceDirectory: evidence);

        Assert.NotEqual(MediaExportVerificationStatus.Failed, report.Status);
        Assert.True(report.FullDecodePassed);
        Assert.True(report.HasVideo);
        Assert.True(report.HasAudio);
        Assert.Equal(160, report.Width);
        Assert.Equal(120, report.Height);
        Assert.NotEmpty(report.EvidenceFrames);
        Assert.All(report.EvidenceFrames, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    [Trait("Category", "Media")]
    public async Task VerifyExportAsync_LongBlackVideo_FailsTemporalQualityGate()
    {
        var source = Path.Combine(_root, $"black-{Guid.NewGuid():N}.mp4");
        await RunFfmpegAsync("-y -f lavfi -i color=c=black:s=160x120:r=30:d=3 -c:v libx264 -pix_fmt yuv420p", source);

        var report = await _service.VerifyExportAsync(
            source,
            expectedWidth: 160,
            expectedHeight: 120,
            expectedDurationSeconds: 3);

        Assert.Equal(MediaExportVerificationStatus.Failed, report.Status);
        Assert.NotEmpty(report.BlackIntervals);
        Assert.Contains(report.Errors, error => error.Contains("Black frames", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> CreateVideoWithAudioAsync()
    {
        var output = Path.Combine(_root, $"video-{Guid.NewGuid():N}.mp4");
        await RunFfmpegAsync("-y -f lavfi -i testsrc=size=160x120:rate=30:duration=2 -f lavfi -i sine=frequency=440:duration=2 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest", output);
        return output;
    }

    private async Task<string> CreateImageAsync()
    {
        var output = Path.Combine(_root, $"image-{Guid.NewGuid():N}.png");
        await RunFfmpegAsync("-y -f lavfi -i color=c=red:s=100x80:d=1 -frames:v 1", output);
        return output;
    }

    private async Task<string> CreateToneAsync()
    {
        var output = Path.Combine(_root, $"tone-{Guid.NewGuid():N}.wav");
        await RunFfmpegAsync("-y -f lavfi -i sine=frequency=880:duration=2 -acodec pcm_s16le -ar 48000 -ac 2", output);
        return output;
    }

    private async Task RunFfmpegAsync(string args, string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"{args} \"{output}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("FFmpeg fixture generation timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private static string ResolveFfmpeg()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".tools", "bin", "ffmpeg.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".tools", "bin", "ffmpeg.exe")),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;
        return "ffmpeg";
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup for Windows file handles.
        }
    }
}
