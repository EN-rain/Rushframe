using System.IO;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop.Services;

internal sealed record SoundLibraryDropPlan(
    IEditCommand Command,
    string TargetTrackName,
    bool CreatesTrack,
    MediaTime SnappedStart,
    MediaTime Duration);

internal sealed record SoundLibraryDropPlanResult(
    SoundLibraryDropPlan? Plan,
    string? Error)
{
    public bool Success => Plan != null;

    public static SoundLibraryDropPlanResult Ok(SoundLibraryDropPlan plan) => new(plan, null);

    public static SoundLibraryDropPlanResult Fail(string error) => new(null, error);
}

internal static class SoundLibraryDropPlanner
{
    public static SoundLibraryDropPlanResult Create(
        Project project,
        MediaAssetId assetId,
        int requestedTrackIndex,
        MediaTime timelineStart)
    {
        ArgumentNullException.ThrowIfNull(project);

        var sequence = project.MainSequence;
        if (sequence == null)
            return SoundLibraryDropPlanResult.Fail("The project has no sequence.");

        var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
        if (asset == null)
            return SoundLibraryDropPlanResult.Fail("The sound is not registered in this project.");
        if (asset.Kind != MediaKind.Audio)
            return SoundLibraryDropPlanResult.Fail("Only audio assets can be dropped from the sound library.");
        if (asset.IsOffline)
            return SoundLibraryDropPlanResult.Fail("The selected sound is offline.");

        var commands = new List<IEditCommand>();
        Track targetTrack;
        var createsTrack = false;

        if (requestedTrackIndex >= 0)
        {
            if (requestedTrackIndex >= sequence.Tracks.Count)
                return SoundLibraryDropPlanResult.Fail("The requested timeline track does not exist.");

            targetTrack = sequence.Tracks[requestedTrackIndex];
            if (targetTrack.Locked)
                return SoundLibraryDropPlanResult.Fail($"Track '{targetTrack.Name}' is locked.");
            if (targetTrack.Kind is not (TrackKind.Audio or TrackKind.Music))
                return SoundLibraryDropPlanResult.Fail("Drop sounds on an audio or music track, or below the existing tracks to create one.");
        }
        else
        {
            targetTrack = sequence.Tracks.FirstOrDefault(candidate =>
                              candidate.Kind == TrackKind.Audio && !candidate.Locked)
                          ?? CreateAudioTrack(sequence);

            if (!sequence.Tracks.Contains(targetTrack))
            {
                commands.Add(new AddPreparedTrackCommand { Track = targetTrack });
                createsTrack = true;
            }
        }

        if (asset.Duration.Seconds <= 0)
            return SoundLibraryDropPlanResult.Fail("The sound duration is unknown. Re-import or reindex the file before adding it to the timeline.");
        var duration = asset.Duration;
        var snappedStart = MediaTime.FromSeconds(Math.Max(0, timelineStart.Seconds))
            .SnapToFrame(sequence.FrameRate);

        commands.Add(new AddClipCommand
        {
            TrackId = targetTrack.Id,
            Item = new TimelineItem
            {
                Kind = ItemKind.Clip,
                MediaAssetId = asset.Id,
                TimelineStart = snappedStart,
                Duration = duration,
                SourceDuration = duration,
            },
        });

        return SoundLibraryDropPlanResult.Ok(new SoundLibraryDropPlan(
            new CompositeEditCommand($"Add sound {Path.GetFileName(asset.OriginalPath)}", commands),
            targetTrack.Name,
            createsTrack,
            snappedStart,
            duration));
    }

    private static Track CreateAudioTrack(Sequence sequence)
    {
        var nextNumber = sequence.Tracks.Count(candidate => candidate.Kind == TrackKind.Audio) + 1;
        return new Track
        {
            Kind = TrackKind.Audio,
            Name = $"A{nextNumber}",
            Order = sequence.Tracks.Count,
        };
    }
}
