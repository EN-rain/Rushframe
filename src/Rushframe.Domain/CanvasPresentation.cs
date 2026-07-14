namespace Rushframe.Domain;

public enum CanvasBackgroundKind
{
    Solid,
    LinearGradient,
    RadialGradient,
    BlurSource,
    Transparent,
}

public sealed class CanvasBackground
{
    public CanvasBackgroundKind Kind { get; set; } = CanvasBackgroundKind.Solid;
    public string PrimaryColor { get; set; } = "#000000";
    public string SecondaryColor { get; set; } = "#000000";
    public double GradientAngleDegrees { get; set; } = 90;
    public double BlurStrength { get; set; } = 20;
    public double Opacity { get; set; } = 1;
}

public enum LayoutGuideKind
{
    Grid,
    Center,
    TitleSafe,
    ActionSafe,
    TikTok,
    InstagramReels,
    YouTubeShorts,
    SnapchatSpotlight,
    Custom,
}

public sealed class LayoutGuide
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public LayoutGuideKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }
    public string Color { get; set; } = "#66FFFFFF";
}
