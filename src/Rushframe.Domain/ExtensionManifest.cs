namespace Rushframe.Domain;

public enum ExtensionPermission
{
    ReadProject,
    ReadMediaMetadata,
    ProposeEdits,
    ApplyApprovedEdits,
    Render,
    ReadFiles,
    WriteFiles,
    Network,
}

public sealed class ExtensionManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string EntryPoint { get; set; } = string.Empty;
    public List<string> Capabilities { get; init; } = [];
    public List<ExtensionPermission> Permissions { get; init; } = [];
    public bool Enabled { get; set; } = true;
}

public enum CreativeAssetKind
{
    Sticker,
    Shape,
    Graphic,
    Font,
    Sound,
    Music,
}

public sealed class CreativeAssetDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public CreativeAssetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? PreviewPath { get; set; }
    public string? LicenseName { get; set; }
    public string? Attribution { get; set; }
    public string? SourceUrl { get; set; }
    public List<string> Tags { get; init; } = [];
}

public sealed class CreativeAssetProviderManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public bool AllowsNetwork { get; set; }
    public bool RequiresAttribution { get; set; }
    public List<CreativeAssetDescriptor> Assets { get; init; } = [];
}
