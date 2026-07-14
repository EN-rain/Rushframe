using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Rushframe.Application;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Infrastructure;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private async void ImportMedia_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Media Files (*.mp4;*.mov;*.avi;*.wav;*.mp3;*.png;*.jpg;*.jpeg)|*.mp4;*.mov;*.avi;*.wav;*.mp3;*.png;*.jpg;*.jpeg",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        SetMediaOperationState(true, $"Importing {dialog.FileNames.Length} media file(s)…");
        try
        {
            var importedAssets = new List<MediaAsset>(dialog.FileNames.Length);
            foreach (var file in dialog.FileNames)
            {
                var duration = MediaTime.Zero;
                var pixelWidth = 0;
                var pixelHeight = 0;
                try
                {
                    var probe = await _mediaService.ProbeAsync(file);
                    duration = MediaTime.FromSeconds(probe.Duration.TotalSeconds);
                    var videoStream = probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
                    pixelWidth = videoStream?.Width ?? 0;
                    pixelHeight = videoStream?.Height ?? 0;
                }
                catch
                {
                    // Keep import usable without FFmpeg; probing can be retried later.
                }

                importedAssets.Add(new MediaAsset
                {
                    Kind = GetMediaKind(file),
                    OriginalPath = file,
                    RelativeProjectPath = file,
                    Duration = duration,
                    PixelWidth = pixelWidth,
                    PixelHeight = pixelHeight,
                });
            }
            using (var mutation = _saveCoordinator.BeginMutation())
            {
                _project.MediaLibrary.AddRange(importedAssets);
                _project.IncrementRevision();
            }
            RefreshMediaList();
            MarkProjectDirty("Media imported");
        }
        finally
        {
            SetMediaOperationState(false, "Import complete");
        }
    }

    private IReadOnlyList<float>? ResolveWaveformPeaks(MediaAssetId assetId)
    {
        if (_waveformPeaks.TryGetValue(assetId, out var cached)) return cached;

        var path = Path.Combine(_appData, "Cache", "waveforms", $"{assetId}.peaks.json");
        if (!File.Exists(path)) return null;
        try
        {
            var peaks = JsonSerializer.Deserialize<float[]>(File.ReadAllText(path));
            if (peaks is not { Length: > 0 }) return null;
            _waveformPeaks[assetId] = peaks;
            return peaks;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void RelinkMedia_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (MediaList.SelectedItem is not MediaListItem selected) return;
        var dialog = new OpenFileDialog { Filter = "Media Files|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.wav;*.mp3;*.aac;*.m4a;*.flac;*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files|*.*" };
        if (dialog.ShowDialog() != true) return;

        var replacement = new MediaAsset
        {
            Id = selected.Asset.Id,
            Kind = GetMediaKind(dialog.FileName),
            OriginalPath = dialog.FileName,
            RelativeProjectPath = dialog.FileName,
            Duration = selected.Asset.Duration,
            PixelWidth = selected.Asset.PixelWidth,
            PixelHeight = selected.Asset.PixelHeight,
            IsOffline = false,
        };
        using (var mutation = _saveCoordinator.BeginMutation())
        {
            var index = _project.MediaLibrary.FindIndex(a => a.Id == selected.Asset.Id);
            if (index >= 0) _project.MediaLibrary[index] = replacement;
            _project.IncrementRevision();
        }
        RefreshMediaList();
        MarkProjectDirty("Media relinked");
        AddRenderQueueMessage($"Relinked: {Path.GetFileName(dialog.FileName)}");
    }

    private async void GenerateMediaCache_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (MediaList.SelectedItem is not MediaListItem selected) return;
        var asset = selected.Asset;
        if (!File.Exists(asset.OriginalPath))
        {
            AddRenderQueueMessage($"Cache skipped, offline: {Path.GetFileName(asset.OriginalPath)}");
            return;
        }

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rushframe", "Cache");
        Directory.CreateDirectory(appData);
        SetMediaOperationState(true, $"Generating cache for {Path.GetFileName(asset.OriginalPath)}…");
        try
        {
            if (asset.Kind is MediaKind.Video or MediaKind.Image)
                await _mediaService.GenerateThumbnailAsync(new(asset.OriginalPath, Path.Combine(appData, "thumbnails", $"{asset.Id}.jpg"), TimeSpan.FromSeconds(1)));
            if (asset.Kind is MediaKind.Video)
                await _mediaService.GenerateProxyAsync(new(asset.OriginalPath, Path.Combine(appData, "proxy", $"{asset.Id}.mp4"), 540));
            if (asset.Kind is MediaKind.Video or MediaKind.Audio)
            {
                await _mediaService.GenerateWaveformAsync(new(asset.OriginalPath, Path.Combine(appData, "waveforms", $"{asset.Id}.png")));
                var peaks = await _mediaService.GenerateWaveformPeaksAsync(asset.OriginalPath, 4096);
                var peaksPath = Path.Combine(appData, "waveforms", $"{asset.Id}.peaks.json");
                Directory.CreateDirectory(Path.GetDirectoryName(peaksPath)!);
                await File.WriteAllTextAsync(peaksPath, JsonSerializer.Serialize(peaks));
                _waveformPeaks[asset.Id] = peaks;
            }

            AddRenderQueueMessage($"Cache generated: {Path.GetFileName(asset.OriginalPath)}");
            RefreshMediaList();
        }
        catch (Exception ex)
        {
            AddRenderQueueMessage($"Cache failed: {ex.Message}");
        }
        finally
        {
            SetMediaOperationState(false, "Cache operation finished");
        }
    }

    private void ApplyMediaIntelligenceProfileToControls(MediaIntelligenceRunProfile profile)
    {
        AnalyzeScenesToggle.IsChecked = profile.AnalyzeScenes;
        AnalyzeTranscriptToggle.IsChecked = profile.TranscribeSpeech;
        AnalyzeMusicToggle.IsChecked = profile.AnalyzeAudio;
        AnalyzeGeminiToggle.IsChecked = profile.AnalyzeVisuals;
        AnalyzeAlignmentToggle.IsChecked = profile.AlignWords;
        AnalyzeOcrToggle.IsChecked = false;
        AnalyzeDiarizationToggle.IsChecked = false;
        AnalyzeAudioEventsToggle.IsChecked = false;
        BuildEmbeddingsToggle.IsChecked = false;
        UpdateMediaIntelligenceFeatureDependencies();
    }

    private void UpdateMediaIntelligenceResponsiveLayout()
    {
        var availableWidth = MediaIntelligenceScrollViewer.ViewportWidth > 0
            ? MediaIntelligenceScrollViewer.ViewportWidth
            : MediaIntelligenceTab.ActualWidth;
        var columns = AdaptiveWindowService.GetDensePanelColumnCount(availableWidth);
        MediaIntelligenceFeatureGrid.Columns = columns;
        MediaIntelligenceModelGrid.Columns = columns;
    }

    private void UpdateMediaIntelligenceFeatureDependencies()
    {
        var transcriptEnabled = AnalyzeTranscriptToggle.IsChecked == true;
        var audioEnabled = AnalyzeMusicToggle.IsChecked == true;
        var visualsEnabled = AnalyzeGeminiToggle.IsChecked == true;
        var idle = !_isMediaOperationRunning;
        AnalyzeScenesToggle.IsEnabled = idle;
        AnalyzeTranscriptToggle.IsEnabled = idle;
        AnalyzeMusicToggle.IsEnabled = idle;
        AnalyzeGeminiToggle.IsEnabled = idle;
        AnalyzeOcrToggle.IsEnabled = idle;
        BuildEmbeddingsToggle.IsEnabled = idle;
        AnalyzeAlignmentToggle.IsEnabled = MediaIntelligenceUiPolicy.CanUseTranscriptFeature(transcriptEnabled, _isMediaOperationRunning);
        AnalyzeDiarizationToggle.IsEnabled = MediaIntelligenceUiPolicy.CanUseTranscriptFeature(transcriptEnabled, _isMediaOperationRunning);
        AnalyzeAudioEventsToggle.IsEnabled = MediaIntelligenceUiPolicy.CanUseAudioFeature(audioEnabled, _isMediaOperationRunning);
        VisualProviderCombo.IsEnabled = MediaIntelligenceUiPolicy.CanChooseVisualProvider(visualsEnabled, _isMediaOperationRunning);
        WhisperModelCombo.IsEnabled = transcriptEnabled && idle;
    }

    private void UpdateMediaIntelligenceActionState()
    {
        var asset = ResolveMediaIntelligenceAsset();
        var online = asset is { IsOffline: false } && File.Exists(asset.OriginalPath);
        var target = asset == null ? null : ResolveMediaIntelligenceTarget(asset.Id);
        var analysisPath = asset == null
            ? null
            : Path.Combine(GetMediaAnalysisOutputDirectory(asset), "media-analysis.json");
        var hasAnalysisFile = analysisPath != null && File.Exists(analysisPath);
        var loadedAnalysis = asset == null
            ? null
            : _project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == asset.Id);
        var idle = !_isMediaOperationRunning;

        RunMediaIntelligenceButton.IsEnabled = idle && online;
        ApplyMediaIntelligenceButton.IsEnabled = idle && target != null && (loadedAnalysis != null || hasAnalysisFile);
        SearchMediaContextButton.IsEnabled = idle && loadedAnalysis?.Moments.Count > 0;
        FindHooksButton.IsEnabled = idle && loadedAnalysis?.Moments.Count > 0;
        OpenMediaAnalysisButton.IsEnabled = idle && asset != null;
        MediaContextSearchBox.IsEnabled = idle && loadedAnalysis?.Moments.Count > 0;
        UpdateMediaIntelligenceFeatureDependencies();
    }

    private Task RunMediaIntelligenceAsync() => RunMediaIntelligenceAsync(profile: null);

    private async Task RunMediaIntelligenceAsync(MediaIntelligenceRunProfile? profile)
    {
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            OpenUtilityPanel(PanelId.MediaIntelligence, MediaIntelligenceTab);
            AddMediaIntelligenceMessage("Select a media file or timeline clip first.");
            return;
        }

        if (!File.Exists(asset.OriginalPath))
        {
            AddMediaIntelligenceMessage($"Analysis skipped, offline: {Path.GetFileName(asset.OriginalPath)}");
            return;
        }

        var outputDir = GetMediaAnalysisOutputDirectory(asset);
        Directory.CreateDirectory(outputDir);

        if (_isMediaOperationRunning) return;
        var operationCancellation = new CancellationTokenSource();
        _operationCancellation?.Dispose();
        _operationCancellation = operationCancellation;
        SetMediaOperationState(true, $"Analyzing {Path.GetFileName(asset.OriginalPath)}…");
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;
        CancelOperationButton.Visibility = Visibility.Visible;
        OpenUtilityPanel(PanelId.MediaIntelligence, MediaIntelligenceTab);
        var analysisName = profile?.Name ?? "Custom media intelligence";
        AddMediaIntelligenceMessage($"{analysisName}: {Path.GetFileName(asset.OriginalPath)}");
        AddMediaIntelligenceMessage($"Output: {outputDir}");

        try
        {
            var repoRoot = FindRepoRoot();
            var ffmpegPath = ResolveFfmpegPath(repoRoot);
            var model = GetSelectedWhisperModel();
            var args = new List<string>
            {
                "-m", "rushframe_intelligence", "analyze",
                asset.OriginalPath,
                outputDir,
                "--ffmpeg", ffmpegPath,
                "--whisper-model", model,
                "--max-input-seconds", Math.Clamp(_settings.MaxAiInputSeconds, 30, 1800).ToString(CultureInfo.InvariantCulture),
            };
            if (profile != null)
            {
                profile.AppendArguments(args, GetSelectedVisualProvider());
            }
            else
            {
                new MediaIntelligenceRunOptions(
                    AnalyzeScenesToggle.IsChecked == true,
                    AnalyzeTranscriptToggle.IsChecked == true,
                    AnalyzeMusicToggle.IsChecked == true,
                    AnalyzeGeminiToggle.IsChecked == true,
                    AnalyzeOcrToggle.IsChecked == true,
                    AnalyzeAlignmentToggle.IsChecked == true,
                    AnalyzeDiarizationToggle.IsChecked == true,
                    AnalyzeAudioEventsToggle.IsChecked == true,
                    BuildEmbeddingsToggle.IsChecked == true)
                    .AppendArguments(args, GetSelectedVisualProvider());
            }

            var result = await RunPythonAsync(args, repoRoot, operationCancellation.Token);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput)) AddMediaIntelligenceMessage(result.StandardOutput.Trim());
            if (!string.IsNullOrWhiteSpace(result.StandardError)) AddMediaIntelligenceMessage(result.StandardError.Trim());

            if (result.ExitCode != 0)
            {
                AddMediaIntelligenceMessage($"Analysis failed with exit code {result.ExitCode}.");
                StatusText.Text = $"Media analysis failed ({result.ExitCode})";
                return;
            }

            var analysisPath = Path.Combine(outputDir, "media-analysis.json");
            SummarizeMediaAnalysis(analysisPath);
            await StoreMediaIntelligenceAnalysisAsync(analysisPath, asset);
            AddMediaIntelligenceMessage("Analysis is ready. Use Apply to add scene markers and captions to the timeline.");
            AddRenderQueueMessage($"Media intelligence complete: {Path.GetFileName(asset.OriginalPath)}");
            StatusText.Text = "Media analysis ready";
        }
        catch (OperationCanceledException)
        {
            AddMediaIntelligenceMessage("Analysis canceled.");
            StatusText.Text = "Media analysis canceled";
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Analysis failed: {ex.Message}");
            StatusText.Text = "Media analysis failed";
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = true;
            CancelOperationButton.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(_operationCancellation, operationCancellation))
                _operationCancellation = null;
            operationCancellation.Dispose();
            SetMediaOperationState(false);
        }
    }

    private async Task ApplyCurrentMediaIntelligenceToTimelineAsync()
    {
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            AddMediaIntelligenceMessage("Select a media item or a timeline clip first.");
            return;
        }

        var analysis = _project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == asset.Id);
        if (analysis == null)
        {
            var analysisPath = Path.Combine(GetMediaAnalysisOutputDirectory(asset), "media-analysis.json");
            if (!File.Exists(analysisPath))
            {
                AddMediaIntelligenceMessage("Run analysis first or import an existing media-analysis.json file.");
                return;
            }
            analysis = await _mediaIntelligenceImportService.ImportAsync(analysisPath, asset);
        }

        ApplyMediaIntelligenceToTimeline(analysis, asset);
    }

    private async void ImportMediaIntelligence_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenUtilityPanel(PanelId.MediaIntelligence, MediaIntelligenceTab);
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            MessageBox.Show(this, "Select a media item or timeline clip first.", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import AI Analysis",
            Filter = "Media analysis JSON|media-analysis.json;*.json|JSON files|*.json|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        if (_isMediaOperationRunning) return;
        SetMediaOperationState(true, $"Importing analysis for {Path.GetFileName(asset.OriginalPath)}…");
        try
        {
            await StoreMediaIntelligenceAnalysisAsync(dialog.FileName, asset);
            AddMediaIntelligenceMessage($"Imported analysis for {Path.GetFileName(asset.OriginalPath)}. Use Apply to add it to the timeline.");
            StatusText.Text = "Media analysis imported";
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Could not import analysis: {ex.Message}");
            StatusText.Text = "Media analysis import failed";
        }
        finally
        {
            SetMediaOperationState(false);
        }
    }

    private async Task StoreMediaIntelligenceAnalysisAsync(string analysisPath, MediaAsset asset)
    {
        var analysis = await _mediaIntelligenceImportService.ImportAsync(analysisPath, asset);
        using var mutation = _saveCoordinator.BeginMutation();
        MediaIntelligenceImportService.StoreInProject(_project, analysis);
        _project.IncrementRevision();
        if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
        MarkProjectDirty("Media analysis imported");
        RefreshAutomationPanels();
        UpdateMediaIntelligenceActionState();
    }

    private void ApplyMediaIntelligenceToTimeline(MediaIntelligenceAnalysis analysis, MediaAsset asset)
    {
        if (_isMediaOperationRunning) return;

        SetMediaOperationState(true, $"Applying analysis for {Path.GetFileName(asset.OriginalPath)}…");
        try
        {
            var target = ResolveMediaIntelligenceTarget(asset.Id);
            if (target == null)
            {
                var missingTargetMessage = $"Analysis saved, but '{Path.GetFileName(asset.OriginalPath)}' is not on the timeline yet.";
                AddMediaIntelligenceMessage(missingTargetMessage);
                StatusText.Text = missingTargetMessage;
                return;
            }
            var projectSnapshot = MediaIntelligenceProjectMutationGuard.Capture(
                _project,
                analysis.MediaAssetId);
            using var mutation = _saveCoordinator.BeginMutation();
            MediaIntelligenceImportService.StoreInProject(_project, analysis);
            var command = new ApplyMediaIntelligenceCommand
            {
                TargetItemId = target.Id,
                Analysis = analysis,
                AddSceneMarkers = true,
                AddCaptionClips = true,
            };
            if (!Execute(command))
            {
                MediaIntelligenceProjectMutationGuard.Restore(_project, projectSnapshot);
                UpdateMediaIntelligenceActionState();
                return;
            }

            var message = $"Timeline updated: {command.CreatedMarkerCount} scene markers and {command.CreatedCaptionCount} caption clips.";
            AddMediaIntelligenceMessage(message);
            StatusText.Text = message;
            _timeline?.ScrollToTime(target.TimelineStart);
            RefreshAutomationPanels();
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Could not apply analysis: {ex.Message}");
            StatusText.Text = "Media analysis could not be applied";
        }
        finally
        {
            SetMediaOperationState(false);
        }
    }

    private MediaAsset? ResolveMediaIntelligenceAsset()
    {
        if (_timeline?.SelectedItem?.MediaAssetId is MediaAssetId timelineAssetId)
            return _project.MediaLibrary.FirstOrDefault(asset => asset.Id == timelineAssetId);
        return (MediaList.SelectedItem as MediaListItem)?.Asset;
    }

    private TimelineItem? ResolveMediaIntelligenceTarget(MediaAssetId assetId)
    {
        if (_timeline?.SelectedItem is { } selected && selected.MediaAssetId == assetId) return selected;
        return _project.MainSequence?.Tracks.SelectMany(track => track.Items).FirstOrDefault(item => item.MediaAssetId == assetId);
    }

    private void OpenSelectedMediaAnalysisOutput()
    {
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            AddMediaIntelligenceMessage("Select a media file or timeline clip first.");
            return;
        }

        try
        {
            var outputDir = GetMediaAnalysisOutputDirectory(asset);
            Directory.CreateDirectory(outputDir);
            Process.Start(new ProcessStartInfo { FileName = outputDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Could not open analysis results: {ex.Message}");
            StatusText.Text = "Analysis results could not be opened";
        }
    }

    private void SearchMediaContext(bool findHooks)
    {
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            AddMediaIntelligenceMessage("Select analyzed media or a timeline clip first.");
            return;
        }

        var analysis = _project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == asset.Id);
        if (analysis == null || analysis.Moments.Count == 0)
        {
            AddMediaIntelligenceMessage("No searchable editing moments are loaded. Run or import media intelligence first.");
            return;
        }

        IReadOnlyList<MediaMomentSearchResult> results = findHooks
            ? _mediaIntelligenceSearchService.FindHooks(analysis, limit: 8)
            : _mediaIntelligenceSearchService.Search(analysis, new MediaMomentSearchQuery(MediaContextSearchBox.Text, Limit: 12));

        MediaIntelligenceList.Items.Clear();
        MediaIntelligenceEmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaIntelligenceEmptyText.Text = results.Count == 0 ? "No matching moments" : "Select media, then run analysis";
        foreach (var result in results)
        {
            var roles = result.Roles.Count > 0 ? string.Join(", ", result.Roles) : "context";
            MediaIntelligenceList.Items.Add($"{FormatPreviewTime(TimeSpan.FromSeconds(result.Start.Seconds))}–{FormatPreviewTime(TimeSpan.FromSeconds(result.End.Seconds))}  [{roles}]  {result.Summary}");
        }
    }

    private void AddMediaIntelligenceMessage(string message)
    {
        MediaIntelligenceList.Items.Add(message);
        MediaIntelligenceEmptyText.Visibility = Visibility.Collapsed;
        MediaIntelligenceList.ScrollIntoView(message);
    }

    private string GetMediaAnalysisOutputDirectory(MediaAsset asset) =>
        Path.Combine(_appData, "analysis", "media-intelligence", asset.Id.ToString());

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{fileName} did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task<ProcessResult> RunPythonAsync(
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var managedPython = Path.Combine(workingDirectory, ".tools", "intelligence-venv", "Scripts", "python.exe");
        foreach (var launcher in new[] { managedPython, "py", "python" })
        {
            if (Path.IsPathFullyQualified(launcher) && !File.Exists(launcher)) continue;
            var psi = new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (launcher == "py") psi.ArgumentList.Add("-3");
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            var geminiApiKey = SecretProtectionService.Unprotect(_settings.ProtectedGeminiApiKey);
            if (!string.IsNullOrWhiteSpace(geminiApiKey)) psi.Environment["GEMINI_API_KEY"] = geminiApiKey;

            try
            {
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Python did not start.");
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                    throw;
                }
                return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
            }
            catch (Win32Exception)
            {
                continue;
            }
        }
        throw new InvalidOperationException("Python was not found. Install Python or add it to PATH.");
    }

    private void SummarizeMediaAnalysis(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            AddMediaIntelligenceMessage("Analysis finished, but media-analysis.json was not created.");
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;
        var scenes = CountArray(root, "scenes");
        var transcript = CountArray(root, "transcript");
        var moments = CountArray(root, "moments");
        var duplicateTakes = CountArray(root, "duplicate_take_groups");
        var warnings = CountArray(root, "warnings");
        AddMediaIntelligenceMessage($"Complete: {scenes} scenes, {transcript} transcript segments, {moments} editing moments, {duplicateTakes} repeated-take groups, {warnings} warnings.");
        AddMediaIntelligenceMessage(jsonPath);
    }

    private string GetSelectedWhisperModel() =>
        MediaIntelligenceUiPolicy.NormalizeWhisperModel(
            (WhisperModelCombo.SelectedItem as ComboBoxItem)?.Tag as string);

    private string GetSelectedVisualProvider() =>
        VisualProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string value ? value : "gemini";

    private static int CountArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static string ResolveFfmpegPath(string repoRoot)
    {
        var local = Path.Combine(repoRoot, ".tools", "bin", "ffmpeg.exe");
        return File.Exists(local) ? local : "ffmpeg";
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "rushframe_intelligence", "pipeline.py"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        return Environment.CurrentDirectory;
    }

    private async void ExtractAudio_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selectedInspectorItem?.MediaAssetId == null) return;
        var source = _project.MediaLibrary.FirstOrDefault(a => a.Id == _selectedInspectorItem.MediaAssetId.Value);
        if (source == null || !File.Exists(source.OriginalPath)) return;

        var output = Path.Combine(Path.GetDirectoryName(source.OriginalPath)!, $"{Path.GetFileNameWithoutExtension(source.OriginalPath)}_audio.wav");
        SetMediaOperationState(true, $"Extracting audio from {Path.GetFileName(source.OriginalPath)}…");
        try
        {
            await _mediaService.ExtractAudioAsync(source.OriginalPath, output);
            using (var mutation = _saveCoordinator.BeginMutation())
            {
                _project.MediaLibrary.Add(new MediaAsset
                {
                    Kind = MediaKind.Audio,
                    OriginalPath = output,
                    RelativeProjectPath = output,
                    Duration = source.Duration,
                });
                _project.IncrementRevision();
            }
            RefreshMediaList();
            MarkProjectDirty("Extracted audio added to media library");
            AddRenderQueueMessage($"Extracted audio: {Path.GetFileName(output)}");
        }
        catch (Exception ex)
        {
            AddRenderQueueMessage($"Extract audio failed: {ex.Message}");
        }
        finally
        {
            SetMediaOperationState(false, "Audio extraction finished");
        }
    }
}
