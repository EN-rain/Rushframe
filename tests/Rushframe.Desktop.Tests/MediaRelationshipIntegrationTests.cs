using Rushframe.Domain;
using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Tests;

public sealed class MediaRelationshipIntegrationTests
{
    [Fact]
    public void Storing_analysis_rebuilds_cross_asset_relationships()
    {
        var project = new Project();
        var firstAsset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "first.mp4", RelativeProjectPath = "first.mp4" };
        var secondAsset = new MediaAsset { Kind = MediaKind.Video, OriginalPath = "second.mp4", RelativeProjectPath = "second.mp4" };
        project.MediaLibrary.AddRange([firstAsset, secondAsset]);

        MediaIntelligenceImportService.StoreInProject(project, Analysis(firstAsset.Id, "first", "robot walks through factory", "factory", "pan right"));
        Assert.Empty(project.MediaRelationships);

        MediaIntelligenceImportService.StoreInProject(project, Analysis(secondAsset.Id, "second", "presenter explains robot factory", "factory", "pan right"));

        Assert.NotEmpty(project.MediaRelationships);
        Assert.Contains(project.MediaRelationships, value => value.Kind == MediaRelationshipKind.LocationContinuity);
        Assert.Contains(project.MediaRelationships, value => value.Source.MediaAssetId != value.Target.MediaAssetId);
    }

    private static MediaIntelligenceAnalysis Analysis(MediaAssetId assetId, string id, string summary, string location, string motion) => new()
    {
        MediaAssetId = assetId,
        Scenes =
        {
            new MediaIntelligenceScene
            {
                SceneId = $"{id}-scene",
                Start = MediaTime.Zero,
                End = MediaTime.FromSeconds(3),
                Location = location,
                CameraMotion = motion,
                Subjects = { "robot" },
                Actions = { "robot walks left to right" },
            },
        },
        Moments =
        {
            new MediaIntelligenceMoment
            {
                MomentId = id,
                Start = MediaTime.Zero,
                End = MediaTime.FromSeconds(3),
                Summary = summary,
                Visual = summary,
                Speech = summary,
                SceneIds = { $"{id}-scene" },
                Tags = { "robot", "factory" },
                Scores = new MediaIntelligenceMomentScores { Overall = 0.8 },
            },
        },
    };
}
