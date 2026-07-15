using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;

namespace Rushframe.Infrastructure;

public sealed class ExtensionManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly IReadOnlySet<ExtensionPermission> AutomaticallyAllowedPermissions =
        new HashSet<ExtensionPermission>
        {
            ExtensionPermission.ReadProject,
            ExtensionPermission.ReadMediaMetadata,
            ExtensionPermission.ProposeEdits,
        };

    public ExtensionManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Extension manifest not found.", manifestPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions)
                       ?? throw new InvalidOperationException("Extension manifest is empty.");
        Validate(manifest, directory);

        // Rushframe currently discovers and reviews manifests but never executes arbitrary extension code.
        // High-risk permissions remain disabled until a future sandbox/host explicitly supports them.
        if (manifest.Permissions.Any(permission => !AutomaticallyAllowedPermissions.Contains(permission)))
            manifest.Enabled = false;
        return manifest;
    }

    public IReadOnlyList<ExtensionManifest> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var manifests = new List<ExtensionManifest>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.rushframe-extension.json", SearchOption.AllDirectories))
        {
            try { manifests.Add(Load(path)); }
            catch { /* Explicit import surfaces validation failures; startup discovery remains resilient. */ }
        }
        return manifests;
    }

    public void WriteTemplate(string path)
    {
        var template = new ExtensionManifest
        {
            Id = "com.example.rushframe-extension",
            Name = "Example Rushframe Extension",
            Version = "1.0.0",
            EntryPoint = "extension.json",
            Capabilities = { "timeline-context", "edit-proposals" },
            Permissions =
            {
                ExtensionPermission.ReadProject,
                ExtensionPermission.ReadMediaMetadata,
                ExtensionPermission.ProposeEdits,
            },
            Enabled = true,
        };
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var entryPoint = Path.Combine(directory, template.EntryPoint!);
        if (!File.Exists(entryPoint)) File.WriteAllText(entryPoint, "{}\n");
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    private static void Validate(ExtensionManifest manifest, string directory)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.Name)
            || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("Extension manifests require id, name, and version.");

        if (manifest.Permissions.Count != manifest.Permissions.Distinct().Count())
            throw new InvalidOperationException("Extension permissions must be unique.");

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint)) return;
        if (Uri.TryCreate(manifest.EntryPoint, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
            throw new InvalidOperationException("Remote extension entry points are not permitted.");

        try
        {
            manifest.EntryPoint = LocalPhysicalPathGuard.ResolveContainedExistingFile(directory, manifest.EntryPoint);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException($"Extension entry point must be a contained local file: {ex.Message}", ex);
        }
    }
}
