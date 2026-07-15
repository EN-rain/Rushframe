namespace Rushframe.Domain;

public enum MediaKind { Video, Audio, Image, Subtitle, Font, Other }

public sealed class MediaAsset
{
    public MediaAssetId Id { get; init; } = MediaAssetId.New();
    public MediaKind Kind { get; init; }
    public string OriginalPath { get; init; } = "";
    public string RelativeProjectPath { get; init; } = "";
    public string FileFingerprint { get; init; } = "";
    public string CatalogSoundId { get; init; } = "";
    public string LicenseName { get; set; } = "";
    public string Attribution { get; set; } = "";
    public bool RequiresAttribution { get; set; }
    public bool IsGeneratedDerivative { get; init; }
    public MediaTime Duration { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public bool IsOffline { get; set; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(OriginalPath)
            ? Kind.ToString()
            : System.IO.Path.GetFileName(OriginalPath);
}
