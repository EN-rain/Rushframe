namespace Rushframe.Domain;

public readonly record struct TextLayoutSize(double Width, double Height);

public static class TextLayoutMetrics
{
    public static TextLayoutSize Measure(TimelineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var fontSize = Math.Max(1, item.FontSize);
        var lines = (item.TextContent ?? "Text").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var longestLineLength = Math.Max(1, lines.Max(line => line.Length));
        var lineCount = Math.Max(1, lines.Length);
        var outlinePadding = Math.Max(0, item.OutlineWidth) * 2;
        var shadowPaddingX = Math.Abs(item.ShadowOffsetX) + Math.Max(0, item.ShadowBlur) * 2;
        var shadowPaddingY = Math.Abs(item.ShadowOffsetY) + Math.Max(0, item.ShadowBlur) * 2;

        var width = Math.Max(fontSize, longestLineLength * fontSize * 0.62) + outlinePadding + shadowPaddingX;
        var height = Math.Max(fontSize * 1.4, fontSize * 1.4 * lineCount) + outlinePadding + shadowPaddingY;
        return new TextLayoutSize(Math.Max(2, width), Math.Max(2, height));
    }
}
