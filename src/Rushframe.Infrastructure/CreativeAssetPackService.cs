using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;

namespace Rushframe.Infrastructure;

public sealed class CreativeAssetPackService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public CreativeAssetProviderManifest BuiltInProvider { get; } = new()
    {
        Id = "rushframe.builtin.shapes",
        Name = "Rushframe Shapes",
        Version = "1.0.0",
        AllowsNetwork = false,
        RequiresAttribution = false,
        Assets =
        {
            BuiltIn("builtin.shape.star", "Star", "star", "favorite sparkle"),
            BuiltIn("builtin.shape.circle", "Circle", "circle", "round dot"),
            BuiltIn("builtin.shape.triangle", "Triangle", "triangle", "play pointer"),
            BuiltIn("builtin.shape.diamond", "Diamond", "diamond", "rhombus"),
            BuiltIn("builtin.shape.arrow", "Arrow", "arrow", "direction pointer"),
            BuiltIn("builtin.shape.heart", "Heart", "heart", "love favorite"),
            BuiltIn("builtin.shape.speech", "Speech Bubble", "speech", "chat comment"),
        },
    };

    public CreativeAssetProviderManifest LoadPack(string manifestPath)
    {
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Asset-pack manifest not found.", manifestPath);
        var packDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var manifest = JsonSerializer.Deserialize<CreativeAssetProviderManifest>(File.ReadAllText(manifestPath), JsonOptions)
                       ?? throw new InvalidOperationException("Asset-pack manifest is empty.");
        ValidateManifest(manifest, packDirectory);
        return manifest;
    }

    public IReadOnlyList<CreativeAssetProviderManifest> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return [BuiltInProvider];
        var providers = new List<CreativeAssetProviderManifest> { BuiltInProvider };
        foreach (var path in Directory.EnumerateFiles(directory, "*.rushframe-assets.json", SearchOption.AllDirectories))
        {
            try { providers.Add(LoadPack(path)); }
            catch { /* Invalid packs are ignored during startup and can be diagnosed during explicit import. */ }
        }
        return providers;
    }

    public void WriteTemplate(string path)
    {
        var manifest = new CreativeAssetProviderManifest
        {
            Id = "com.example.my-pack",
            Name = "My Local Asset Pack",
            Version = "1.0.0",
            AllowsNetwork = false,
            RequiresAttribution = true,
            Assets =
            {
                new CreativeAssetDescriptor
                {
                    Id = "example.sticker",
                    ProviderId = "com.example.my-pack",
                    Kind = CreativeAssetKind.Sticker,
                    Name = "Example Sticker",
                    LocalPath = "assets/example.png",
                    LicenseName = "CC BY 4.0",
                    Attribution = "Creator Name",
                    Tags = { "example", "sticker" },
                },
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void ValidateManifest(CreativeAssetProviderManifest manifest, string packDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException("Asset packs require an id and name.");
        if (manifest.AllowsNetwork)
            throw new InvalidOperationException("Rushframe asset packs must be local-only. Network-enabled packs are rejected.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Id) || !ids.Add(asset.Id))
                throw new InvalidOperationException("Asset ids must be non-empty and unique within a pack.");
            asset.ProviderId = manifest.Id;
            if (asset.Id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(asset.LocalPath))
                throw new InvalidOperationException($"Asset {asset.Id} requires a localPath.");

            try
            {
                asset.LocalPath = LocalPhysicalPathGuard.ResolveContainedExistingFile(packDirectory, asset.LocalPath);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
            {
                throw new InvalidOperationException($"Asset {asset.Id} is not a contained local file: {ex.Message}", ex);
            }
            if (!string.IsNullOrWhiteSpace(asset.PreviewPath))
            {
                try
                {
                    asset.PreviewPath = LocalPhysicalPathGuard.ResolveContainedExistingFile(packDirectory, asset.PreviewPath);
                }
                catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
                {
                    throw new InvalidOperationException($"Preview for {asset.Id} is not a contained local file: {ex.Message}", ex);
                }
            }
            if (manifest.RequiresAttribution && string.IsNullOrWhiteSpace(asset.Attribution))
                throw new InvalidOperationException($"Asset {asset.Id} requires attribution metadata.");
        }
    }

    private static CreativeAssetDescriptor BuiltIn(string id, string name, params string[] tags)
    {
        var asset = new CreativeAssetDescriptor
        {
            Id = id,
            ProviderId = "rushframe.builtin.shapes",
            Kind = CreativeAssetKind.Shape,
            Name = name,
            LicenseName = "Rushframe built-in",
        };
        asset.Tags.AddRange(tags);
        return asset;
    }
}
