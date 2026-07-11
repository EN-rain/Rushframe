using Rushframe.Application;
using Rushframe.Domain;

namespace Rushframe.Domain.Tests;

public sealed class MediaIntelligenceSearchServiceTests
{
    [Fact]
    public void Search_returns_matching_role_and_text()
    {
        var analysis = new MediaIntelligenceAnalysis
        {
            Moments =
            [
                new MediaIntelligenceMoment
                {
                    MomentId = "hook",
                    Start = MediaTime.Zero,
                    End = MediaTime.FromSeconds(3),
                    Summary = "Surprising product reveal",
                    Speech = "You will not expect this result",
                    EditingRoles = ["hook"],
                    Tags = ["product", "reveal"],
                    Scores = new MediaIntelligenceMomentScores { Overall = 0.9 },
                },
                new MediaIntelligenceMoment
                {
                    MomentId = "context",
                    Start = MediaTime.FromSeconds(4),
                    End = MediaTime.FromSeconds(10),
                    Summary = "Setup explanation",
                    Speech = "This explains the setup",
                    EditingRoles = ["context"],
                    Scores = new MediaIntelligenceMomentScores { Overall = 0.5 },
                },
            ],
        };
        var service = new MediaIntelligenceSearchService();

        var results = service.Search(analysis, new MediaMomentSearchQuery(
            "product reveal",
            Roles: ["hook"]));

        var result = Assert.Single(results);
        Assert.Equal("hook", result.MomentId);
    }

    [Fact]
    public void Agent_context_builder_returns_bounded_relevant_context()
    {
        var analysis = new MediaIntelligenceAnalysis
        {
            SourcePath = "video.mp4",
            SourceChecksum = "sha256:test",
            Metadata = new MediaIntelligenceTechnicalMetadata
            {
                Duration = MediaTime.FromSeconds(30),
                HasVideo = true,
                HasAudio = true,
            },
            Moments =
            [
                new MediaIntelligenceMoment
                {
                    MomentId = "hook",
                    Start = MediaTime.Zero,
                    End = MediaTime.FromSeconds(3),
                    Summary = "Strong reveal",
                    EditingRoles = ["hook"],
                    Scores = new MediaIntelligenceMomentScores { Overall = 0.9, HookPotential = 0.95 },
                },
                new MediaIntelligenceMoment
                {
                    MomentId = "context",
                    Start = MediaTime.FromSeconds(4),
                    End = MediaTime.FromSeconds(10),
                    Summary = "Background",
                    EditingRoles = ["context"],
                    Scores = new MediaIntelligenceMomentScores { Overall = 0.4 },
                },
            ],
        };
        var builder = new MediaAgentContextBuilder();

        var context = builder.Build(analysis, new MediaAgentContextRequest(Roles: ["hook"], Limit: 1));

        Assert.Equal(2, context.Summary.EditingMomentCount);
        Assert.Equal("hook", Assert.Single(context.Moments).MomentId);
        Assert.Equal("sha256:test", context.Source.SourceChecksum);
    }

    [Fact]
    public void FindBestTake_uses_recommended_candidate()
    {
        var analysis = new MediaIntelligenceAnalysis
        {
            Moments =
            [
                new MediaIntelligenceMoment
                {
                    MomentId = "take-1",
                    Start = MediaTime.Zero,
                    End = MediaTime.FromSeconds(2),
                    Summary = "First take",
                },
                new MediaIntelligenceMoment
                {
                    MomentId = "take-2",
                    Start = MediaTime.FromSeconds(3),
                    End = MediaTime.FromSeconds(5),
                    Summary = "Second take",
                },
            ],
            DuplicateTakeGroups =
            [
                new MediaIntelligenceDuplicateTakeGroup
                {
                    GroupId = "group-1",
                    Candidates =
                    [
                        new MediaIntelligenceDuplicateTakeCandidate { MomentId = "take-1", Score = 0.5 },
                        new MediaIntelligenceDuplicateTakeCandidate { MomentId = "take-2", Score = 0.9, Recommended = true },
                    ],
                },
            ],
        };
        var service = new MediaIntelligenceSearchService();

        var result = service.FindBestTake(analysis, "group-1");

        Assert.NotNull(result);
        Assert.Equal("take-2", result.MomentId);
    }
}
