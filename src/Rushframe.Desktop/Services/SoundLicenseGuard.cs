using System.IO;
using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

internal sealed record SoundLicenseIssue(
    MediaAssetId MediaAssetId,
    string Name,
    string LicenseName,
    string Message);

internal static class SoundLicenseGuard
{
    public static IReadOnlyList<SoundLicenseIssue> FindIssues(Project project, Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);

        var hasSoloTracks = sequence.Tracks.Any(track => track.Solo && !track.Hidden);
        var usedAssetIds = sequence.Tracks
            .Where(track => !track.Hidden && !track.Muted && (!hasSoloTracks || track.Solo))
            .SelectMany(track => track.Items)
            .Where(item => !item.Muted && item.MediaAssetId.HasValue)
            .Select(item => item.MediaAssetId!.Value)
            .ToHashSet();

        return project.MediaLibrary
            .Where(asset => asset.Kind == MediaKind.Audio && usedAssetIds.Contains(asset.Id))
            .Where(asset => asset.RequiresAttribution && string.IsNullOrWhiteSpace(asset.Attribution))
            .OrderBy(asset => Path.GetFileName(asset.OriginalPath), StringComparer.OrdinalIgnoreCase)
            .Select(asset => new SoundLicenseIssue(
                asset.Id,
                Path.GetFileName(asset.OriginalPath),
                asset.LicenseName,
                string.IsNullOrWhiteSpace(asset.LicenseName)
                    ? "Attribution is required, but both the license name and credit are missing."
                    : $"The {asset.LicenseName} license requires an attribution credit."))
            .ToArray();
    }

    public static string FormatBlockingMessage(IReadOnlyList<SoundLicenseIssue> issues)
    {
        if (issues.Count == 0) return string.Empty;
        var lines = issues.Take(8).Select(issue =>
            $"• {issue.Name}: {issue.Message}");
        var suffix = issues.Count > 8 ? $"\n• …and {issues.Count - 8} more" : string.Empty;
        return "Export is blocked because required sound attribution is missing:\n\n"
               + string.Join("\n", lines)
               + suffix
               + "\n\nOpen Preferences > View > Sound Library, select each sound, and complete its License metadata.";
    }
}
