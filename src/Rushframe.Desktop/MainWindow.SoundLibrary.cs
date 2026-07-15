using System.IO;
using System.Windows;
using Rushframe.Desktop.Dialogs;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Timeline;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private SoundLibraryWindow? _soundLibraryWindow;

    private void ShowSoundLibrary()
    {
        if (_soundLibraryWindow is { IsLoaded: true })
        {
            if (_soundLibraryWindow.WindowState == WindowState.Minimized)
                _soundLibraryWindow.WindowState = WindowState.Normal;
            _soundLibraryWindow.RefreshAssets();
            _soundLibraryWindow.Activate();
            return;
        }

        _soundLibraryWindow = new SoundLibraryWindow(
            this,
            () => _project.MediaLibrary.Where(asset => asset.Kind == MediaKind.Audio).ToArray(),
            (query, cancellationToken) => _soundLibraryCatalogService.SearchAsync(query, cancellationToken),
            cancellationToken => _soundLibraryCatalogService.GetStatusAsync(cancellationToken),
            () => _project.Id.ToString(),
            (projectId, cancellationToken) => _soundLibraryCatalogService.ListCollectionsAsync(projectId, cancellationToken),
            (name, projectId, cancellationToken) => _soundLibraryCatalogService.CreateCollectionAsync(name, projectId, cancellationToken),
            (collectionId, soundId, cancellationToken) => _soundLibraryCatalogService.AddToCollectionAsync(collectionId, soundId, cancellationToken),
            (collectionId, soundId, cancellationToken) => _soundLibraryCatalogService.RemoveFromCollectionAsync(collectionId, soundId, cancellationToken),
            ImportSoundsIntoProjectAsync,
            IndexSoundFolderAsync,
            ReindexSoundFoldersAsync,
            RegisterCatalogSoundAsync,
            SetCatalogSoundFavoriteAsync,
            UpdateCatalogSoundLicenseAsync,
            asset => AddSoundToTimeline(asset, -1, _timeline?.PlayheadTime ?? MediaTime.Zero),
            PreviewAsset,
            PreviewCatalogSound);
        _soundLibraryWindow.Closed += (_, _) => _soundLibraryWindow = null;
        _soundLibraryWindow.Show();
    }

    private async Task<SoundLibraryImportResult> ImportSoundsIntoProjectAsync(IReadOnlyList<string> filePaths)
    {
        if (_isMediaOperationRunning)
            return new SoundLibraryImportResult([], ["Another media operation is already running."]);

        var projectContext = CaptureProjectOperationContext();
        SetMediaOperationState(true, $"Indexing {filePaths.Count} sound(s)…");
        try
        {
            var normalizedPaths = new List<string>();
            var errors = new List<string>();
            foreach (var requestedPath in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fullPath = Path.GetFullPath(requestedPath);
                    if (!File.Exists(fullPath))
                    {
                        errors.Add($"{Path.GetFileName(fullPath)}: file not found.");
                        continue;
                    }
                    normalizedPaths.Add(fullPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{requestedPath}: invalid path ({ex.Message}).");
                }
            }

            if (normalizedPaths.Count == 0)
                return new SoundLibraryImportResult([], errors);

            var repoRoot = FindRepoRoot();
            var indexResult = await _soundLibraryCatalogService.IndexFilesAsync(
                normalizedPaths,
                ResolveFfmpegPath(repoRoot),
                ResolveFfprobePath(repoRoot),
                enableSemantic: true);
            errors.AddRange(indexResult.Warnings.Select(warning =>
                $"{Path.GetFileName(warning.Path)}: {warning.Message}"));

            if (!IsCurrentProjectOperation(projectContext))
            {
                return new SoundLibraryImportResult(
                    [],
                    [.. errors, "The originating project is no longer open; indexed sounds were not registered."]);
            }

            var existingAssets = new List<MediaAsset>();
            var newAssets = new List<MediaAsset>();
            foreach (var path in normalizedPaths)
            {
                try
                {
                    var entry = await _soundLibraryCatalogService.GetSoundByPathAsync(path);
                    var existing = FindRegisteredCatalogSound(projectContext.Project, entry);
                    if (existing != null)
                    {
                        existingAssets.Add(existing);
                        continue;
                    }
                    newAssets.Add(CreateCatalogMediaAsset(entry));
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: catalog registration failed ({ex.Message}).");
                }
            }

            if (newAssets.Count > 0)
            {
                if (!IsCurrentProjectOperation(projectContext))
                {
                    return new SoundLibraryImportResult(
                        existingAssets,
                        [.. errors, "The originating project is no longer open; no new sounds were registered."]);
                }

                var commands = newAssets.Select(asset =>
                    (IEditCommand)new AddProjectMediaAssetCommand(projectContext.Project, asset));
                if (!Execute(new CompositeEditCommand($"Register {newAssets.Count} catalog sound(s)", commands)))
                {
                    errors.Add("Rushframe rejected the sound-library registration.");
                    newAssets.Clear();
                }
                else
                {
                    RefreshMediaList();
                    StatusText.Text = $"Indexed and registered {newAssets.Count} sound(s)";
                }
            }

            return new SoundLibraryImportResult(
                existingAssets.Concat(newAssets).DistinctBy(asset => asset.Id).ToArray(),
                errors);
        }
        finally
        {
            SetMediaOperationState(false);
        }
    }

    private async Task<SoundLibraryIndexResult> IndexSoundFolderAsync(
        string folder,
        CancellationToken cancellationToken)
    {
        var repoRoot = FindRepoRoot();
        var result = await _soundLibraryCatalogService.IndexRootAsync(
            folder,
            ResolveFfmpegPath(repoRoot),
            ResolveFfprobePath(repoRoot),
            enableSemantic: true,
            watchEnabled: true,
            cancellationToken);
        await _soundLibraryWatchService.RefreshAsync(cancellationToken);
        return result;
    }

    private async Task<SoundLibraryIndexResult> ReindexSoundFoldersAsync(CancellationToken cancellationToken)
    {
        var status = await _soundLibraryCatalogService.GetStatusAsync(cancellationToken);
        var aggregate = new SoundLibraryIndexResult();
        var repoRoot = FindRepoRoot();
        foreach (var root in status.Roots.Where(candidate => candidate.WatchEnabled))
        {
            var result = await _soundLibraryCatalogService.IndexRootAsync(
                root.Path,
                ResolveFfmpegPath(repoRoot),
                ResolveFfprobePath(repoRoot),
                enableSemantic: true,
                watchEnabled: true,
                cancellationToken);
            aggregate.Indexed.AddRange(result.Indexed);
            aggregate.Duplicates.AddRange(result.Duplicates);
            aggregate.Skipped.AddRange(result.Skipped);
            aggregate.Warnings.AddRange(result.Warnings);
            aggregate.Roots.Clear();
            aggregate.Roots.AddRange(result.Roots);
        }
        return aggregate;
    }

    private async Task<MediaAsset?> RegisterCatalogSoundAsync(
        SoundLibraryCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        if (_isMediaOperationRunning)
            throw new InvalidOperationException("Another media operation is already running.");
        if (entry.Offline || !File.Exists(entry.Path))
            throw new InvalidOperationException("The selected catalog sound is offline.");

        var existing = FindRegisteredCatalogSound(_project, entry);
        if (existing != null) return existing;

        var context = CaptureProjectOperationContext();
        SetMediaOperationState(true, $"Registering {entry.Name}…");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProjectOperation(context))
                throw new InvalidOperationException("The originating project is no longer open.");
            var asset = CreateCatalogMediaAsset(entry);
            if (!Execute(new AddProjectMediaAssetCommand(context.Project, asset)))
                throw new InvalidOperationException("Rushframe rejected the project media registration.");
            RefreshMediaList();
            StatusText.Text = $"Registered catalog sound: {entry.Name}";
            return asset;
        }
        finally
        {
            SetMediaOperationState(false);
        }
    }

    private Task SetCatalogSoundFavoriteAsync(
        SoundLibraryCatalogEntry entry,
        bool favorite,
        CancellationToken cancellationToken) =>
        _soundLibraryCatalogService.SetFavoriteAsync(entry.SoundId, favorite, cancellationToken);

    private async Task UpdateCatalogSoundLicenseAsync(
        SoundLibraryCatalogEntry entry,
        string licenseName,
        string attribution,
        bool requiresAttribution,
        CancellationToken cancellationToken)
    {
        var registered = FindRegisteredCatalogSounds(_project, entry);
        await _soundLibraryCatalogService.UpdateLicenseAsync(
            entry.SoundId,
            licenseName,
            attribution,
            requiresAttribution,
            cancellationToken);
        if (registered.Count == 0) return;

        var commands = registered.Select(asset =>
            (IEditCommand)new UpdateProjectMediaLicenseCommand(
                _project,
                asset.Id,
                licenseName,
                attribution,
                requiresAttribution));
        if (!Execute(new CompositeEditCommand($"Update license for {registered.Count} sound registration(s)", commands)))
        {
            try
            {
                await _soundLibraryCatalogService.UpdateLicenseAsync(
                    entry.SoundId,
                    entry.LicenseName,
                    entry.Attribution,
                    entry.RequiresAttribution,
                    cancellationToken);
            }
            catch
            {
                throw new InvalidOperationException(
                    "Project metadata could not be updated, and the catalog license rollback also failed.");
            }
            throw new InvalidOperationException("Project metadata could not be updated; the catalog change was rolled back.");
        }
        RefreshMediaList();
    }

    private void PreviewCatalogSound(SoundLibraryCatalogEntry entry)
    {
        if (entry.Offline || !File.Exists(entry.Path))
        {
            StatusText.Text = $"Catalog sound is offline: {entry.Name}";
            return;
        }
        PreviewAsset(CreateCatalogMediaAsset(entry));
    }

    private static MediaAsset CreateCatalogMediaAsset(SoundLibraryCatalogEntry entry) => new()
    {
        Kind = MediaKind.Audio,
        OriginalPath = entry.Path,
        RelativeProjectPath = entry.Path,
        FileFingerprint = entry.ContentHash,
        CatalogSoundId = entry.SoundId,
        LicenseName = entry.LicenseName,
        Attribution = entry.Attribution,
        RequiresAttribution = entry.RequiresAttribution,
        IsGeneratedDerivative = !string.IsNullOrWhiteSpace(entry.DerivativePath)
                                && PathsReferToSameFile(entry.Path, entry.DerivativePath),
        Duration = MediaTime.FromSeconds(Math.Max(0, entry.Duration)),
        IsOffline = entry.Offline,
    };

    private static MediaAsset? FindRegisteredCatalogSound(Project project, SoundLibraryCatalogEntry entry) =>
        FindRegisteredCatalogSounds(project, entry).FirstOrDefault();

    private static IReadOnlyList<MediaAsset> FindRegisteredCatalogSounds(Project project, SoundLibraryCatalogEntry entry) =>
        project.MediaLibrary.Where(asset =>
            asset.Kind == MediaKind.Audio
            && ((!string.IsNullOrWhiteSpace(asset.CatalogSoundId)
                 && asset.CatalogSoundId.Equals(entry.SoundId, StringComparison.OrdinalIgnoreCase))
                || PathsReferToSameFile(asset.OriginalPath, entry.Path)
                || (!string.IsNullOrWhiteSpace(asset.FileFingerprint)
                    && asset.FileFingerprint.Equals(entry.ContentHash, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

    private static string ResolveFfprobePath(string repoRoot)
    {
        var local = Path.Combine(repoRoot, ".tools", "bin", "ffprobe.exe");
        return File.Exists(local) ? local : "ffprobe";
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private object BuildAgentSoundLibraryRegistrations() => new
    {
        ok = true,
        assets = _project.MediaLibrary
            .Where(asset => asset.Kind == MediaKind.Audio)
            .Select(asset => new
            {
                mediaAssetId = asset.Id.ToString(),
                catalogSoundId = asset.CatalogSoundId,
                path = asset.OriginalPath,
                offline = asset.IsOffline || !File.Exists(asset.OriginalPath),
                licenseName = asset.LicenseName,
                requiresAttribution = asset.RequiresAttribution,
                attributionPresent = !string.IsNullOrWhiteSpace(asset.Attribution),
            })
            .ToArray(),
    };

    private void HandleTimelineMediaDragPreview(object? sender, TimelineMediaDragPreviewEventArgs args)
    {
        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == args.MediaAssetId);
        if (asset == null)
        {
            args.Accepted = false;
            args.Message = "The sound is not registered in this project.";
            StatusText.Text = args.Message;
            return;
        }

        var result = SoundLibraryDropPlanner.Create(_project, asset.Id, args.TrackIndex, args.TimelineStart);
        args.Accepted = result.Success;
        if (result.Plan == null)
        {
            args.Message = result.Error ?? "The sound cannot be dropped here.";
            StatusText.Text = args.Message;
            return;
        }

        var trackAction = result.Plan.CreatesTrack ? $"create {result.Plan.TargetTrackName}" : result.Plan.TargetTrackName;
        args.Message = $"Drop {Path.GetFileName(asset.OriginalPath)} on {trackAction} at {FormatPreviewTime(TimeSpan.FromSeconds(result.Plan.SnappedStart.Seconds))} · {result.Plan.Duration.Seconds:0.###}s";
        StatusText.Text = args.Message;
    }

    private void HandleTimelineMediaDrop(object? sender, TimelineMediaDropRequestedEventArgs args)
    {
        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == args.MediaAssetId);
        if (asset == null)
        {
            StatusText.Text = "The dropped sound is not registered in this project";
            return;
        }
        AddSoundToTimeline(asset, args.TrackIndex, args.TimelineStart);
    }

    private void AddSoundToTimeline(MediaAsset asset, int trackIndex, MediaTime timelineStart)
    {
        if (asset.Kind != MediaKind.Audio)
        {
            StatusText.Text = "Only audio assets can be added from the sound library";
            return;
        }
        if (!File.Exists(asset.OriginalPath))
        {
            StatusText.Text = $"Sound is offline: {Path.GetFileName(asset.OriginalPath)}";
            return;
        }

        var result = SoundLibraryDropPlanner.Create(_project, asset.Id, trackIndex, timelineStart);
        if (!result.Success || result.Plan == null)
        {
            StatusText.Text = result.Error ?? "The sound could not be added to the timeline";
            return;
        }

        if (!Execute(result.Plan.Command)) return;

        StatusText.Text = $"Added {Path.GetFileName(asset.OriginalPath)} to {result.Plan.TargetTrackName} at {FormatPreviewTime(TimeSpan.FromSeconds(result.Plan.SnappedStart.Seconds))}";
        PreviewAsset(asset);
        if (!string.IsNullOrWhiteSpace(asset.CatalogSoundId))
            _ = RecordSoundUsageAsync(asset);
    }

    private async Task RecordSoundUsageAsync(MediaAsset asset)
    {
        try
        {
            await _soundLibraryCatalogService.RecordUsageAsync(
                asset.CatalogSoundId,
                _project.Id.ToString(),
                asset.Id.ToString());
        }
        catch
        {
            // Usage history is non-critical and must never invalidate a successful edit.
        }
    }
}
