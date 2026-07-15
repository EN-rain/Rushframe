using System.IO;

namespace Rushframe.Desktop.Services;

internal static class SoundLibraryAudioPolicy
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".aac", ".m4a", ".flac", ".ogg", ".oga", ".opus",
        ".wma", ".aif", ".aiff", ".ac3", ".amr", ".caf",
    };

    public static bool IsKnownAudioExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));
}
