using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;

namespace Rushframe.Desktop.Tests;

public sealed class SoundLibraryIntegrationTests
{
    [Fact]
    public void required_attribution_blocks_only_used_visible_unmuted_audio()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var used = CreateSound("used.wav", requiresAttribution: true, attribution: "");
        var unused = CreateSound("unused.wav", requiresAttribution: true, attribution: "");
        var complete = CreateSound("complete.wav", requiresAttribution: true, attribution: "Artist Name");
        project.MediaLibrary.AddRange([used, unused, complete]);
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Audio,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = used.Id,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = complete.Id,
                    TimelineStart = MediaTime.FromSeconds(1),
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });

        var issues = SoundLicenseGuard.FindIssues(project, sequence);

        var issue = Assert.Single(issues);
        Assert.Equal(used.Id, issue.MediaAssetId);
        Assert.Contains("requires an attribution", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferences > View > Sound Library", SoundLicenseGuard.FormatBlockingMessage(issues), StringComparison.Ordinal);
    }

    [Fact]
    public void hidden_track_and_muted_item_do_not_block_sound_license_guard()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var sound = CreateSound("hidden.wav", requiresAttribution: true, attribution: "");
        project.MediaLibrary.Add(sound);
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Audio,
            Hidden = true,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = sound.Id,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });
        sequence.Tracks.Add(new Track
        {
            Kind = TrackKind.Audio,
            Items =
            {
                new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    MediaAssetId = sound.Id,
                    Muted = true,
                    Duration = MediaTime.FromSeconds(1),
                    SourceDuration = MediaTime.FromSeconds(1),
                },
            },
        });

        Assert.Empty(SoundLicenseGuard.FindIssues(project, sequence));
    }

    [Fact]
    public void sound_license_update_is_exactly_undoable()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var sound = CreateSound("license.wav", requiresAttribution: false, attribution: "Old Credit");
        sound.LicenseName = "Old License";
        project.MediaLibrary.Add(sound);
        var command = new UpdateProjectMediaLicenseCommand(
            project,
            sound.Id,
            "CC BY 4.0",
            "New Credit",
            requiresAttribution: true);

        Assert.True(command.Execute(sequence).Success);
        Assert.Equal("CC BY 4.0", sound.LicenseName);
        Assert.Equal("New Credit", sound.Attribution);
        Assert.True(sound.RequiresAttribution);

        Assert.True(command.Undo(sequence).Success);
        Assert.Equal("Old License", sound.LicenseName);
        Assert.Equal("Old Credit", sound.Attribution);
        Assert.False(sound.RequiresAttribution);
    }

    [Fact]
    public void sound_catalog_identity_and_license_survive_project_roundtrip()
    {
        var project = new Project();
        var sound = new MediaAsset
        {
            Kind = MediaKind.Audio,
            OriginalPath = "C:\\sounds\\whoosh.wav",
            RelativeProjectPath = "whoosh.wav",
            FileFingerprint = "sha256-value",
            CatalogSoundId = "catalog-id",
            LicenseName = "CC BY 4.0",
            Attribution = "Artist Name",
            RequiresAttribution = true,
            IsGeneratedDerivative = false,
            Duration = MediaTime.FromSeconds(1.25),
        };
        project.MediaLibrary.Add(sound);

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        var restoredSound = Assert.Single(restored.MediaLibrary);

        Assert.Equal("sha256-value", restoredSound.FileFingerprint);
        Assert.Equal("catalog-id", restoredSound.CatalogSoundId);
        Assert.Equal("CC BY 4.0", restoredSound.LicenseName);
        Assert.Equal("Artist Name", restoredSound.Attribution);
        Assert.True(restoredSound.RequiresAttribution);
        Assert.False(restoredSound.IsGeneratedDerivative);
    }

    [Fact]
    public void drop_preview_exposes_snapped_start_duration_and_target_without_mutation()
    {
        var project = new Project();
        var sequence = Assert.IsType<Sequence>(project.MainSequence);
        var sound = CreateSound("preview.wav", durationSeconds: 2.25);
        project.MediaLibrary.Add(sound);
        var requestedStart = MediaTime.FromSeconds(1.237);

        var result = SoundLibraryDropPlanner.Create(project, sound.Id, -1, requestedStart);

        Assert.True(result.Success, result.Error);
        var plan = Assert.IsType<SoundLibraryDropPlan>(result.Plan);
        Assert.Equal(sequence.FrameRate.Snap(requestedStart), plan.SnappedStart);
        Assert.Equal(sound.Duration, plan.Duration);
        Assert.Equal("A1", plan.TargetTrackName);
        Assert.True(plan.CreatesTrack);
        Assert.Empty(sequence.Tracks);
    }

    [Fact]
    public async Task desktop_catalog_service_indexes_and_searches_through_real_python_worker()
    {
        var root = FindRepositoryRoot();
        var temp = Path.Combine(Path.GetTempPath(), $"rushframe-sound-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var wav = Path.Combine(temp, "tense_whoosh.wav");
        WriteSilentWav(wav, 0.15);
        var service = new SoundLibraryCatalogService(root, Path.Combine(temp, "catalog.sqlite"));
        try
        {
            var ffmpeg = Path.Combine(root, ".tools", "bin", "ffmpeg.exe");
            var ffprobe = Path.Combine(root, ".tools", "bin", "ffprobe.exe");
            var indexed = await service.IndexFilesAsync(
                [wav],
                File.Exists(ffmpeg) ? ffmpeg : null,
                File.Exists(ffprobe) ? ffprobe : null,
                enableSemantic: false);
            var entry = await service.GetSoundByPathAsync(wav);
            var collectionId = await service.CreateCollectionAsync("Transitions", "project-test");
            await service.AddToCollectionAsync(collectionId, entry.SoundId);
            await service.RecordUsageAsync(entry.SoundId, "project-test", "media-test");
            var search = await service.SearchAsync(new SoundLibraryCatalogQuery("tense whoosh"));
            var collectionSearch = await service.SearchAsync(new SoundLibraryCatalogQuery(CollectionId: collectionId));
            var projectSearch = await service.SearchAsync(new SoundLibraryCatalogQuery(ProjectId: "project-test"));
            var recentSearch = await service.SearchAsync(new SoundLibraryCatalogQuery(RecentlyUsed: true));
            var collections = await service.ListCollectionsAsync("project-test");
            var status = await service.GetStatusAsync();

            Assert.Single(indexed.Indexed);
            Assert.Empty(indexed.Skipped);
            Assert.Equal("tense_whoosh.wav", entry.Name);
            Assert.Equal("transition", entry.Category);
            Assert.Equal("tense", entry.Mood);
            if (File.Exists(ffmpeg))
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.WaveformPath));
                Assert.True(File.Exists(entry.WaveformPath));
            }
            Assert.Equal(entry.SoundId, Assert.Single(search.Results).SoundId);
            Assert.Equal(entry.SoundId, Assert.Single(collectionSearch.Results).SoundId);
            Assert.Equal(entry.SoundId, Assert.Single(projectSearch.Results).SoundId);
            Assert.Equal(entry.SoundId, Assert.Single(recentSearch.Results).SoundId);
            Assert.Equal(collectionId, Assert.Single(collections).CollectionId);
            Assert.Equal(1, status.SoundCount);
            Assert.Equal(1, status.OnlineCount);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task watched_sound_folder_debounces_audio_changes_and_reindexes_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rushframe-sound-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var callback = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new SoundLibraryWatchService(
            _ => Task.FromResult(new SoundLibraryCatalogStatus
            {
                SoundCount = 0,
                OnlineCount = 0,
                RootCount = 1,
                Roots =
                [
                    new SoundLibraryCatalogRoot
                    {
                        RootId = "root",
                        Path = root,
                        WatchEnabled = true,
                    },
                ],
            }),
            (path, _) =>
            {
                callback.TrySetResult(path);
                return Task.CompletedTask;
            });
        try
        {
            await watcher.RefreshAsync();
            WriteSilentWav(Path.Combine(root, "new-impact.wav"), 0.05);
            var indexedRoot = await callback.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(indexedRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task watched_sound_folder_coalesces_changes_without_overlapping_reindexes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rushframe-sound-watch-coalesce-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var calls = 0;
        using var watcher = new SoundLibraryWatchService(
            _ => Task.FromResult(new SoundLibraryCatalogStatus
            {
                RootCount = 1,
                Roots = [new SoundLibraryCatalogRoot { RootId = "root", Path = root, WatchEnabled = true }],
            }),
            async (_, _) =>
            {
                var currentActive = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, currentActive);
                var call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    firstEntered.TrySetResult();
                    await releaseFirst.Task;
                }
                Interlocked.Decrement(ref active);
                if (call == 2) secondCompleted.TrySetResult();
            });
        try
        {
            await watcher.RefreshAsync();
            WriteSilentWav(Path.Combine(root, "first.wav"), 0.05);
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            WriteSilentWav(Path.Combine(root, "second.wav"), 0.05);
            WriteSilentWav(Path.Combine(root, "third.wav"), 0.05);
            await Task.Delay(1400);
            releaseFirst.TrySetResult();
            await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await Task.Delay(1200);

            Assert.Equal(1, maximumActive);
            Assert.Equal(2, calls);
        }
        finally
        {
            releaseFirst.TrySetResult();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void sound_library_ui_and_agent_registration_contracts_are_present()
    {
        var dialog = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Dialogs", "SoundLibraryWindow.cs"));
        var integration = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.SoundLibrary.cs"));
        var bridge = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var backend = File.ReadAllText(SourcePath("rushframe_intelligence", "backend.py"));
        var catalog = File.ReadAllText(SourcePath("rushframe_intelligence", "sound_library.py"));

        foreach (var text in new[]
                 {
                     "Semantic search", "Favorites only", "Include offline", "Project used", "Recently used",
                     "New Collection", "LUFS min/max", "BPM min/max", "Selected sound waveform", "Add Folder", "Reindex", "Register",
                     "Similar", "License", "Catalog-only results must be registered",
                 })
            Assert.Contains(text, dialog, StringComparison.Ordinal);
        Assert.Contains("_soundLibraryCatalogService", integration, StringComparison.Ordinal);
        Assert.Contains("BuildAgentSoundLibraryRegistrations", integration, StringComparison.Ordinal);
        Assert.Contains("sound-library-registrations", bridge, StringComparison.Ordinal);
        Assert.Contains("rushframe.search_sfx", backend, StringComparison.Ordinal);
        Assert.Contains("registration_required_for_unregistered_results", backend, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sounds", catalog, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS embeddings", catalog, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS project_usage", catalog, StringComparison.Ordinal);
    }

    private static void WriteSilentWav(string path, double seconds, int sampleRate = 8000)
    {
        var sampleCount = Math.Max(1, (int)Math.Round(seconds * sampleRate));
        var dataLength = sampleCount * 2;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }

    private static MediaAsset CreateSound(
        string name,
        bool requiresAttribution = false,
        string attribution = "",
        double durationSeconds = 1) => new()
    {
        Kind = MediaKind.Audio,
        OriginalPath = Path.Combine(Path.GetTempPath(), name),
        RelativeProjectPath = name,
        CatalogSoundId = $"catalog-{name}",
        LicenseName = "CC BY 4.0",
        Attribution = attribution,
        RequiresAttribution = requiresAttribution,
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
