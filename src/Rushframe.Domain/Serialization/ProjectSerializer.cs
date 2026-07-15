using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rushframe.Domain.Serialization;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new MediaTimeConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static string Serialize(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        // Persistence normalization must never mutate the live editor model. Build
        // an isolated in-memory clone first, then apply schema/default/overview
        // updates only to that clone before producing the final JSON snapshot.
        var rawJson = JsonSerializer.Serialize(project, Options);
        var snapshot = JsonSerializer.Deserialize<Project>(rawJson, Options)
                       ?? throw new InvalidOperationException("Could not clone project for serialization");
        ProjectInvariantNormalizer.Normalize(snapshot);
        snapshot.SchemaVersion = Project.CurrentSchemaVersion;
        snapshot.Workflow.EnsureDefaults();
        EnsureDefaultVariant(snapshot);
        EnsureDefaultProviders(snapshot);
        snapshot.Overview = ProjectOverviewBuilder.Build(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static Project CreateSnapshot(Project project) =>
        Deserialize(Serialize(project));

    public static Project Deserialize(string json)
    {
        var migrated = ProjectMigrationPipeline.MigrateToCurrent(json);
        var project = JsonSerializer.Deserialize<Project>(migrated, Options)
                      ?? throw new InvalidOperationException("Deserialized project is null");
        ProjectInvariantNormalizer.Normalize(project);
        project.SchemaVersion = Project.CurrentSchemaVersion;
        project.Workflow.EnsureDefaults();
        EnsureDefaultVariant(project);
        EnsureDefaultProviders(project);
        return project;
    }

    private static void EnsureDefaultProviders(Project project)
    {
        var defaults = new[]
        {
            new AutomationProviderManifest
            {
                Id = "rushframe-ffmpeg",
                Name = "Rushframe FFmpeg",
                Enabled = true,
                Local = true,
                Paid = false,
                Capabilities = { "edit", "render", "transcode", "audio", "verification" },
            },
            new AutomationProviderManifest
            {
                Id = "rushframe-intelligence",
                Name = "Rushframe Local Intelligence",
                Enabled = true,
                Local = true,
                Paid = false,
                Capabilities = { "transcription", "scenes", "audio-analysis", "visual-analysis", "semantic-search" },
            },
            new AutomationProviderManifest
            {
                Id = "remotion-local",
                Name = "Local Remotion Adapter",
                Enabled = false,
                Local = true,
                Paid = false,
                Capabilities = { "motion-graphics", "react-composition" },
                Notes = "Enabled only after a project-local Remotion binary is validated.",
            },
            new AutomationProviderManifest
            {
                Id = "hyperframes-local",
                Name = "Local HyperFrames Adapter",
                Enabled = false,
                Local = true,
                Paid = false,
                Capabilities = { "motion-graphics", "html-composition" },
                Notes = "Enabled only after a project-local HyperFrames binary is validated.",
            },
        };
        foreach (var provider in defaults)
        {
            if (project.AutomationProviders.All(candidate => !candidate.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase)))
                project.AutomationProviders.Add(provider);
        }
    }

    private static void EnsureDefaultVariant(Project project)
    {
        if (project.ExportVariants.Count > 0) return;
        var sequence = project.MainSequence;
        project.ExportVariants.Add(new ExportVariant
        {
            Name = "Primary",
            SequenceId = sequence?.Id,
            Width = sequence?.Width ?? 1080,
            Height = sequence?.Height ?? 1920,
            FrameRate = sequence?.FrameRate,
        });
    }
}
