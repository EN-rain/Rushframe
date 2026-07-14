namespace Rushframe.Domain;

public enum MaskShape
{
    Rectangle,
    Ellipse,
    Linear,
    Mirror,
    Star,
    Polygon,
    Split,
    Diamond,
    Heart,
    Text,
    Custom,
}

public sealed class MaskPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double InHandleX { get; set; }
    public double InHandleY { get; set; }
    public double OutHandleX { get; set; }
    public double OutHandleY { get; set; }
}

public sealed class Mask
{
    public MaskShape Shape { get; set; } = MaskShape.Rectangle;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double RotationDegrees { get; set; }
    public double Feather { get; set; }
    public double Expansion { get; set; }
    public bool Inverted { get; set; }
    public int PolygonSides { get; set; } = 6;
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Arial";
    public List<MaskPoint> Points { get; init; } = [];
}
