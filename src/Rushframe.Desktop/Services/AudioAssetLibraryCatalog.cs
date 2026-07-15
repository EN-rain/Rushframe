namespace Rushframe.Desktop.Services;

internal sealed record AudioAssetLibraryRecommendation(
    string Name,
    string Category,
    string Url,
    string BestFor,
    string Access,
    string Attribution,
    string CommercialUse,
    string Guidance);

internal static class AudioAssetLibraryCatalog
{
    public static IReadOnlyList<AudioAssetLibraryRecommendation> All { get; } =
    [
        new(
            "Pixabay Music & Sound Effects",
            "Music & SFX",
            "https://pixabay.com/music/",
            "Background music, transitions, impacts, ambience, risers, and social-media sounds.",
            "Free",
            "Usually not required; verify the exact asset license.",
            "Generally permitted for commercial projects under the Pixabay Content License.",
            "Download manually in your browser. Do not redistribute original files as a standalone library; import the local file and save its license record."),
        new(
            "Mixkit",
            "Music & SFX",
            "https://mixkit.co/free-sound-effects/",
            "Quick transitions, UI sounds, impacts, nature, technology, game sounds, and stock music.",
            "Free",
            "Check the applicable Music or Sound Effects license for the selected asset.",
            "Commercial use depends on the applicable Mixkit license; confirm before import.",
            "Download manually and preserve the applicable Mixkit license with the project."),
        new(
            "Freesound",
            "SFX",
            "https://freesound.org/",
            "Highly specific Foley, field recordings, crowds, machines, footsteps, room tone, and ambience.",
            "Free",
            "Varies by sound: CC0, CC BY, or CC BY-NC.",
            "Prefer CC0 or CC BY for commercial projects; CC BY-NC is not suitable for commercial use.",
            "Download manually. Save the exact license and attribution for every imported sound."),
        new(
            "ZapSplat",
            "SFX",
            "https://www.zapsplat.com/",
            "Everyday Foley, vehicles, interfaces, horror, cartoons, nature, and cinematic effects.",
            "Free and Premium",
            "Free-account downloads normally require ZapSplat attribution; Premium does not.",
            "Free MP3 use is generally available for commercial projects with required attribution; verify the selected license.",
            "Download manually and keep the account tier and attribution record with the project."),
        new(
            "Sonniss GameAudioGDC",
            "SFX Packs",
            "https://sonniss.com/gameaudiogdc",
            "Downloadable professional game, film, and cinematic sound-effect packs.",
            "Free packs",
            "Not required for the GDC collections.",
            "Commercial, royalty-free media-production use; not for AI or ML training.",
            "Download packs manually, then add their local folder to the Sound Library and retain the pack license."),
        new(
            "YouTube Audio Library",
            "Music & SFX",
            "https://studio.youtube.com/",
            "YouTube music and basic production sound effects.",
            "Free",
            "Some tracks require attribution; filter for tracks that do not when needed.",
            "Copyright-safe for YouTube under the selected track terms; verify use outside YouTube.",
            "Open Audio Library in YouTube Studio, inspect the track license, then download manually."),
        new(
            "BBC Rewind Sound Effects",
            "Archive & Ambience",
            "https://sound-effects.bbcrewind.co.uk/",
            "Historical ambience, transportation, nature, machinery, crowds, and location recordings.",
            "Free for personal and educational use; commercial licensing available.",
            "Depends on the selected licence.",
            "Commercial projects require the appropriate BBC licence.",
            "Download manually only after confirming the selected recording's licence and saving that record."),
    ];
}
