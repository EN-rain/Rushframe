using Rushframe.Domain.Serialization;

namespace Rushframe.Domain.Tests;

public sealed class MediaRelationshipTests
{
    [Fact]
    public void Builder_links_broll_subject_location_and_motion_across_assets()
    {
        var firstAsset = MediaAssetId.New();
        var secondAsset = MediaAssetId.New();
        var analyses = new[]
        {
            new MediaIntelligenceAnalysis
            {
                MediaAssetId = firstAsset,
                Scenes =
                {
                    new MediaIntelligenceScene
                    {
                        SceneId = "a-scene",
                        Start = MediaTime.Zero,
                        End = MediaTime.FromSeconds(3),
                        Subjects = { "red sports car" },
                        Actions = { "car accelerates left to right" },
                        Tags = { "vehicle", "speed" },
                        Location = "city street",
                        CameraMotion = "pan right",
                    },
                },
                Moments =
                {
                    new MediaIntelligenceMoment
                    {
                        MomentId = "a",
                        Start = MediaTime.Zero,
                        End = MediaTime.FromSeconds(3),
                        Summary = "Red sports car accelerates through the city",
                        Visual = "red sports car moving left to right",
                        SceneIds = { "a-scene" },
                        Tags = { "vehicle", "speed" },
                        EditingRoles = { "b-roll" },
                        Scores = new MediaIntelligenceMomentScores { Overall = 0.8, BrollUsefulness = 0.9 },
                    },
                },
            },
            new MediaIntelligenceAnalysis
            {
                MediaAssetId = secondAsset,
                Scenes =
                {
                    new MediaIntelligenceScene
                    {
                        SceneId = "b-scene",
                        Start = MediaTime.Zero,
                        End = MediaTime.FromSeconds(4),
                        Subjects = { "red sports car" },
                        Actions = { "presenter points at car" },
                        Location = "city street",
                        CameraMotion = "pan right",
                    },
                },
                Moments =
                {
                    new MediaIntelligenceMoment
                    {
                        MomentId = "b",
                        Start = MediaTime.Zero,
                        End = MediaTime.FromSeconds(4),
                        Summary = "Presenter explains the red sports car speed",
                        Speech = "This red sports car reaches speed quickly on the city street",
                        SceneIds = { "b-scene" },
                        Tags = { "vehicle", "speed" },
                        Scores = new MediaIntelligenceMomentScores { Overall = 0.75 },
                    },
                },
            },
        };

        var relationships = MediaRelationshipBuilder.Build(analyses);

        Assert.Contains(relationships, value => value.Kind == MediaRelationshipKind.BrollRelevance);
        Assert.Contains(relationships, value => value.Kind == MediaRelationshipKind.SubjectContinuity);
        Assert.Contains(relationships, value => value.Kind == MediaRelationshipKind.LocationContinuity);
        Assert.Contains(relationships, value => value.Kind == MediaRelationshipKind.MatchingMotion);
        Assert.Contains(relationships, value => value.Kind == MediaRelationshipKind.ScreenDirectionCompatibility);
        Assert.All(relationships, value => Assert.InRange(value.Score, 0.55, 1));
    }

    [Fact]
    public void Relationships_round_trip_and_version_four_migrates_to_five()
    {
        var project = new Project();
        var sourceAssetId = MediaAssetId.New();
        var targetAssetId = MediaAssetId.New();
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis
        {
            MediaAssetId = sourceAssetId,
            Moments = { new MediaIntelligenceMoment { MomentId = "one", Start = MediaTime.FromSeconds(1), End = MediaTime.FromSeconds(2) } },
        });
        project.MediaIntelligence.Add(new MediaIntelligenceAnalysis
        {
            MediaAssetId = targetAssetId,
            Moments = { new MediaIntelligenceMoment { MomentId = "two", Start = MediaTime.FromSeconds(3), End = MediaTime.FromSeconds(4) } },
        });
        project.MediaRelationships.Add(new MediaRelationship
        {
            Kind = MediaRelationshipKind.AlternateTake,
            Source = new MediaMomentReference { MediaAssetId = sourceAssetId, MomentId = "one", StartSeconds = 1, EndSeconds = 2 },
            Target = new MediaMomentReference { MediaAssetId = targetAssetId, MomentId = "two", StartSeconds = 3, EndSeconds = 4 },
            Score = 0.9,
            Reason = "same take",
        });

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.Equal(5, restored.SchemaVersion);
        Assert.Single(restored.MediaRelationships);
        Assert.Equal(MediaRelationshipKind.AlternateTake, restored.MediaRelationships[0].Kind);

        var migrated = ProjectSerializer.Deserialize("""
        {
          "schemaVersion": 4,
          "name": "Old",
          "sequences": [{"name":"Main","width":1920,"height":1080,"frameRate":{"numerator":30,"denominator":1},"tracks":[],"markers":[],"transitions":[]}],
          "mediaLibrary": [],
          "mediaIntelligence": [],
          "editingBrief": {"editingStyle":"custom","requiredMessages":[],"requiredMediaAssetIds":[],"forbiddenMediaAssetIds":[],"brandColors":[],"brandFonts":[]}
        }
        """);
        Assert.Equal(5, migrated.SchemaVersion);
        Assert.Empty(migrated.MediaRelationships);
    }
}
