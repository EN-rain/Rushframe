using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

public sealed record InspectorProfile(
    string DisplayName,
    bool ShowTransform,
    bool ShowText,
    bool ShowTiming,
    bool ShowFades,
    bool ShowColor,
    bool ShowStabilization,
    bool ShowEffects,
    bool ShowAudio,
    bool CanExtractAudio,
    int PreferredTabIndex)
{
    public static InspectorProfile Resolve(ItemKind itemKind, MediaKind? mediaKind, TrackKind? trackKind)
    {
        var audioOnly = mediaKind == MediaKind.Audio
            || trackKind is TrackKind.Audio or TrackKind.Music or TrackKind.Voice;

        if (itemKind == ItemKind.Text)
        {
            return new InspectorProfile(
                "Text clip",
                ShowTransform: true,
                ShowText: true,
                ShowTiming: false,
                ShowFades: true,
                ShowColor: false,
                ShowStabilization: false,
                ShowEffects: true,
                ShowAudio: false,
                CanExtractAudio: false,
                PreferredTabIndex: 0);
        }

        if (itemKind == ItemKind.AdjustmentLayer)
        {
            return new InspectorProfile(
                "Adjustment layer",
                ShowTransform: false,
                ShowText: false,
                ShowTiming: false,
                ShowFades: false,
                ShowColor: true,
                ShowStabilization: false,
                ShowEffects: true,
                ShowAudio: false,
                CanExtractAudio: false,
                PreferredTabIndex: 0);
        }

        if (audioOnly)
        {
            return new InspectorProfile(
                trackKind == TrackKind.Music ? "Music clip" : trackKind == TrackKind.Voice ? "Voice clip" : "Audio clip",
                ShowTransform: false,
                ShowText: false,
                ShowTiming: true,
                ShowFades: true,
                ShowColor: false,
                ShowStabilization: false,
                ShowEffects: false,
                ShowAudio: true,
                CanExtractAudio: false,
                PreferredTabIndex: 2);
        }

        if (itemKind is ItemKind.Image or ItemKind.Sticker || mediaKind == MediaKind.Image)
        {
            return new InspectorProfile(
                itemKind == ItemKind.Sticker ? "Sticker" : "Image clip",
                ShowTransform: true,
                ShowText: false,
                ShowTiming: false,
                ShowFades: true,
                ShowColor: true,
                ShowStabilization: false,
                ShowEffects: true,
                ShowAudio: false,
                CanExtractAudio: false,
                PreferredTabIndex: 0);
        }

        var video = mediaKind == MediaKind.Video;
        return new InspectorProfile(
            video ? "Video clip" : "Media clip",
            ShowTransform: true,
            ShowText: false,
            ShowTiming: video,
            ShowFades: video,
            ShowColor: true,
            ShowStabilization: video,
            ShowEffects: true,
            ShowAudio: video,
            CanExtractAudio: video,
            PreferredTabIndex: 0);
    }
}
