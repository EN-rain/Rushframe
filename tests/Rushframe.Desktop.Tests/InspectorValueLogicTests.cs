using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class InspectorValueLogicTests
{
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("")]
    public void Numeric_parser_rejects_non_finite_or_empty_values(string value)
    {
        Assert.False(InspectorValueLogic.TryParseFiniteNumber(value, out _));
    }

    [Theory]
    [InlineData("1.25", 1.25)]
    [InlineData("-0.5", -0.5)]
    [InlineData("1e2", 100)]
    public void Numeric_parser_accepts_finite_values(string value, double expected)
    {
        Assert.True(InspectorValueLogic.TryParseFiniteNumber(value, out var actual));
        Assert.Equal(expected, actual, 8);
    }

    [Fact]
    public void Transform_clone_preserves_independent_axes_and_anchor()
    {
        var current = new Transform2D
        {
            ScaleX = 0.5,
            ScaleY = 0.8,
            AnchorX = 12,
            AnchorY = 18,
        };

        var result = InspectorValueLogic.CloneTransform(current, 10, 20, 0.6, 0.9, 30);

        Assert.Equal(0.6, result.ScaleX);
        Assert.Equal(0.9, result.ScaleY);
        Assert.Equal(12, result.AnchorX);
        Assert.Equal(18, result.AnchorY);
    }

    [Fact]
    public void Speed_clone_preserves_segments_and_pitch_policy()
    {
        var current = new SpeedCurve
        {
            ConstantSpeed = 1,
            PreservePitch = false,
            Segments =
            {
                new SpeedSegment
                {
                    SourceStart = MediaTime.FromSeconds(1),
                    SourceEnd = MediaTime.FromSeconds(2),
                    Speed = 1.75,
                },
            },
        };

        var result = InspectorValueLogic.CloneSpeedCurve(current, 2);

        Assert.Equal(2, result.ConstantSpeed);
        Assert.False(result.PreservePitch);
        Assert.Single(result.Segments);
        Assert.Equal(1.75, result.Segments[0].Speed);
        Assert.NotSame(current.Segments[0], result.Segments[0]);
    }

    [Fact]
    public void Color_builder_preserves_hidden_grading_fields()
    {
        var current = new ColorCorrection
        {
            Brightness = 0.1,
            Exposure = 0.2,
            Highlights = 0.3,
            Shadows = -0.2,
            Whites = 0.15,
            Blacks = -0.1,
            Tint = 12,
        };

        var result = InspectorValueLogic.BuildColorCorrection(current, 0.4, 0.5, 1.2, true);

        Assert.NotNull(result);
        Assert.Equal(0.2, result!.Exposure);
        Assert.Equal(0.3, result.Highlights);
        Assert.Equal(-0.2, result.Shadows);
        Assert.Equal(0.15, result.Whites);
        Assert.Equal(-0.1, result.Blacks);
        Assert.Equal(12, result.Tint);
        Assert.True(result.BlackAndWhite);
    }

    [Fact]
    public void Default_color_values_normalize_to_null()
    {
        Assert.Null(InspectorValueLogic.BuildColorCorrection(null, 0, 0, 1, false));
    }

    [Theory]
    [InlineData("white", "#FFFFFF")]
    [InlineData("#123456", "#123456")]
    public void Color_parser_normalizes_valid_values(string value, string expected)
    {
        Assert.True(InspectorValueLogic.TryNormalizeColor(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Color_parser_rejects_invalid_values()
    {
        Assert.False(InspectorValueLogic.TryNormalizeColor("not-a-color", out _));
    }
}
