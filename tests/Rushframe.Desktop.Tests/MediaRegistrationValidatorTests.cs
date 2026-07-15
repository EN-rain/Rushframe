using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop.Tests;

public sealed class MediaRegistrationValidatorTests
{
    [Fact]
    public void relink_rejects_media_kind_change()
    {
        var project = new Project();
        var current = new MediaAsset { Kind = MediaKind.Video, Duration = MediaTime.FromSeconds(10) };
        var replacement = new MediaAsset
        {
            Id = current.Id,
            Kind = MediaKind.Audio,
            Duration = MediaTime.FromSeconds(10),
        };

        var error = MediaRegistrationValidator.ValidateRelink(project, current, replacement);

        Assert.Contains("preserve media kind", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void relink_rejects_replacement_shorter_than_used_source_range()
    {
        var project = new Project();
        var current = new MediaAsset { Kind = MediaKind.Video, Duration = MediaTime.FromSeconds(10) };
        project.MediaLibrary.Add(current);
        project.MainSequence!.Tracks.Add(new Track
        {
            Kind = TrackKind.Video,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = current.Id,
                    SourceStart = MediaTime.FromSeconds(4),
                    SourceDuration = MediaTime.FromSeconds(5),
                    Duration = MediaTime.FromSeconds(5),
                },
            },
        });
        var replacement = new MediaAsset
        {
            Id = current.Id,
            Kind = MediaKind.Video,
            Duration = MediaTime.FromSeconds(8),
        };

        var error = MediaRegistrationValidator.ValidateRelink(project, current, replacement);

        Assert.Contains("too short", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void create_asset_rejects_video_without_video_stream()
    {
        var probe = new MediaProbeResult(
            "audio.mp4",
            TimeSpan.FromSeconds(2),
            1024,
            [new MediaStreamInfo(MediaStreamKind.Audio, "aac", Channels: 2, SampleRate: 48_000)]);

        var error = Assert.Throws<InvalidDataException>(() =>
            MediaRegistrationValidator.CreateAsset("audio.mp4", MediaKind.Video, probe));

        Assert.Contains("video stream", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
