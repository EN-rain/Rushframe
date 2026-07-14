using System.Text.Json.Nodes;

namespace Rushframe.Domain.Serialization;

public interface IProjectMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Apply(JsonObject project);
}

public static class ProjectMigrationPipeline
{
    private static readonly IReadOnlyList<IProjectMigration> Migrations =
    [
        new Version0To1Migration(),
        new Version1To2Migration(),
        new Version2To3Migration(),
    ];

    public static string MigrateToCurrent(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new InvalidOperationException("Project JSON must contain an object root.");
        var version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > Project.CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Project schema {version} is newer than supported schema {Project.CurrentSchemaVersion}.");

        while (version < Project.CurrentSchemaVersion)
        {
            var migration = Migrations.SingleOrDefault(candidate => candidate.FromVersion == version)
                            ?? throw new InvalidOperationException($"No project migration is registered from schema {version}.");
            migration.Apply(root);
            version = migration.ToVersion;
            root["schemaVersion"] = version;
        }

        return root.ToJsonString();
    }

    private sealed class Version0To1Migration : IProjectMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;

        public void Apply(JsonObject project)
        {
            project["revision"] ??= 0;
            project["modifiedUtc"] ??= DateTimeOffset.UtcNow;
            project["assetProviders"] ??= new JsonArray();
            project["extensions"] ??= new JsonArray();
        }
    }

    private sealed class Version2To3Migration : IProjectMigration
    {
        public int FromVersion => 2;
        public int ToVersion => 3;

        public void Apply(JsonObject project)
        {
            project["workflow"] ??= new JsonObject
            {
                ["version"] = 1,
                ["activeStageId"] = "brief",
                ["estimatedSpendUsd"] = 0,
                ["actualSpendUsd"] = 0,
                ["budgetMode"] = "cap",
                ["localFirst"] = true,
                ["paidProvidersEnabled"] = false,
                ["requireApprovalForPaidOperations"] = true,
                ["singleActionApprovalThresholdUsd"] = 0.50,
                ["stages"] = new JsonArray
                {
                    Stage("brief", "Brief", true, "ready"),
                    Stage("source_review", "Source Review", false, "pending"),
                    Stage("edit_plan", "Edit Plan", true, "pending"),
                    Stage("agent_draft", "Agent Draft", true, "pending"),
                    Stage("human_review", "Human Review", true, "pending"),
                    Stage("final_qa", "Final QA", false, "pending"),
                    Stage("export", "Export", true, "pending"),
                },
                ["decisions"] = new JsonArray(),
                ["costEvents"] = new JsonArray(),
            };
            project["exportVariants"] ??= new JsonArray();
            project["externalCompositions"] ??= new JsonArray();
            project["automationProviders"] ??= new JsonArray();
            project["agentEditPlans"] ??= new JsonArray();
            project["renderJobs"] ??= new JsonArray();
            project["renderReceipts"] ??= new JsonArray();
            project["transcriptEditPolicy"] ??= new JsonObject
            {
                ["wordPaddingBeforeSeconds"] = 0.05,
                ["wordPaddingAfterSeconds"] = 0.08,
                ["minimumSilenceCutSeconds"] = 0.4,
                ["generatedCutFadeSeconds"] = 0.03,
                ["captionWordsPerChunk"] = 3,
                ["captionMinimumDurationSeconds"] = 0.45,
                ["captionMaximumDurationSeconds"] = 2.5,
                ["preserveAudioEvents"] = true,
                ["preserveReactionTails"] = true,
            };
        }

        private static JsonObject Stage(string id, string name, bool approval, string status) => new()
        {
            ["id"] = id,
            ["name"] = name,
            ["status"] = status,
            ["requiresApproval"] = approval,
            ["summary"] = string.Empty,
            ["inputs"] = new JsonArray(),
            ["outputs"] = new JsonArray(),
            ["warnings"] = new JsonArray(),
            ["artifactPaths"] = new JsonArray(),
            ["revision"] = 0,
        };
    }

    private sealed class Version1To2Migration : IProjectMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;

        public void Apply(JsonObject project)
        {
            if (project["sequences"] is not JsonArray sequences) return;
            foreach (var sequenceNode in sequences)
            {
                if (sequenceNode is not JsonObject sequence) continue;
                var fps = sequence["fps"]?.GetValue<double>() ?? 30;
                var frameRate = FrameRate.FromDouble(fps);
                sequence["frameRate"] ??= new JsonObject
                {
                    ["numerator"] = frameRate.Numerator,
                    ["denominator"] = frameRate.Denominator,
                };
                sequence["background"] ??= new JsonObject
                {
                    ["kind"] = "solid",
                    ["primaryColor"] = "#000000",
                    ["secondaryColor"] = "#000000",
                    ["gradientAngleDegrees"] = 90,
                    ["blurStrength"] = 20,
                    ["opacity"] = 1,
                };
                sequence["layoutGuides"] ??= new JsonArray();

                if (sequence["tracks"] is not JsonArray tracks) continue;
                foreach (var trackNode in tracks)
                {
                    if (trackNode is not JsonObject track || track["items"] is not JsonArray items) continue;
                    foreach (var itemNode in items)
                    {
                        if (itemNode is not JsonObject item) continue;
                        if (item["animationChannels"] is null)
                        {
                            var channels = new JsonArray();
                            if (item["animatedProperty"] is JsonObject legacy)
                                channels.Add(legacy.DeepClone());
                            item["animationChannels"] = channels;
                        }
                    }
                }
            }
        }
    }
}
