using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;
using Rushframe.Infrastructure;

namespace Rushframe.Domain.Tests;

public sealed class AssetPackGuardrailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rushframe-assets-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public AssetPackGuardrailTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Asset_pack_rejects_network_enabled_provider()
    {
        var path = WriteAssetManifest(new CreativeAssetProviderManifest
        {
            Id = "network.pack",
            Name = "Network Pack",
            AllowsNetwork = true,
        });

        var error = Assert.Throws<InvalidOperationException>(() => new CreativeAssetPackService().LoadPack(path));

        Assert.Contains("local-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Asset_pack_rejects_path_traversal()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, $"outside-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(outside, [1, 2, 3]);
        try
        {
            var manifest = new CreativeAssetProviderManifest
            {
                Id = "escape.pack",
                Name = "Escape Pack",
                Assets =
                {
                    new CreativeAssetDescriptor
                    {
                        Id = "escape",
                        Kind = CreativeAssetKind.Sticker,
                        Name = "Escape",
                        LocalPath = Path.GetRelativePath(_root, outside),
                    },
                },
            };
            var path = WriteAssetManifest(manifest);

            var error = Assert.Throws<InvalidOperationException>(() => new CreativeAssetPackService().LoadPack(path));

            Assert.Contains("escapes", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Asset_pack_rejects_junction_escape()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"rushframe-outside-assets-{Guid.NewGuid():N}");
        var junction = Path.Combine(_root, "linked");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "escape.png"), [1, 2, 3]);
        CreateJunction(junction, outside);
        try
        {
            var path = WriteAssetManifest(new CreativeAssetProviderManifest
            {
                Id = "junction.pack",
                Name = "Junction Pack",
                Assets =
                {
                    new CreativeAssetDescriptor
                    {
                        Id = "junction.escape",
                        Kind = CreativeAssetKind.Sticker,
                        Name = "Escape",
                        LocalPath = "linked/escape.png",
                    },
                },
            });

            var error = Assert.Throws<InvalidOperationException>(() => new CreativeAssetPackService().LoadPack(path));

            Assert.Contains("filesystem link", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RemoveJunction(junction);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Valid_local_asset_pack_resolves_file_and_attribution()
    {
        var assetDirectory = Path.Combine(_root, "assets");
        Directory.CreateDirectory(assetDirectory);
        var assetPath = Path.Combine(assetDirectory, "shape.png");
        File.WriteAllBytes(assetPath, [1, 2, 3]);
        var manifest = new CreativeAssetProviderManifest
        {
            Id = "valid.pack",
            Name = "Valid Pack",
            RequiresAttribution = true,
            Assets =
            {
                new CreativeAssetDescriptor
                {
                    Id = "valid.shape",
                    Kind = CreativeAssetKind.Shape,
                    Name = "Shape",
                    LocalPath = "assets/shape.png",
                    Attribution = "Example Creator",
                    LicenseName = "CC BY 4.0",
                },
            },
        };
        var path = WriteAssetManifest(manifest);

        var loaded = new CreativeAssetPackService().LoadPack(path);

        Assert.Equal(Path.GetFullPath(assetPath), loaded.Assets.Single().LocalPath);
        Assert.Equal("valid.pack", loaded.Assets.Single().ProviderId);
    }

    [Fact]
    public void Extension_manifest_rejects_remote_entry_point()
    {
        var path = WriteExtensionManifest(new ExtensionManifest
        {
            Id = "remote.extension",
            Name = "Remote",
            Version = "1.0.0",
            EntryPoint = "https://example.invalid/plugin.js",
        });

        var error = Assert.Throws<InvalidOperationException>(() => new ExtensionManifestService().Load(path));

        Assert.Contains("Remote", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void High_risk_extension_permissions_remain_disabled()
    {
        var path = WriteExtensionManifest(new ExtensionManifest
        {
            Id = "write.extension",
            Name = "Write Extension",
            Version = "1.0.0",
            Permissions = { ExtensionPermission.ReadProject, ExtensionPermission.WriteFiles },
            Enabled = true,
        });

        var loaded = new ExtensionManifestService().Load(path);

        Assert.False(loaded.Enabled);
    }

    private string WriteAssetManifest(CreativeAssetProviderManifest manifest)
    {
        var path = Path.Combine(_root, $"{manifest.Id}.rushframe-assets.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        return path;
    }

    private string WriteExtensionManifest(ExtensionManifest manifest)
    {
        var path = Path.Combine(_root, $"{manifest.Id}.rushframe-extension.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        return path;
    }

    private static void CreateJunction(string junction, string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create junction fixture.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static void RemoveJunction(string junction)
    {
        if (!Directory.Exists(junction)) return;
        var startInfo = new ProcessStartInfo { FileName = "cmd.exe", UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("rmdir");
        startInfo.ArgumentList.Add(junction);
        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
