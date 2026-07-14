using System.Globalization;
using System.Windows.Media;
using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

public static class InspectorValueLogic
{
    private const double EqualityTolerance = 0.000000001;

    public static bool TryParseFiniteNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return parsed && double.IsFinite(value);
    }

    public static bool TryNormalizeColor(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is not Color color) return false;
            normalized = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            return true;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static Transform2D CloneTransform(
        Transform2D current,
        double positionX,
        double positionY,
        double scaleX,
        double scaleY,
        double rotationDegrees) => new()
    {
        PositionX = positionX,
        PositionY = positionY,
        ScaleX = Math.Max(0.01, scaleX),
        ScaleY = Math.Max(0.01, scaleY),
        RotationDegrees = rotationDegrees,
        AnchorX = current.AnchorX,
        AnchorY = current.AnchorY,
    };

    public static SpeedCurve CloneSpeedCurve(SpeedCurve current, double constantSpeed) => new()
    {
        ConstantSpeed = Math.Clamp(constantSpeed, 0.1, 100),
        PreservePitch = current.PreservePitch,
        Segments = current.Segments.Select(segment => new SpeedSegment
        {
            SourceStart = segment.SourceStart,
            SourceEnd = segment.SourceEnd,
            Speed = segment.Speed,
        }).ToList(),
    };

    public static ColorCorrection? BuildColorCorrection(
        ColorCorrection? current,
        double brightness,
        double contrast,
        double saturation,
        bool blackAndWhite)
    {
        var result = new ColorCorrection
        {
            Brightness = Math.Clamp(brightness, -1, 1),
            Contrast = Math.Clamp(contrast, -1, 3),
            Saturation = Math.Clamp(saturation, 0, 4),
            Exposure = current?.Exposure ?? 0,
            Highlights = current?.Highlights ?? 0,
            Shadows = current?.Shadows ?? 0,
            Whites = current?.Whites ?? 0,
            Blacks = current?.Blacks ?? 0,
            Tint = current?.Tint ?? 0,
            BlackAndWhite = blackAndWhite,
        };
        return IsDefault(result) ? null : result;
    }

    public static StabilizationSettings BuildStabilization(StabilizationSettings? current, bool enabled) => new()
    {
        Enabled = enabled,
        Strength = current?.Strength ?? 0.5,
        CropZoomCompensation = current?.CropZoomCompensation ?? true,
        AnalysisComplete = current?.AnalysisComplete ?? false,
    };

    public static bool TransformEquals(Transform2D left, Transform2D right) =>
        NearlyEqual(left.PositionX, right.PositionX)
        && NearlyEqual(left.PositionY, right.PositionY)
        && NearlyEqual(left.ScaleX, right.ScaleX)
        && NearlyEqual(left.ScaleY, right.ScaleY)
        && NearlyEqual(left.RotationDegrees, right.RotationDegrees)
        && NearlyEqual(left.AnchorX, right.AnchorX)
        && NearlyEqual(left.AnchorY, right.AnchorY);

    public static bool SpeedCurveEquals(SpeedCurve? left, SpeedCurve? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;
        if (!NearlyEqual(left.ConstantSpeed, right.ConstantSpeed)
            || left.PreservePitch != right.PreservePitch
            || left.Segments.Count != right.Segments.Count)
            return false;

        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            if (leftSegment.SourceStart != rightSegment.SourceStart
                || leftSegment.SourceEnd != rightSegment.SourceEnd
                || !NearlyEqual(leftSegment.Speed, rightSegment.Speed))
                return false;
        }
        return true;
    }

    public static bool ColorCorrectionEquals(ColorCorrection? left, ColorCorrection? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return left == null && right == null;
        return NearlyEqual(left.Brightness, right.Brightness)
            && NearlyEqual(left.Contrast, right.Contrast)
            && NearlyEqual(left.Saturation, right.Saturation)
            && NearlyEqual(left.Exposure, right.Exposure)
            && NearlyEqual(left.Highlights, right.Highlights)
            && NearlyEqual(left.Shadows, right.Shadows)
            && NearlyEqual(left.Whites, right.Whites)
            && NearlyEqual(left.Blacks, right.Blacks)
            && NearlyEqual(left.Tint, right.Tint)
            && left.BlackAndWhite == right.BlackAndWhite;
    }

    public static bool StabilizationEquals(StabilizationSettings? left, StabilizationSettings? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;
        return left.Enabled == right.Enabled
            && NearlyEqual(left.Strength, right.Strength)
            && left.CropZoomCompensation == right.CropZoomCompensation
            && left.AnalysisComplete == right.AnalysisComplete;
    }

    public static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= EqualityTolerance;

    private static bool IsDefault(ColorCorrection color) =>
        NearlyEqual(color.Brightness, 0)
        && NearlyEqual(color.Contrast, 0)
        && NearlyEqual(color.Saturation, 1)
        && NearlyEqual(color.Exposure, 0)
        && NearlyEqual(color.Highlights, 0)
        && NearlyEqual(color.Shadows, 0)
        && NearlyEqual(color.Whites, 0)
        && NearlyEqual(color.Blacks, 0)
        && NearlyEqual(color.Tint, 0)
        && !color.BlackAndWhite;
}
