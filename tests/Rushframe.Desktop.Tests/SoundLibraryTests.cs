using Rushframe.Desktop.Services;
using Rushframe.Domain;

namespace Rushframe.Desktop.Tests;

public sealed class SoundLibraryTests
{
    [Fact]
    public void dropping_sound_below_tracks_creates_one_audio_track_and_is_exactly_undoable()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var asset = CreateAudioAsset(durationSeconds: 2.5);
        project.MediaLibrary.Add(asset);

        var requestedStart = MediaTime.FromSeconds(1.237);
        var result = SoundLibraryDropPlanner.Create(project, asset.Id, -1, requestedStart);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Plan);
        Assert.True(result.Plan.CreatesTrack);
        Assert.True(result.Plan.Command.Execute(sequence).Success);

        var track = Assert.Single(sequence.Tracks);
        Assert.Equal(TrackKind.Audio, track.Kind);
        var item = Assert.Single(track.Items);
        Assert.Equal(asset.Id, item.MediaAssetId);
        Assert.Equal(sequence.FrameRate.Snap(requestedStart), item.TimelineStart);
        Assert.Equal(asset.Duration, item.Duration);

        Assert.True(result.Plan.Command.Undo(sequence).Success);
        Assert.Empty(sequence.Tracks);
    }

    [Fact]
    public void dropping_sound_on_existing_audio_track_uses_that_track_without_creating_another()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var track = new Track { Kind = TrackKind.Music, Name = "Music 1", Order = 0 };
        sequence.Tracks.Add(track);
        var asset = CreateAudioAsset(durationSeconds: 4);
        project.MediaLibrary.Add(asset);

        var result = SoundLibraryDropPlanner.Create(project, asset.Id, 0, MediaTime.FromSeconds(3));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Plan);
        Assert.False(result.Plan.CreatesTrack);
        Assert.Equal("Music 1", result.Plan.TargetTrackName);
        Assert.True(result.Plan.Command.Execute(sequence).Success);
        Assert.Single(sequence.Tracks);
        Assert.Single(track.Items);
    }

    [Fact]
    public void sound_drop_rejects_locked_or_incompatible_targets_before_mutation()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        sequence.Tracks.Add(new Track { Kind = TrackKind.Video, Name = "V1", Order = 0 });
        sequence.Tracks.Add(new Track { Kind = TrackKind.Audio, Name = "A1", Order = 1, Locked = true });
        var asset = CreateAudioAsset(durationSeconds: 1);
        project.MediaLibrary.Add(asset);

        var videoResult = SoundLibraryDropPlanner.Create(project, asset.Id, 0, MediaTime.Zero);
        var lockedResult = SoundLibraryDropPlanner.Create(project, asset.Id, 1, MediaTime.Zero);

        Assert.False(videoResult.Success);
        Assert.Contains("audio or music track", videoResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(lockedResult.Success);
        Assert.Contains("locked", lockedResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.All(sequence.Tracks, track => Assert.Empty(track.Items));
    }

    [Fact]
    public void sound_drop_rejects_unknown_duration_without_mutation()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var asset = CreateAudioAsset(durationSeconds: 0);
        project.MediaLibrary.Add(asset);

        var result = SoundLibraryDropPlanner.Create(project, asset.Id, -1, MediaTime.Zero);

        Assert.False(result.Success);
        Assert.Contains("duration is unknown", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sequence.Tracks);
    }

    [Fact]
    public void project_fallback_assets_respect_active_catalog_filters()
    {
        var asset = CreateAudioAsset(
            durationSeconds: 1,
            originalPath: Path.Combine(Path.GetTempPath(), "cinematic-impact.wav"));

        Assert.True(new SoundLibraryCatalogQuery().MatchesProjectFallback(asset));
        Assert.True(new SoundLibraryCatalogQuery("impact").MatchesProjectFallback(asset));
        Assert.False(new SoundLibraryCatalogQuery("whoosh").MatchesProjectFallback(asset));
        Assert.False(new SoundLibraryCatalogQuery(FavoritesOnly: true).MatchesProjectFallback(asset));
        Assert.False(new SoundLibraryCatalogQuery(Category: "impact").MatchesProjectFallback(asset));
        Assert.False(new SoundLibraryCatalogQuery(RecentlyUsed: true).MatchesProjectFallback(asset));
    }

    [Theory]
    [InlineData("sound.ogg")]
    [InlineData("sound.opus")]
    [InlineData("sound.aiff")]
    [InlineData("sound.caf")]
    public void sound_library_accepts_extended_audio_extensions(string path)
    {
        Assert.True(SoundLibraryAudioPolicy.IsKnownAudioExtension(path));
    }

    [Fact]
    public void preferences_view_menu_and_sound_library_drag_contract_are_present()
    {
        var xaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var window = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var soundWindow = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Dialogs", "SoundLibraryWindow.cs"));
        var soundIntegration = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.SoundLibrary.cs"));
        var timelineDrop = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Timeline", "TimelineControl.ExternalMediaDrop.cs"));

        Assert.Contains("<MenuItem Header=\"Preferences\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<MenuItem Header=\"View\" x:Name=\"PanelsMenu\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header = \"Sound Library\"", window, StringComparison.Ordinal);
        Assert.Contains("DragDrop.DoDragDrop", soundWindow, StringComparison.Ordinal);
        Assert.Contains("Browse Libraries", soundWindow, StringComparison.Ordinal);
        Assert.Contains("AudioAssetLibraryCatalog.All", soundWindow, StringComparison.Ordinal);
        Assert.Contains("Rushframe never downloads or scrapes", soundWindow, StringComparison.Ordinal);
        Assert.Contains("_soundLibraryCatalogService.IndexFilesAsync", soundIntegration, StringComparison.Ordinal);
        Assert.Contains("CreateCatalogMediaAsset", soundIntegration, StringComparison.Ordinal);
        Assert.DoesNotContain("IsKnownAudioExtension(fullPath)", soundIntegration, StringComparison.Ordinal);
        Assert.Contains("Rushframe.MediaAssetId", timelineDrop, StringComparison.Ordinal);
        Assert.Contains("MediaDropRequested", timelineDrop, StringComparison.Ordinal);
    }

    private static MediaAsset CreateAudioAsset(double durationSeconds, string? originalPath = null) => new()
    {
        Kind = MediaKind.Audio,
        OriginalPath = originalPath ?? Path.Combine(Path.GetTempPath(), $"sound-{Guid.NewGuid():N}.wav"),
        RelativeProjectPath = "sound.wav",
        Duration = MediaTime.FromSeconds(durationSeconds),
    };

    private static string SourcePath(params string[] parts) =>
        Path.Combine(FindRepositoryRoot(), Path.Combine(parts));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
