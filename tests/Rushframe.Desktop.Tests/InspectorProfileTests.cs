using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class InspectorProfileTests
{
    [Fact]
    public void Audio_clip_exposes_audio_timing_and_fades_without_visual_controls()
    {
        var profile = InspectorProfile.Resolve(ItemKind.Clip, MediaKind.Audio, TrackKind.Audio);

        Assert.Equal("Audio clip", profile.DisplayName);
        Assert.True(profile.ShowAudio);
        Assert.True(profile.ShowTiming);
        Assert.True(profile.ShowFades);
        Assert.False(profile.ShowTransform);
        Assert.False(profile.ShowColor);
        Assert.False(profile.ShowStabilization);
        Assert.False(profile.ShowEffects);
        Assert.False(profile.CanExtractAudio);
        Assert.Equal(2, profile.PreferredTabIndex);
    }

    [Fact]
    public void Text_clip_exposes_rendered_typography_transform_fades_and_effects()
    {
        var profile = InspectorProfile.Resolve(ItemKind.Text, null, TrackKind.Text);

        Assert.True(profile.ShowTransform);
        Assert.True(profile.ShowText);
        Assert.True(profile.ShowFades);
        Assert.True(profile.ShowEffects);
        Assert.False(profile.ShowTiming);
        Assert.False(profile.ShowColor);
        Assert.False(profile.ShowStabilization);
        Assert.False(profile.ShowAudio);
        Assert.False(profile.CanExtractAudio);
        Assert.Equal(0, profile.PreferredTabIndex);
    }

    [Fact]
    public void Video_clip_exposes_visual_audio_timing_and_extract_controls()
    {
        var profile = InspectorProfile.Resolve(ItemKind.Clip, MediaKind.Video, TrackKind.Video);

        Assert.Equal("Video clip", profile.DisplayName);
        Assert.True(profile.ShowTransform);
        Assert.True(profile.ShowTiming);
        Assert.True(profile.ShowFades);
        Assert.True(profile.ShowColor);
        Assert.True(profile.ShowStabilization);
        Assert.True(profile.ShowEffects);
        Assert.True(profile.ShowAudio);
        Assert.True(profile.CanExtractAudio);
    }

    [Fact]
    public void Image_clip_hides_timing_audio_and_stabilization_but_keeps_visual_fades()
    {
        var profile = InspectorProfile.Resolve(ItemKind.Image, MediaKind.Image, TrackKind.Overlay);

        Assert.True(profile.ShowTransform);
        Assert.True(profile.ShowFades);
        Assert.True(profile.ShowColor);
        Assert.True(profile.ShowEffects);
        Assert.False(profile.ShowTiming);
        Assert.False(profile.ShowAudio);
        Assert.False(profile.ShowStabilization);
        Assert.False(profile.CanExtractAudio);
    }

    [Fact]
    public void Adjustment_layer_only_exposes_properties_used_by_exact_renderer()
    {
        var profile = InspectorProfile.Resolve(ItemKind.AdjustmentLayer, null, TrackKind.Overlay);

        Assert.False(profile.ShowTransform);
        Assert.False(profile.ShowTiming);
        Assert.False(profile.ShowFades);
        Assert.True(profile.ShowColor);
        Assert.True(profile.ShowEffects);
        Assert.False(profile.ShowAudio);
    }
}
