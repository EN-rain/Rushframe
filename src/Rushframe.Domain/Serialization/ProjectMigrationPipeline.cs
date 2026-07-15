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
        new Version3To4Migration(),
        new Version4To5Migration(),
        new Version5To6Migration(),
        new Version6To7Migration(),
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
            project["modifiedUtc"] ??= project["createdUtc"]?.DeepClone() ?? DateTimeOffset.UnixEpoch;
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

    private sealed class Version3To4Migration : IProjectMigration
    {
        public int FromVersion => 3;
        public int ToVersion => 4;

        public void Apply(JsonObject project)
        {
            project["editingBrief"] ??= new JsonObject
            {
                ["purpose"] = string.Empty,
                ["targetAudience"] = string.Empty,
                ["platform"] = string.Empty,
                ["aspectRatio"] = string.Empty,
                ["tone"] = string.Empty,
                ["editingStyle"] = "custom",
                ["pacing"] = string.Empty,
                ["requiredMessages"] = new JsonArray(),
                ["requiredMediaAssetIds"] = new JsonArray(),
                ["forbiddenMediaAssetIds"] = new JsonArray(),
                ["captionPolicy"] = string.Empty,
                ["musicPolicy"] = string.Empty,
                ["soundEffectsPolicy"] = string.Empty,
                ["transitionPolicy"] = string.Empty,
                ["callToAction"] = string.Empty,
                ["brandColors"] = new JsonArray(),
                ["brandFonts"] = new JsonArray(),
                ["logoPolicy"] = string.Empty,
                ["referenceNotes"] = string.Empty,
            };
        }
    }

    private sealed class Version4To5Migration : IProjectMigration
    {
        public int FromVersion => 4;
        public int ToVersion => 5;

        public void Apply(JsonObject project)
        {
            project["mediaRelationships"] ??= new JsonArray();
        }
    }

    private sealed class Version5To6Migration : IProjectMigration
    {
        public int FromVersion => 5;
        public int ToVersion => 6;

        public void Apply(JsonObject project)
        {
            if (project["sequences"] is not JsonArray sequences) return;
            foreach (var sequenceNode in sequences)
            {
                if (sequenceNode is not JsonObject sequence
                    || sequence["transitions"] is not JsonArray transitions)
                    continue;
                foreach (var transitionNode in transitions)
                {
                    if (transitionNode is JsonObject transition)
                        transition["audioMode"] ??= "none";
                }
            }
        }
    }

    private sealed class Version6To7Migration : IProjectMigration
    {
        public int FromVersion => 6;
        public int ToVersion => 7;

        public void Apply(JsonObject project)
        {
            if (project["sequences"] is not JsonArray sequences) return;
            foreach (var sequenceNode in sequences)
            {
                if (sequenceNode is not JsonObject sequence || sequence["tracks"] is not JsonArray tracks) continue;
                foreach (var trackNode in tracks)
                {
                    if (trackNode is not JsonObject track || track["items"] is not JsonArray items) continue;
                    foreach (var itemNode in items)
                    {
                        if (itemNode is not JsonObject item) continue;
                        item["visualTransitionIn"] ??= "none";
                        item["visualTransitionInDuration"] ??= new JsonObject { ["numerator"] = 0, ["denominator"] = 1 };
                        item["visualTransitionOut"] ??= "none";
                        item["visualTransitionOutDuration"] ??= new JsonObject { ["numerator"] = 0, ["denominator"] = 1 };
                    }
                }
            }
        }
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
