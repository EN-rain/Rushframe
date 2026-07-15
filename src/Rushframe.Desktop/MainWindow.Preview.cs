using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private static readonly (string Label, double Value)[] PreviewSpeedOptions =
    [
        ("0.25x", 0.25),
        ("0.5x", 0.5),
        ("1x", 1.0),
        ("1.5x", 1.5),
        ("2x", 2.0),
    ];

    private static readonly (string Label, double Value)[] PreviewZoomOptions =
    [
        ("Fit", 1.0),
        ("50%", 0.5),
        ("100%", 1.0),
        ("200%", 2.0),
    ];

    private double? _pendingPreviewSourceSeconds;
    private bool _smoothPlayheadUpdatesActive;
    private double _previewTransportSpeed = 1.0;
    private int _previewSpeedIndex = 2;
    private int _previewZoomIndex;

    private async Task PlayPreviewAsync()
    {
        if (_timeline?.SelectedItem != null || _isTimelineCompositePreview)
        {
            var timelineSeconds = _timeline?.PlayheadTime.Seconds ?? 0;
            if (!await EnsureTimelineCompositePreviewAsync(timelineSeconds)) return;
        }

        if (!PreviewPlayButton.IsEnabled) return;
        if (!_isRealtimeTimelinePreview && PreviewPlayer.Visibility != Visibility.Visible) return;

        if (!_isTimelineCompositePreview)
        {
            BindPreviewToSelectedTimelineItem();
            if (TryGetBoundPreviewItem(out var item))
            {
                var sourceStart = item.SourceStart.Seconds;
                var sourceEnd = sourceStart + GetSourcePlaybackDuration(item);
                var position = GetPreviewPosition().TotalSeconds;
                if (position < sourceStart - 0.001 || position >= sourceEnd - 0.001)
                    SetPreviewPosition(TimeSpan.FromSeconds(sourceStart));
            }
        }

        ApplyPreviewPlaybackSpeed();
        if (_isRealtimeTimelinePreview) StartRealtimePlayback();
        else PreviewPlayer.Play();
        _isPreviewPlaying = true;
        StartSmoothPlayheadUpdates();
        UpdatePreviewTransportDisplay();
    }

    private void PausePreview()
    {
        if (!PreviewPauseButton.IsEnabled) return;
        if (_isRealtimeTimelinePreview) PauseRealtimePlayback();
        else if (PreviewPlayer.Visibility == Visibility.Visible) PreviewPlayer.Pause();
        _isPreviewPlaying = false;
        StopSmoothPlayheadUpdates();
        UpdatePreviewTransportDisplay();
    }

    private void StopPreview()
    {
        if (_isRealtimeTimelinePreview)
        {
            PauseRealtimePlayback();
            SetRealtimePreviewPosition(0);
        }
        else
        {
            PreviewPlayer.Stop();
            SetPreviewPosition(TimeSpan.Zero);
        }
        _isPreviewPlaying = false;
        StopSmoothPlayheadUpdates();
        UpdatePreviewTransportDisplay();
    }

    private async Task TogglePreviewPlaybackAsync()
    {
        if (_isPreviewPlaying) PausePreview();
        else await PlayPreviewAsync();
    }

    private void SetPreviewControlsEnabled(bool enabled)
    {
        var hasPlayableTimeline = enabled && _isRealtimeTimelinePreview;
        var hasPlayableMedia = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewPlayButton.IsEnabled = hasPlayableTimeline || hasPlayableMedia;
        PreviewPauseButton.IsEnabled = hasPlayableTimeline || hasPlayableMedia;
        PreviewStopButton.IsEnabled = hasPlayableTimeline || hasPlayableMedia;
        PreviewPreviousFrameButton.IsEnabled = hasPlayableTimeline || hasPlayableMedia;
        PreviewNextFrameButton.IsEnabled = hasPlayableTimeline || hasPlayableMedia;
        PreviewSeekSlider.IsEnabled = enabled;
        PreviewTimeBox.IsEnabled = enabled;
        PreviewMarkInButton.IsEnabled = enabled;
        PreviewMarkOutButton.IsEnabled = enabled;
        PreviewClearMarksButton.IsEnabled = enabled;
        PreviewInsertButton.IsEnabled = enabled && _previewAsset != null;
        PreviewOverwriteButton.IsEnabled = enabled && _previewAsset != null;
        PreviewSnapshotButton.IsEnabled = enabled;
    }

    private void PreviewSelectedMedia()
    {
        if (MediaList.SelectedItem is MediaListItem selected) PreviewAsset(selected.Asset);
    }

    private async Task PreviewTimelineItemAsync(TimelineItem item)
    {
        var timelineSeconds = _timeline?.PlayheadTime.Seconds ?? item.TimelineStart.Seconds;
        if (timelineSeconds < item.TimelineStart.Seconds || timelineSeconds > item.TimelineEnd.Seconds)
            timelineSeconds = item.TimelineStart.Seconds;
        await EnsureTimelineCompositePreviewAsync(timelineSeconds);
    }

    private async Task<bool> EnsureTimelineCompositePreviewAsync(double timelineSeconds)
    {
        var sequence = _project.MainSequence;
        if (sequence == null || sequence.Duration.Seconds <= 0)
        {
            ClearPreviewSurface("Timeline has no content to preview");
            return false;
        }

        timelineSeconds = Math.Clamp(timelineSeconds, 0, sequence.Duration.Seconds);
        if (TryShowRealtimeTimelinePreview(timelineSeconds))
        {
            _timelinePreviewDirty = false;
            _timelinePreviewOffsetSeconds = 0;
            _timelinePreviewChunkEndSeconds = sequence.Duration.Seconds;
            _timelinePreviewRevision = _project.Revision;
            return true;
        }
        if (_isRealtimeTimelinePreview)
            StopRealtimeTimelinePreview(clearSurface: true);

        var previewMaxWidth = Math.Clamp(_settings.PreviewMaxWidth, 480, 1920);
        var scale = Math.Min(1.0, previewMaxWidth / (double)Math.Max(1, sequence.Width));
        var previewWidth = Math.Max(2, (int)Math.Round(sequence.Width * scale / 2) * 2);
        var previewHeight = Math.Max(2, (int)Math.Round(sequence.Height * scale / 2) * 2);
        var chunk = _exactPreviewCache.Describe(
            _project,
            sequence,
            timelineSeconds,
            previewWidth,
            previewHeight);

        if (_timelinePreviewRevision == _project.Revision
            && string.Equals(_timelinePreviewPath, chunk.Path, StringComparison.OrdinalIgnoreCase)
            && File.Exists(chunk.Path))
        {
            if (!_isTimelineCompositePreview
                || PreviewPlayer.Source == null
                || !string.Equals(PreviewPlayer.Source.LocalPath, chunk.Path, StringComparison.OrdinalIgnoreCase))
                LoadTimelineCompositePreview(chunk, timelineSeconds);
            else
                SeekPreviewSource(Math.Max(0, timelineSeconds - chunk.StartSeconds));
            _timelinePreviewDirty = false;
            return true;
        }

        _timelinePreviewRenderCancellation?.Cancel();
        _timelinePreviewRenderCancellation?.Dispose();
        _timelinePreviewRenderCancellation = new CancellationTokenSource();
        var cancellationToken = _timelinePreviewRenderCancellation.Token;

        try
        {
            PausePreview();
            SetPreviewControlsEnabled(false);
            PreviewSourceNameText.Text = "Preparing exact preview chunk...";
            StatusText.Text = $"Rendering exact preview {FormatPreviewTime(TimeSpan.FromSeconds(chunk.StartSeconds))}";

            var outputPath = await _exactPreviewCache.GetOrCreateAsync(
                chunk,
                (temporaryPath, token) => _mediaService.ExportTimelineRangeAsync(
                    _project,
                    sequence,
                    temporaryPath,
                    chunk.StartSeconds,
                    chunk.DurationSeconds,
                    token,
                    previewWidth,
                    previewHeight),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _timelinePreviewPath = outputPath;
            _timelinePreviewOffsetSeconds = chunk.StartSeconds;
            _timelinePreviewChunkEndSeconds = chunk.EndSeconds;
            _timelinePreviewRevision = chunk.Revision;
            _timelinePreviewDirty = false;
            LoadTimelineCompositePreview(chunk with { Path = outputPath }, timelineSeconds);
            StatusText.Text = "Exact preview ready";
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ClearPreviewSurface($"Timeline preview failed: {ex.Message}");
            StatusText.Text = "Timeline preview could not be rendered";
            return false;
        }
    }

    private async Task SwitchExactPreviewChunkAsync(double timelineSeconds, bool resumePlayback)
    {
        if (_isExactPreviewChunkSwitching) return;
        _isExactPreviewChunkSwitching = true;
        try
        {
            _isPreviewPlaying = false;
            StopSmoothPlayheadUpdates();
            if (!await EnsureTimelineCompositePreviewAsync(timelineSeconds)) return;
            if (resumePlayback) await PlayPreviewAsync();
        }
        finally
        {
            _isExactPreviewChunkSwitching = false;
        }
    }

    private void LoadTimelineCompositePreview(ExactPreviewChunk chunk, double timelineSeconds)
    {
        _isTimelineCompositePreview = true;
        _previewTimelineItemId = null;
        _previewTimelineItemCache = null;
        _previewAsset = null;
        _timelinePreviewOffsetSeconds = chunk.StartSeconds;
        _timelinePreviewChunkEndSeconds = chunk.EndSeconds;
        _timelinePreviewRevision = chunk.Revision;
        _pendingPreviewSourceSeconds = Math.Max(0, timelineSeconds - chunk.StartSeconds);
        ClearPreviewMarks(announce: false);
        StopPreview();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewPlayer.Visibility = Visibility.Visible;
        PreviewSourceNameText.Text = $"Exact timeline preview · {FormatPreviewTime(TimeSpan.FromSeconds(chunk.StartSeconds))}";
        LoadPreviewMedia(chunk.Path);
    }

    private void PreviewAsset(MediaAsset asset) => PreviewAsset(asset, clearTimelineSelection: true);

    private void PreviewAsset(MediaAsset asset, bool clearTimelineSelection)
    {
        _isTimelineCompositePreview = false;
        if (clearTimelineSelection)
        {
            _previewTimelineItemId = null;
            _previewTimelineItemCache = null;
        }
        _previewAsset = asset;
        ClearPreviewMarks(announce: false);
        _previewHistory.RemoveAll(item => item.Id == asset.Id);
        _previewHistory.Insert(0, asset);
        if (_previewHistory.Count > 12) _previewHistory.RemoveRange(12, _previewHistory.Count - 12);

        if (!File.Exists(asset.OriginalPath))
        {
            ClearPreviewSurface("Media file is offline");
            return;
        }

        PreviewSourceNameText.Text = Path.GetFileName(asset.OriginalPath);
        StopPreview();
        ClearPreviewMedia();
        SetPreviewSeekSliderValue(0);
        PreviewSeekSlider.Maximum = 1;
        PreviewTimeText.Text = "00:00";
        PreviewDurationText.Text = FormatPreviewTime(TimeSpan.Zero);
        SetPreviewControlsEnabled(false);

        if (asset.Kind == MediaKind.Image)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(asset.OriginalPath);
                image.EndInit();
                image.Freeze();
                PreviewImage.Source = image;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewPlayer.Visibility = Visibility.Collapsed;
                var stillDuration = asset.Duration.Seconds > 0 ? asset.Duration.Seconds : 5;
                PreviewSeekSlider.Maximum = stillDuration;
                PreviewDurationText.Text = FormatPreviewTime(TimeSpan.FromSeconds(stillDuration));
                PreviewTimeBox.Text = "00:00";
                SetPreviewControlsEnabled(true);
            }
            catch (Exception ex)
            {
                PreviewImage.Source = null;
                PreviewSourceNameText.Text = $"Image preview failed: {ex.Message}";
            }
            return;
        }

        if (asset.Kind is not (MediaKind.Video or MediaKind.Audio))
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlayer.Visibility = Visibility.Collapsed;
            PreviewSourceNameText.Text = "This media type has no source preview";
            SetPreviewControlsEnabled(false);
            return;
        }

        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewPlayer.Visibility = Visibility.Visible;
        LoadPreviewMedia(asset.OriginalPath);
    }

    private void ClearPreviewSurface(string message)
    {
        _isTimelineCompositePreview = false;
        _previewTimelineItemId = null;
        _previewTimelineItemCache = null;
        _timelinePreviewOffsetSeconds = 0;
        _timelinePreviewChunkEndSeconds = 0;
        _timelinePreviewRevision = -1;
        StopPreview();
        StopRealtimeTimelinePreview(clearSurface: true);
        ClearPreviewMedia();
        PreviewPlayer.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        SetPreviewSeekSliderValue(0);
        PreviewSeekSlider.Maximum = 1;
        PreviewTimeText.Text = "00:00";
        PreviewTimeBox.Text = "00:00";
        PreviewDurationText.Text = "00:00";
        PreviewSourceNameText.Text = message;
        SetPreviewControlsEnabled(false);
    }

    private bool IsPreviewSurfaceLoadedFor(MediaAsset asset)
    {
        if (_previewAsset?.Id != asset.Id || !File.Exists(asset.OriginalPath)) return false;

        if (asset.Kind == MediaKind.Image)
            return PreviewImage.Source != null && PreviewImage.Visibility == Visibility.Visible;

        if (asset.Kind is MediaKind.Video or MediaKind.Audio)
        {
            return PreviewPlayer.Source != null
                && string.Equals(PreviewPlayer.Source.LocalPath, asset.OriginalPath, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void OnPreviewMediaOpened()
    {
        if (!PreviewPlayer.NaturalDuration.HasTimeSpan) return;
        var duration = PreviewPlayer.NaturalDuration.TimeSpan;
        SetPreviewControlsEnabled(true);

        if (_isTimelineCompositePreview && _project.MainSequence is { } timelineSequence)
        {
            var localSeconds = Math.Clamp(
                _pendingPreviewSourceSeconds
                ?? Math.Max(0, (_timeline?.PlayheadTime.Seconds ?? _timelinePreviewOffsetSeconds) - _timelinePreviewOffsetSeconds),
                0,
                duration.TotalSeconds);
            var timelineSeconds = Math.Clamp(
                _timelinePreviewOffsetSeconds + localSeconds,
                0,
                timelineSequence.Duration.Seconds);
            _pendingPreviewSourceSeconds = null;
            SetPreviewPosition(TimeSpan.FromSeconds(localSeconds));
            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = Math.Max(0.001, timelineSequence.Duration.Seconds);
            SetPreviewSeekSliderValue(timelineSeconds);
            PreviewTimeBox.Text = FormatPreviewTime(TimeSpan.FromSeconds(timelineSeconds));
            PreviewDurationText.Text = FormatPreviewTime(TimeSpan.FromSeconds(timelineSequence.Duration.Seconds));
        }
        else if (TryGetBoundPreviewItem(out var item))
        {
            var sourceStart = Math.Clamp(
                _pendingPreviewSourceSeconds ?? item.SourceStart.Seconds,
                0,
                duration.TotalSeconds);
            _pendingPreviewSourceSeconds = null;
            SetPreviewPosition(TimeSpan.FromSeconds(sourceStart));
            PreviewSeekSlider.Minimum = item.TimelineStart.Seconds;
            PreviewSeekSlider.Maximum = Math.Max(item.TimelineEnd.Seconds, item.TimelineStart.Seconds + 0.001);
            var timelineSeconds = GetTimelineSecondsForSourcePosition(item, sourceStart);
            SetPreviewSeekSliderValue(timelineSeconds);
            PreviewTimeBox.Text = FormatPreviewTime(TimeSpan.FromSeconds(timelineSeconds));
            PreviewDurationText.Text = FormatPreviewTime(TimeSpan.FromSeconds(item.TimelineEnd.Seconds));
        }
        else
        {
            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = Math.Max(0.001, duration.TotalSeconds);
            PreviewDurationText.Text = FormatPreviewTime(duration);
            PreviewTimeBox.Text = "00:00";
        }

        UpdatePreviewProgress();
    }

    private void UpdatePreviewProgress()
    {
        if (_isRealtimeTimelinePreview)
        {
            UpdateRealtimePreviewProgressDisplay(renderFrame: false);
            return;
        }

        var position = PreviewPlayer.Visibility == Visibility.Visible
            ? GetPreviewPosition()
            : TimeSpan.Zero;
        if (PreviewPlayer.Visibility != Visibility.Visible) return;

        if (_isPreviewPlaying && _isTimelineCompositePreview && _project.MainSequence is { } timelineSequence)
        {
            var timelinePosition = _timelinePreviewOffsetSeconds + position.TotalSeconds;
            if (timelinePosition >= timelineSequence.Duration.Seconds - 0.001)
            {
                if (PreviewLoopToggle.IsChecked == true)
                {
                    _ = SwitchExactPreviewChunkAsync(0, resumePlayback: true);
                    return;
                }

                PausePreview();
                timelinePosition = timelineSequence.Duration.Seconds;
                position = TimeSpan.FromSeconds(Math.Max(0, timelinePosition - _timelinePreviewOffsetSeconds));
                SetPreviewPosition(position);
            }
            else if (!_isExactPreviewChunkSwitching
                     && timelinePosition >= _timelinePreviewChunkEndSeconds - 0.05
                     && _timelinePreviewChunkEndSeconds < timelineSequence.Duration.Seconds - 0.001)
            {
                _ = SwitchExactPreviewChunkAsync(_timelinePreviewChunkEndSeconds, resumePlayback: true);
                return;
            }
        }
        else if (_isPreviewPlaying && TryGetBoundPreviewItem(out var boundItem))
        {
            var sourceStart = boundItem.SourceStart.Seconds;
            var sourceEnd = sourceStart + GetSourcePlaybackDuration(boundItem);
            if (position.TotalSeconds >= sourceEnd)
            {
                if (PreviewLoopToggle.IsChecked == true)
                {
                    SetPreviewPosition(TimeSpan.FromSeconds(sourceStart));
                    position = GetPreviewPosition();
                }
                else
                {
                    PausePreview();
                    SetPreviewPosition(TimeSpan.FromSeconds(sourceEnd));
                    position = GetPreviewPosition();
                }
            }
        }
        else if (_isPreviewPlaying && _previewMarkOutSeconds.HasValue && position.TotalSeconds >= _previewMarkOutSeconds.Value)
        {
            if (PreviewLoopToggle.IsChecked == true)
            {
                SetPreviewPosition(TimeSpan.FromSeconds(_previewMarkInSeconds ?? 0));
                position = GetPreviewPosition();
            }
            else
            {
                PausePreview();
                SetPreviewPosition(TimeSpan.FromSeconds(_previewMarkOutSeconds.Value));
                position = GetPreviewPosition();
            }
        }

        var displayPosition = _isTimelineCompositePreview
            ? TimeSpan.FromSeconds(_timelinePreviewOffsetSeconds + position.TotalSeconds)
            : TryGetBoundPreviewItem(out var displayItem)
                ? TimeSpan.FromSeconds(GetTimelineSecondsForSourcePosition(displayItem, position.TotalSeconds))
                : position;
        if (!_isPreviewSeeking)
        {
            var sliderValue = Math.Clamp(
                displayPosition.TotalSeconds,
                PreviewSeekSlider.Minimum,
                PreviewSeekSlider.Maximum);
            if (Math.Abs(PreviewSeekSlider.Value - sliderValue) >= 0.001)
                SetPreviewSeekSliderValue(sliderValue);
        }
        var formatted = FormatPreviewTime(displayPosition);
        SetTextIfChanged(PreviewTimeText, formatted);
        if (!PreviewTimeBox.IsKeyboardFocusWithin && !string.Equals(PreviewTimeBox.Text, formatted, StringComparison.Ordinal))
            PreviewTimeBox.Text = formatted;
    }

    private void StartSmoothPlayheadUpdates()
    {
        if (_smoothPlayheadUpdatesActive) return;
        _previewScheduler.Start();
        _smoothPlayheadUpdatesActive = true;
    }

    private void StopSmoothPlayheadUpdates()
    {
        if (!_smoothPlayheadUpdatesActive) return;
        _previewScheduler.Stop();
        _smoothPlayheadUpdatesActive = false;
    }

    private void OnPreviewFrameTick()
    {
        if (!_isPreviewPlaying) return;
        if (_isRealtimeTimelinePreview)
        {
            UpdateRealtimePreviewFrame();
            if (_timeline != null)
                _timeline.PlayheadTime = MediaTime.FromSeconds(GetRealtimePreviewPositionSeconds());
            return;
        }
        if (PreviewPlayer.Visibility == Visibility.Visible)
            SyncTimelinePlayheadToPreview(GetPreviewPosition());
    }

    private void UpdatePreviewTransportDisplay() => UpdatePreviewProgress();

    private double GetPreviewTargetFramesPerSecond()
    {
        var sequenceFps = _project.MainSequence?.FrameRate.Value ?? 30;
        return Math.Clamp(
            Math.Min(sequenceFps, Math.Clamp(_settings.PreviewMaxFps, 15, 60)),
            1,
            60);
    }

    private static void SetTextIfChanged(TextBlock target, string value)
    {
        if (!string.Equals(target.Text, value, StringComparison.Ordinal)) target.Text = value;
    }

    private void LoadPreviewMedia(string sourcePath)
    {
        ClearPreviewMedia();
        PreviewPlayer.Source = new Uri(sourcePath, UriKind.Absolute);
        ApplyPreviewPlaybackSpeed();
    }

    private void ClearPreviewMedia()
    {
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
    }

    private TimeSpan GetPreviewPosition() => _isRealtimeTimelinePreview
        ? TimeSpan.FromSeconds(GetRealtimePreviewPositionSeconds())
        : PreviewPlayer.Position;

    private void SetPreviewPosition(TimeSpan position)
    {
        if (_isRealtimeTimelinePreview)
        {
            SetRealtimePreviewPosition(Math.Max(0, position.TotalSeconds));
            return;
        }
        if (PreviewPlayer.Source == null) return;
        PreviewPlayer.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private void SyncTimelinePlayheadToPreview(TimeSpan sourcePosition)
    {
        if (_timeline == null) return;
        if (_isTimelineCompositePreview)
        {
            _timeline.PlayheadTime = MediaTime.FromSeconds(
                _timelinePreviewOffsetSeconds + sourcePosition.TotalSeconds);
            return;
        }
        if (!TryGetBoundPreviewItem(out var item)) return;

        _timeline.PlayheadTime = MediaTime.FromSeconds(
            GetTimelineSecondsForSourcePosition(item, sourcePosition.TotalSeconds));
    }

    private void SeekPreviewToTimelinePlayhead()
    {
        if (_timeline == null || _project.MainSequence == null) return;

        var timelineSeconds = _timeline.PlayheadTime.Seconds;
        if (_isTimelineCompositePreview)
        {
            if (!_timelinePreviewDirty
                && _timelinePreviewRevision == _project.Revision
                && timelineSeconds >= _timelinePreviewOffsetSeconds
                && timelineSeconds < _timelinePreviewChunkEndSeconds)
                SeekPreviewSource(timelineSeconds - _timelinePreviewOffsetSeconds);
            else
                _ = SwitchExactPreviewChunkAsync(timelineSeconds, resumePlayback: false);
            return;
        }

        var item = FindPreviewItemAtTimelineTime(timelineSeconds);
        if (item?.MediaAssetId == null) return;

        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == item.MediaAssetId.Value);
        if (asset == null || asset.Kind is not (MediaKind.Video or MediaKind.Audio)) return;

        if (_isPreviewPlaying)
            PausePreview();

        _previewTimelineItemId = item.Id;
        _previewTimelineItemCache = item;
        var sourceSeconds = GetSourceSecondsForTimelinePosition(item, timelineSeconds);

        if (!IsPreviewSurfaceLoadedFor(asset))
        {
            _pendingPreviewSourceSeconds = sourceSeconds;
            PreviewAsset(asset, clearTimelineSelection: false);
            return;
        }

        SeekPreviewSource(sourceSeconds);
    }

    private TimelineItem? FindPreviewItemAtTimelineTime(double timelineSeconds)
    {
        static bool Contains(TimelineItem item, double time) =>
            time >= item.TimelineStart.Seconds && time <= item.TimelineEnd.Seconds;

        if (TryGetBoundPreviewItem(out var bound) && Contains(bound, timelineSeconds))
            return bound;

        if (_timeline?.SelectedItem is { } selected && Contains(selected, timelineSeconds))
            return selected;

        return _project.MainSequence?.Tracks
            .Where(track => !track.Muted && track.Kind is TrackKind.Video or TrackKind.Audio)
            .Reverse()
            .SelectMany(track => track.Items)
            .FirstOrDefault(item => item.MediaAssetId.HasValue && Contains(item, timelineSeconds));
    }

    private void BindPreviewToSelectedTimelineItem()
    {
        if (_previewTimelineItemId != null || _previewAsset == null || _timeline?.SelectedItem == null) return;
        if (_timeline.SelectedItem.MediaAssetId != _previewAsset.Id) return;
        _previewTimelineItemId = _timeline.SelectedItem.Id;
        _previewTimelineItemCache = _timeline.SelectedItem;
    }

    private bool TryGetBoundPreviewItem(out TimelineItem item)
    {
        item = null!;
        if (_previewTimelineItemId == null) return false;
        if (_previewTimelineItemCache?.Id == _previewTimelineItemId.Value)
        {
            item = _previewTimelineItemCache;
            return true;
        }

        var found = _project.MainSequence?.Tracks
            .SelectMany(track => track.Items)
            .FirstOrDefault(candidate => candidate.Id == _previewTimelineItemId.Value);
        if (found == null) return false;
        _previewTimelineItemCache = found;
        item = found;
        return true;
    }

    private static double GetEffectiveClipSpeed(TimelineItem item)
    {
        var speed = item.SpeedCurve?.ConstantSpeed ?? item.Speed;
        return speed > 0 ? speed : 1;
    }

    private static double GetSourcePlaybackDuration(TimelineItem item) =>
        Math.Max(0.001, item.Duration.Seconds * GetEffectiveClipSpeed(item));

    private static double GetTimelineSecondsForSourcePosition(TimelineItem item, double sourceSeconds)
    {
        var sourceOffset = Math.Max(0, sourceSeconds - item.SourceStart.Seconds);
        var timelineOffset = sourceOffset / GetEffectiveClipSpeed(item);
        return Math.Clamp(
            item.TimelineStart.Seconds + timelineOffset,
            item.TimelineStart.Seconds,
            item.TimelineEnd.Seconds);
    }

    private static double GetSourceSecondsForTimelinePosition(TimelineItem item, double timelineSeconds)
    {
        var timelineOffset = Math.Clamp(
            timelineSeconds - item.TimelineStart.Seconds,
            0,
            item.Duration.Seconds);
        return item.SourceStart.Seconds + timelineOffset * GetEffectiveClipSpeed(item);
    }

    private void ApplyPreviewPlaybackSpeed()
    {
        var clipSpeed = TryGetBoundPreviewItem(out var item) ? GetEffectiveClipSpeed(item) : 1.0;
        PreviewPlayer.SpeedRatio = Math.Clamp(clipSpeed * _previewTransportSpeed, 0.1, 16.0);
        ApplyRealtimeAudioSettings();
    }

    private static string FormatPreviewTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void SeekPreview(double seconds)
    {
        if (_isRealtimeTimelinePreview)
        {
            SetRealtimePreviewPosition(seconds);
            return;
        }
        if (PreviewPlayer.Visibility != Visibility.Visible) return;

        if (TryGetBoundPreviewItem(out var item))
        {
            var timelineSeconds = Math.Clamp(seconds, item.TimelineStart.Seconds, item.TimelineEnd.Seconds);
            var timelineOffset = timelineSeconds - item.TimelineStart.Seconds;
            var sourceSeconds = item.SourceStart.Seconds + timelineOffset * GetEffectiveClipSpeed(item);
            SeekPreviewSource(sourceSeconds);
            if (_timeline != null)
                _timeline.PlayheadTime = MediaTime.FromSeconds(timelineSeconds);
            return;
        }

        if (_isTimelineCompositePreview)
        {
            if (seconds >= _timelinePreviewOffsetSeconds && seconds < _timelinePreviewChunkEndSeconds)
                SeekPreviewSource(seconds - _timelinePreviewOffsetSeconds);
            else
                _ = SwitchExactPreviewChunkAsync(seconds, resumePlayback: _isPreviewPlaying);
            if (_timeline != null) _timeline.PlayheadTime = MediaTime.FromSeconds(seconds);
            return;
        }

        SeekPreviewSource(seconds);
    }

    private void SeekPreviewSource(double sourceSeconds)
    {
        var maxSource = PreviewPlayer.NaturalDuration.HasTimeSpan
            ? PreviewPlayer.NaturalDuration.TimeSpan.TotalSeconds
            : Math.Max(sourceSeconds, 0);
        var clamped = Math.Clamp(sourceSeconds, 0, Math.Max(0, maxSource));
        SetPreviewPosition(TimeSpan.FromSeconds(clamped));
        UpdatePreviewProgress();
    }

    private void SetPreviewSeekSliderValue(double value) =>
        _previewSeekRequestGate.ApplyProgrammaticValue(() => PreviewSeekSlider.Value = value);

    private void StepPreviewFrame(int direction)
    {
        PausePreview();
        var fps = _project.MainSequence?.FrameRate.Value > 0
            ? _project.MainSequence.FrameRate.Value
            : 30;
        if (_isTimelineCompositePreview)
        {
            SeekPreview(PreviewTimelineSeekMath.GetFrameStepTargetSeconds(
                _timeline?.PlayheadTime.Seconds,
                _timelinePreviewOffsetSeconds,
                GetPreviewPosition().TotalSeconds,
                direction,
                fps));
            return;
        }
        SeekPreviewSource(GetPreviewPosition().TotalSeconds + direction / fps);
    }

    private void SetPreviewMark(bool isIn)
    {
        var value = Math.Clamp(GetPreviewPosition().TotalSeconds, 0, PreviewSeekSlider.Maximum);
        if (isIn)
        {
            _previewMarkInSeconds = value;
            if (_previewMarkOutSeconds.HasValue && _previewMarkOutSeconds.Value < value)
                _previewMarkOutSeconds = null;
        }
        else
        {
            _previewMarkOutSeconds = value;
            if (_previewMarkInSeconds.HasValue && _previewMarkInSeconds.Value > value)
                _previewMarkInSeconds = null;
        }
        UpdatePreviewMarkLabels();
        StatusText.Text = isIn
            ? $"Mark In set at {FormatPreviewTime(TimeSpan.FromSeconds(value))}"
            : $"Mark Out set at {FormatPreviewTime(TimeSpan.FromSeconds(value))}";
    }

    private void ClearPreviewMarks(bool announce = true)
    {
        _previewMarkInSeconds = null;
        _previewMarkOutSeconds = null;
        UpdatePreviewMarkLabels();
        if (announce) StatusText.Text = "Source marks cleared";
    }

    private void UpdatePreviewMarkLabels()
    {
        PreviewMarkInText.Text = _previewMarkInSeconds.HasValue
            ? $"In {FormatPreviewTime(TimeSpan.FromSeconds(_previewMarkInSeconds.Value))}"
            : "In --:--";
        PreviewMarkOutText.Text = _previewMarkOutSeconds.HasValue
            ? $"Out {FormatPreviewTime(TimeSpan.FromSeconds(_previewMarkOutSeconds.Value))}"
            : "Out --:--";
    }

    private void AddPreviewRangeToTimeline(bool overwrite)
    {
        if (_previewAsset == null || _timeline == null || _project.MainSequence == null) return;
        var seq = _project.MainSequence;
        var sourceStart = Math.Clamp(_previewMarkInSeconds ?? 0, 0, PreviewSeekSlider.Maximum);
        var sourceEnd = Math.Clamp(_previewMarkOutSeconds ?? PreviewSeekSlider.Maximum, sourceStart, PreviewSeekSlider.Maximum);
        var durationSeconds = sourceEnd - sourceStart;
        if (durationSeconds <= 0.001)
        {
            StatusText.Text = "Cannot edit source range: Mark Out must be after Mark In";
            return;
        }

        var trackKind = _previewAsset.Kind switch
        {
            MediaKind.Audio => TrackKind.Audio,
            MediaKind.Image => TrackKind.Overlay,
            _ => TrackKind.Video,
        };
        var itemKind = _previewAsset.Kind == MediaKind.Image ? ItemKind.Image : ItemKind.Clip;
        var commands = new List<IEditCommand>();
        var track = seq.Tracks.FirstOrDefault(t => t.Kind == trackKind && !t.Locked);
        if (track == null)
        {
            track = new Track
            {
                Kind = trackKind,
                Name = trackKind == TrackKind.Audio ? "A1" : trackKind == TrackKind.Overlay ? "O1" : "V1",
                Order = seq.Tracks.Count,
            };
            commands.Add(new AddPreparedTrackCommand { Track = track });
        }

        var timelineStart = _timeline.PlayheadTime;
        var duration = MediaTime.FromSeconds(durationSeconds);
        if (overwrite)
        {
            var overwriteEnd = timelineStart.Add(duration);
            var overlapping = track.Items
                .Where(item => item.TimelineStart < overwriteEnd && item.TimelineStart.Add(item.Duration) > timelineStart)
                .ToList();
            if (overlapping.Any(item => item.Locked))
            {
                StatusText.Text = "Overwrite blocked: an overlapping item is locked";
                return;
            }
            if (overlapping.Count > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    $"Overwrite will replace {overlapping.Count} item(s) on {track.Name}. Continue?",
                    "Confirm Overwrite",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Overwrite canceled";
                    return;
                }
            }

            commands.AddRange(overlapping.Select(existing =>
                (IEditCommand)new DeleteClipCommand { ItemId = existing.Id }));
        }

        commands.Add(new AddClipCommand
        {
            TrackId = track.Id,
            Item = new TimelineItem
            {
                Kind = itemKind,
                MediaAssetId = _previewAsset.Id,
                TimelineStart = timelineStart,
                Duration = duration,
                SourceStart = MediaTime.FromSeconds(sourceStart),
                SourceDuration = duration,
            },
        });
        if (Execute(new CompositeEditCommand(
                overwrite ? "Overwrite source range" : "Insert source range",
                commands)))
            StatusText.Text = overwrite ? "Source range overwritten at playhead" : "Source range inserted at playhead";
    }

    private void CyclePreviewSpeed()
    {
        _previewSpeedIndex = (_previewSpeedIndex + 1) % PreviewSpeedOptions.Length;
        ApplyPreviewSpeed();
    }

    private void ApplyPreviewSpeed()
    {
        var (label, speed) = PreviewSpeedOptions[_previewSpeedIndex];
        PreviewSpeedButton.ToolTip = $"Playback speed: {label}. Click to cycle.";
        _previewTransportSpeed = Math.Clamp(speed, 0.1, 8.0);
        ApplyPreviewPlaybackSpeed();
    }

    private void CyclePreviewZoom()
    {
        _previewZoomIndex = (_previewZoomIndex + 1) % PreviewZoomOptions.Length;
        ApplyPreviewZoom();
    }

    private void ApplyPreviewZoom()
    {
        var (label, zoom) = PreviewZoomOptions[_previewZoomIndex];
        PreviewZoomButton.ToolTip = $"Monitor zoom: {label}. Click to cycle.";
        PreviewScaleTransform.ScaleX = zoom;
        PreviewScaleTransform.ScaleY = zoom;
    }

    private async Task SavePreviewSnapshotAsync()
    {
        if (!_isRealtimeTimelinePreview
            && !_isTimelineCompositePreview
            && (_previewAsset == null || !File.Exists(_previewAsset.OriginalPath))) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"rushframe-frame-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            if (_isRealtimeTimelinePreview)
            {
                SaveRealtimePreviewSnapshot(dialog.FileName);
                StatusText.Text = $"Timeline snapshot saved: {Path.GetFileName(dialog.FileName)}";
                return;
            }

            if (_isTimelineCompositePreview && !string.IsNullOrWhiteSpace(_timelinePreviewPath))
            {
                await _mediaService.GenerateThumbnailAsync(
                    new ThumbnailRequest(_timelinePreviewPath, dialog.FileName, GetPreviewPosition()));
            }
            else if (_previewAsset!.Kind == MediaKind.Image)
            {
                File.Copy(_previewAsset.OriginalPath, dialog.FileName, overwrite: true);
            }
            else
            {
                await _mediaService.GenerateThumbnailAsync(
                    new ThumbnailRequest(_previewAsset.OriginalPath, dialog.FileName, GetPreviewPosition()));
            }
            StatusText.Text = $"Snapshot saved at source resolution: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Snapshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Snapshot failed";
        }
    }

    private void TogglePreviewFullscreen()
    {
        _previewFullscreen = !_previewFullscreen;
        if (_previewFullscreen)
        {
            Panel.SetZIndex(PreviewBorder, 1000);
            Grid.SetColumn(PreviewBorder, 0);
            Grid.SetColumnSpan(PreviewBorder, 5);
            Grid.SetRow(PreviewBorder, 0);
            Grid.SetRowSpan(PreviewBorder, 3);
            PreviewFullscreenIcon.Data = Geometry.Parse("M5,2 L5,6 L1,6 M13,2 L13,6 L17,6 M5,16 L5,12 L1,12 M13,16 L13,12 L17,12");
            PreviewFullscreenButton.ToolTip = "Exit fullscreen Preview";
        }
        else
        {
            Panel.SetZIndex(PreviewBorder, 0);
            ApplyLayout();
            PreviewFullscreenIcon.Data = Geometry.Parse("M3,7 L3,3 L7,3 M11,3 L15,3 L15,7 M15,11 L15,15 L11,15 M7,15 L3,15 L3,11");
            PreviewFullscreenButton.ToolTip = "Enter fullscreen Preview";
        }
    }

    private async void OnPreviewKeyboardShortcut(object sender, KeyEventArgs args)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (_actionRegistry.Matches("preview.play-pause", args, _settings.Keybindings))
        {
            if (_isPreviewPlaying) PausePreview(); else await PlayPreviewAsync();
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.previous-frame", args, _settings.Keybindings))
        {
            StepPreviewFrame(-1);
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.next-frame", args, _settings.Keybindings))
        {
            StepPreviewFrame(1);
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.mark-in", args, _settings.Keybindings))
        {
            SetPreviewMark(true);
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.mark-out", args, _settings.Keybindings))
        {
            SetPreviewMark(false);
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.seek-backward", args, _settings.Keybindings))
        {
            PausePreview();
            if (_isTimelineCompositePreview)
                SeekPreview(_timelinePreviewOffsetSeconds + GetPreviewPosition().TotalSeconds - 1);
            else
                SeekPreviewSource(GetPreviewPosition().TotalSeconds - 1);
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.pause", args, _settings.Keybindings))
        {
            PausePreview();
            args.Handled = true;
        }
        else if (_actionRegistry.Matches("preview.play", args, _settings.Keybindings))
        {
            await PlayPreviewAsync();
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && _previewFullscreen)
        {
            TogglePreviewFullscreen();
            args.Handled = true;
        }
    }

    private static bool TryParsePreviewTime(string text, out double seconds)
    {
        seconds = 0;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
        {
            seconds = raw;
            return true;
        }
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time))
        {
            seconds = time.TotalSeconds;
            return true;
        }
        return false;
    }
}
