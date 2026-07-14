using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class MediaIntelligenceProjectMutationGuardTests
{
    [Fact]
    public void restore_removes_rejected_replacement_and_restores_original_order_and_identity()
    {
        var firstAssetId = MediaAssetId.New();
        var protectedAssetId = MediaAssetId.New();
        var lastAssetId = MediaAssetId.New();
        var first = Analysis(firstAssetId, "first");
        var original = Analysis(protectedAssetId, "original");
        var last = Analysis(lastAssetId, "last");
        var project = new Project();
        project.MediaIntelligence.AddRange([first, original, last]);

        var snapshot = MediaIntelligenceProjectMutationGuard.Capture(project, protectedAssetId);
        project.MediaIntelligence.RemoveAll(value => value.MediaAssetId == protectedAssetId);
        project.MediaIntelligence.Add(Analysis(protectedAssetId, "replacement"));

        MediaIntelligenceProjectMutationGuard.Restore(project, snapshot);

        Assert.Equal(3, project.MediaIntelligence.Count);
        Assert.Same(first, project.MediaIntelligence[0]);
        Assert.Same(original, project.MediaIntelligence[1]);
        Assert.Same(last, project.MediaIntelligence[2]);
    }

    [Fact]
    public void restore_removes_new_analysis_when_no_previous_entry_existed()
    {
        var assetId = MediaAssetId.New();
        var project = new Project();
        var snapshot = MediaIntelligenceProjectMutationGuard.Capture(project, assetId);
        project.MediaIntelligence.Add(Analysis(assetId, "new"));

        MediaIntelligenceProjectMutationGuard.Restore(project, snapshot);

        Assert.Empty(project.MediaIntelligence);
    }

    private static MediaIntelligenceAnalysis Analysis(MediaAssetId assetId, string sourcePath) => new()
    {
        MediaAssetId = assetId,
        SourcePath = sourcePath,
    };
}
