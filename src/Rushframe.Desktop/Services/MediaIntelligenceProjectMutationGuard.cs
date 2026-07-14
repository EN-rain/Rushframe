using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

public sealed record MediaIntelligenceProjectSnapshot(
    MediaAssetId MediaAssetId,
    IReadOnlyList<MediaIntelligenceProjectSnapshotEntry> Entries);

public sealed record MediaIntelligenceProjectSnapshotEntry(
    int Index,
    MediaIntelligenceAnalysis Analysis);

public static class MediaIntelligenceProjectMutationGuard
{
    public static MediaIntelligenceProjectSnapshot Capture(Project project, MediaAssetId mediaAssetId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var entries = project.MediaIntelligence
            .Select((analysis, index) => new MediaIntelligenceProjectSnapshotEntry(index, analysis))
            .Where(entry => entry.Analysis.MediaAssetId == mediaAssetId)
            .ToArray();
        return new MediaIntelligenceProjectSnapshot(mediaAssetId, entries);
    }

    public static void Restore(Project project, MediaIntelligenceProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);

        project.MediaIntelligence.RemoveAll(existing => existing.MediaAssetId == snapshot.MediaAssetId);
        foreach (var entry in snapshot.Entries.OrderBy(entry => entry.Index))
            project.MediaIntelligence.Insert(
                Math.Min(entry.Index, project.MediaIntelligence.Count),
                entry.Analysis);
    }
}
