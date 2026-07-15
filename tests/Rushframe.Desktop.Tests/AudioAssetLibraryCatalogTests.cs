using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class AudioAssetLibraryCatalogTests
{
    [Fact]
    public void catalog_contains_music_and_sfx_sources_with_manual_download_guidance()
    {
        Assert.Contains(AudioAssetLibraryCatalog.All, item => item.Category.Contains("Music", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(AudioAssetLibraryCatalog.All, item => item.Category.Contains("SFX", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["Pixabay Music & Sound Effects", "Mixkit", "Freesound", "ZapSplat", "Sonniss GameAudioGDC", "YouTube Audio Library", "BBC Rewind Sound Effects"],
            AudioAssetLibraryCatalog.All.Select(item => item.Name));
        Assert.All(AudioAssetLibraryCatalog.All, item =>
        {
            Assert.True(Uri.TryCreate(item.Url, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.Contains("manual", item.Guidance, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(item.BestFor));
            Assert.False(string.IsNullOrWhiteSpace(item.Access));
            Assert.False(string.IsNullOrWhiteSpace(item.Attribution));
            Assert.False(string.IsNullOrWhiteSpace(item.CommercialUse));
        });
    }

    [Fact]
    public void creative_assets_dialog_states_that_rushframe_does_not_download_library_media()
    {
        var source = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Dialogs", "CreativeAssetsDialog.cs"));

        Assert.Contains("Audio Libraries", source, StringComparison.Ordinal);
        Assert.Contains("Rushframe does not download", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] segments) =>
        Path.Combine([FindRepositoryRoot(), ..segments]);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
