using System.IO;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop.Services;

internal static class MediaRegistrationValidator
{
    public static bool RequiresProbe(MediaKind kind) =>
        kind is MediaKind.Video or MediaKind.Audio or MediaKind.Image;

    public static MediaAsset CreateAsset(
        string path,
        MediaKind kind,
        MediaProbeResult? probe,
        MediaAssetId? preserveId = null,
        MediaAsset? preserveMetadataFrom = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (RequiresProbe(kind) && probe == null)
            throw new InvalidDataException($"{kind} media must be probed before registration.");

        var video = probe?.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
        ValidateProbeMatchesKind(path, kind, probe);
        return new MediaAsset
        {
            Id = preserveId ?? MediaAssetId.New(),
            Kind = kind,
            OriginalPath = Path.GetFullPath(path),
            RelativeProjectPath = Path.GetFullPath(path),
            FileFingerprint = preserveMetadataFrom?.FileFingerprint ?? string.Empty,
            CatalogSoundId = preserveMetadataFrom?.CatalogSoundId ?? string.Empty,
            LicenseName = preserveMetadataFrom?.LicenseName ?? string.Empty,
            Attribution = preserveMetadataFrom?.Attribution ?? string.Empty,
            RequiresAttribution = preserveMetadataFrom?.RequiresAttribution ?? false,
            IsGeneratedDerivative = preserveMetadataFrom?.IsGeneratedDerivative ?? false,
            Duration = MediaTime.FromSeconds(probe?.Duration.TotalSeconds ?? 0),
            PixelWidth = video?.Width ?? 0,
            PixelHeight = video?.Height ?? 0,
            IsOffline = false,
        };
    }

    public static string? ValidateRelink(
        Project project,
        MediaAsset current,
        MediaAsset replacement)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);

        if (current.Kind != replacement.Kind)
            return $"Relink must preserve media kind. Expected {current.Kind}, received {replacement.Kind}.";

        if (replacement.Kind is MediaKind.Video or MediaKind.Audio)
        {
            var requiredSourceEnd = project.Sequences
                .SelectMany(sequence => sequence.Tracks)
                .SelectMany(track => track.Items)
                .Where(item => item.MediaAssetId == current.Id)
                .Select(item => item.SourceDuration.Seconds > 0
                    ? item.SourceStart.Seconds + item.SourceDuration.Seconds
                    : item.SourceStart.Seconds + item.Duration.Seconds * Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100))
                .DefaultIfEmpty(0)
                .Max();
            if (requiredSourceEnd > replacement.Duration.Seconds + 0.05)
            {
                return $"Replacement media is too short. Timeline clips require {requiredSourceEnd:0.###}s, but the replacement is {replacement.Duration.Seconds:0.###}s.";
            }
        }

        return null;
    }

    private static void ValidateProbeMatchesKind(string path, MediaKind kind, MediaProbeResult? probe)
    {
        if (probe == null) return;
        var hasVideo = probe.Streams.Any(stream => stream.Kind == MediaStreamKind.Video);
        var hasAudio = probe.Streams.Any(stream => stream.Kind == MediaStreamKind.Audio);
        switch (kind)
        {
            case MediaKind.Video when !hasVideo:
                throw new InvalidDataException($"'{Path.GetFileName(path)}' does not contain a video stream.");
            case MediaKind.Audio when !hasAudio:
                throw new InvalidDataException($"'{Path.GetFileName(path)}' does not contain an audio stream.");
            case MediaKind.Image when !hasVideo:
                throw new InvalidDataException($"'{Path.GetFileName(path)}' could not be decoded as an image.");
        }
    }
}
