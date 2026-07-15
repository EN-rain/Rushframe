namespace Rushframe.Domain;

public static class TrackCompatibility
{
    public static bool IsItemCompatibleWithTrack(ItemKind itemKind, TrackKind trackKind) => itemKind switch
    {
        ItemKind.Clip => trackKind is TrackKind.Video or TrackKind.Audio or TrackKind.Music or TrackKind.Voice or TrackKind.Overlay,
        ItemKind.Text => trackKind is TrackKind.Text or TrackKind.Overlay,
        ItemKind.Image => trackKind is TrackKind.Video or TrackKind.Overlay,
        ItemKind.Sticker => trackKind is TrackKind.Overlay or TrackKind.Video,
        ItemKind.AdjustmentLayer => trackKind is TrackKind.Video or TrackKind.Overlay,
        _ => false,
    };
}
