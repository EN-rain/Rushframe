using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Rushframe.Desktop.Controllers;
using Rushframe.Desktop.Dialogs;
using Rushframe.Desktop.Panels;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<WorkflowStageListItem> _workflowStageItems = [];
    private readonly ObservableCollection<TranscriptAssetListItem> _transcriptAssetItems = [];
    private readonly ObservableCollection<TranscriptSegmentListItem> _transcriptSegmentItems = [];
    private readonly ObservableCollection<OutputVariantListItem> _outputVariantItems = [];
    private readonly ObservableCollection<RenderReceiptListItem> _renderReceiptItems = [];
    private readonly ObservableCollection<GeneratedCompositionListItem> _generatedCompositionItems = [];
    private bool _automationPanelsInitialized;

    private void InitializeAutomationPanels()
    {
        if (_automationPanelsInitialized) return;
        _automationPanelsInitialized = true;

        WorkflowStageList.ItemsSource = _workflowStageItems;
        TranscriptAssetCombo.ItemsSource = _transcriptAssetItems;
        TranscriptSegmentList.ItemsSource = _transcriptSegmentItems;
        OutputVariantList.ItemsSource = _outputVariantItems;
        RenderReceiptList.ItemsSource = _renderReceiptItems;
        GeneratedCompositionList.ItemsSource = _generatedCompositionItems;

        WorkflowRefreshButton.Click += (_, _) => RefreshAutomationPanels();
        WorkflowApproveButton.Click += (_, _) => SetSelectedWorkflowStage(approve: true, complete: false, reset: false);
        WorkflowCompleteButton.Click += (_, _) => SetSelectedWorkflowStage(approve: false, complete: true, reset: false);
        WorkflowResetButton.Click += (_, _) => SetSelectedWorkflowStage(approve: false, complete: false, reset: true);

        TranscriptAssetCombo.SelectionChanged += (_, _) => RefreshTranscriptSegments();
        TranscriptSegmentList.SelectionChanged += (_, _) => UpdateTranscriptButtons();
        TranscriptFilterBox.TextChanged += (_, _) => RefreshTranscriptSegments();
        TranscriptCreateClipButton.Click += (_, _) => CreateClipFromSelectedTranscript();
        TranscriptCreateCaptionsButton.Click += (_, _) => CreateCaptionsFromSelectedTranscript();
        TranscriptBestMomentsButton.Click += (_, _) => AssembleBestMomentsFromTranscript();
        TranscriptRemoveSilenceButton.Click += (_, _) => RemoveSilenceFromSelectedTimelineItem();

        AddPortraitVariantButton.Click += (_, _) => AddOutputVariantPreset("Vertical Social", 1080, 1920, 5, 10, 30, 5);
        AddLandscapeVariantButton.Click += (_, _) => AddOutputVariantPreset("Landscape", 1920, 1080, 5, 5, 10, 5);
        AddSquareVariantButton.Click += (_, _) => AddOutputVariantPreset("Square Social", 1080, 1080, 5, 8, 22, 8);
        RefreshVariantsButton.Click += (_, _) => RefreshVariantsAndReceipts();
        RenderVariantButton.Click += async (_, _) => await RenderSelectedVariantAsync();
        OpenRenderReceiptButton.Click += (_, _) => OpenSelectedRenderReceipt();

        RegisterRemotionButton.Click += (_, _) => RegisterLocalComposition(ExternalCompositionKind.Remotion);
        RegisterHyperFramesButton.Click += (_, _) => RegisterLocalComposition(ExternalCompositionKind.HyperFrames);
        RenderCompositionButton.Click += async (_, _) => await RenderSelectedCompositionAsync();
        OpenCompositionOutputButton.Click += (_, _) => OpenSelectedCompositionOutput();

        RefreshAutomationPanels();
    }

    private void RefreshAutomationPanels()
    {
        if (!_automationPanelsInitialized) return;
        RefreshWorkflowPanel();
        RefreshTranscriptAssets();
        RefreshVariantsAndReceipts();
        RefreshGeneratedCompositions();
    }

    private void RefreshWorkflowPanel()
    {
        _project.Workflow.EnsureDefaults();
        var selectedId = (WorkflowStageList.SelectedItem as WorkflowStageListItem)?.Stage.Id;
        _workflowStageItems.Clear();
        for (var index = 0; index < _project.Workflow.Stages.Count; index++)
        {
            var item = new WorkflowStageListItem(index + 1, _project.Workflow.Stages[index]);
            _workflowStageItems.Add(item);
            if (item.Stage.Id == selectedId) WorkflowStageList.SelectedItem = item;
        }
        WorkflowStageList.SelectedItem ??= _workflowStageItems.FirstOrDefault(item => item.Stage.Id == _project.Workflow.ActiveStageId)
                                                  ?? _workflowStageItems.FirstOrDefault();
        WorkflowStatusText.Text = $"Active: {_project.Workflow.ActiveStageId}  •  Decisions: {_project.Workflow.Decisions.Count}  •  "
                                  + $"Budget: {FormatBudget(_project.Workflow.ActualSpendUsd, _project.Workflow.BudgetLimitUsd)}";
    }

    private void SetSelectedWorkflowStage(bool approve, bool complete, bool reset)
    {
        if (WorkflowStageList.SelectedItem is not WorkflowStageListItem selected) return;
        var stage = selected.Stage;
        using var mutation = _saveCoordinator.BeginMutation();
        if (reset)
        {
            stage.Status = ProductionStageStatus.Ready;
            stage.StartedUtc = null;
            stage.CompletedUtc = null;
            stage.ApprovedUtc = null;
            stage.ApprovedBy = null;
            stage.Warnings.Clear();
        }
        else if (approve)
        {
            stage.Status = ProductionStageStatus.Approved;
            stage.ApprovedUtc = DateTimeOffset.UtcNow;
            stage.ApprovedBy = "local-user";
            stage.StartedUtc ??= DateTimeOffset.UtcNow;
        }
        else if (complete)
        {
            if (stage.RequiresApproval && stage.ApprovedUtc == null)
            {
                StatusText.Text = $"Approve '{stage.Name}' before completing it";
                return;
            }
            stage.Status = ProductionStageStatus.Completed;
            stage.StartedUtc ??= DateTimeOffset.UtcNow;
            stage.CompletedUtc = DateTimeOffset.UtcNow;
            ReadyNextWorkflowStage(stage);
        }
        stage.Revision++;
        _project.Workflow.ActiveStageId = stage.Id;
        _project.IncrementRevision();
        MarkProjectDirty("Production workflow updated");
        RefreshWorkflowPanel();
        StatusText.Text = $"Workflow stage updated: {stage.Name} — {stage.Status}";
    }

    private void RefreshTranscriptAssets()
    {
        var selectedAssetId = (TranscriptAssetCombo.SelectedItem as TranscriptAssetListItem)?.Analysis.MediaAssetId;
        _transcriptAssetItems.Clear();
        foreach (var analysis in _project.MediaIntelligence.OrderBy(value => ResolveMediaName(value.MediaAssetId)))
        {
            var item = new TranscriptAssetListItem(ResolveMediaName(analysis.MediaAssetId), analysis);
            _transcriptAssetItems.Add(item);
            if (selectedAssetId.HasValue && item.Analysis.MediaAssetId == selectedAssetId.Value)
                TranscriptAssetCombo.SelectedItem = item;
        }
        TranscriptAssetCombo.SelectedItem ??= _transcriptAssetItems.FirstOrDefault();
        RefreshTranscriptSegments();
    }

    private void RefreshTranscriptSegments()
    {
        _transcriptSegmentItems.Clear();
        if (TranscriptAssetCombo.SelectedItem is not TranscriptAssetListItem selected)
        {
            TranscriptEmptyText.Visibility = Visibility.Visible;
            UpdateTranscriptButtons();
            return;
        }
        var filter = TranscriptFilterBox.Text.Trim();
        foreach (var segment in selected.Analysis.Transcript)
        {
            if (!string.IsNullOrWhiteSpace(filter)
                && !segment.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !(segment.Speaker?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            _transcriptSegmentItems.Add(new TranscriptSegmentListItem(segment));
        }
        TranscriptEmptyText.Visibility = _transcriptSegmentItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTranscriptButtons();
    }

    private void UpdateTranscriptButtons()
    {
        var hasAnalysis = TranscriptAssetCombo.SelectedItem is TranscriptAssetListItem;
        TranscriptCreateClipButton.IsEnabled = hasAnalysis && TranscriptSegmentList.SelectedItems.Count > 0;
        TranscriptCreateCaptionsButton.IsEnabled = hasAnalysis;
        TranscriptBestMomentsButton.IsEnabled = hasAnalysis;
        TranscriptRemoveSilenceButton.IsEnabled = _timeline?.SelectedItem?.MediaAssetId != null;
    }

    private void CreateClipFromSelectedTranscript()
    {
        if (TranscriptAssetCombo.SelectedItem is not TranscriptAssetListItem selected) return;
        var segments = TranscriptSegmentList.SelectedItems.Cast<TranscriptSegmentListItem>().Select(item => item.Segment).OrderBy(item => item.Start).ToArray();
        if (segments.Length == 0)
        {
            StatusText.Text = "Select one or more transcript segments";
            return;
        }
        ExecuteLocalAutomationAction(
            "create_clip_from_transcript",
            new Dictionary<string, object?>
            {
                ["media_asset_id"] = selected.Analysis.MediaAssetId.ToString(),
                ["source_start"] = segments[0].Start.Seconds,
                ["source_end"] = segments[^1].End.Seconds,
                ["timeline_start"] = _timeline?.PlayheadTime.Seconds ?? 0,
            });
    }

    private void CreateCaptionsFromSelectedTranscript()
    {
        if (TranscriptAssetCombo.SelectedItem is not TranscriptAssetListItem selected) return;
        var selectedSegments = TranscriptSegmentList.SelectedItems.Cast<TranscriptSegmentListItem>().Select(item => item.Segment).OrderBy(item => item.Start).ToArray();
        var sourceStart = selectedSegments.Length > 0 ? selectedSegments[0].Start.Seconds : 0;
        var sourceEnd = selectedSegments.Length > 0 ? selectedSegments[^1].End.Seconds : selected.Analysis.Metadata.Duration.Seconds;
        ExecuteLocalAutomationAction(
            "add_captions_from_transcript",
            new Dictionary<string, object?>
            {
                ["media_asset_id"] = selected.Analysis.MediaAssetId.ToString(),
                ["source_start"] = sourceStart,
                ["source_end"] = sourceEnd,
                ["timeline_start"] = _timeline?.PlayheadTime.Seconds ?? 0,
                ["words_per_chunk"] = _project.TranscriptEditPolicy.CaptionWordsPerChunk,
                ["font_size"] = 54,
                ["font_bold"] = true,
                ["outline_width"] = 3,
            });
    }

    private void AssembleBestMomentsFromTranscript()
    {
        if (TranscriptAssetCombo.SelectedItem is not TranscriptAssetListItem selected) return;
        ExecuteLocalAutomationAction(
            "assemble_best_moments",
            new Dictionary<string, object?>
            {
                ["media_asset_id"] = selected.Analysis.MediaAssetId.ToString(),
                ["timeline_start"] = _timeline?.PlayheadTime.Seconds ?? 0,
                ["count"] = 5,
                ["maximum_duration"] = 30,
            });
    }

    private void RemoveSilenceFromSelectedTimelineItem()
    {
        var selected = _timeline?.SelectedItem;
        if (selected == null)
        {
            StatusText.Text = "Select a timeline clip with analyzed audio first";
            return;
        }
        ExecuteLocalAutomationAction(
            "remove_silence",
            new Dictionary<string, object?>
            {
                ["item_id"] = selected.Id.ToString(),
                ["minimum_silence"] = _project.TranscriptEditPolicy.MinimumSilenceCutSeconds,
                ["ripple"] = true,
            });
    }

    private void ExecuteLocalAutomationAction(string action, Dictionary<string, object?> values)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        values["action"] = action;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values, AgentPayloadReader.JsonOptions));
        var result = _agentEditCommandFactory.Build(_project, sequence, document.RootElement, action, _timeline?.PlayheadTime.Seconds ?? 0);
        if (!result.Success || result.Command == null)
        {
            StatusText.Text = result.Error ?? "The transcript edit could not be created";
            return;
        }
        Execute(result.Command);
        _timelinePreviewDirty = true;
        _timeline?.InvalidateVisual();
        RefreshAutomationPanels();
        StatusText.Text = result.Summary;
    }

    private void AddOutputVariantPreset(
        string name,
        int width,
        int height,
        double safeTop,
        double safeRight,
        double safeBottom,
        double safeLeft)
    {
        var existing = _project.ExportVariants.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        using var mutation = _saveCoordinator.BeginMutation();
        if (existing == null)
        {
            _project.ExportVariants.Add(new ExportVariant
            {
                Name = name,
                SequenceId = _project.MainSequence?.Id,
                Width = width,
                Height = height,
                FrameRate = _project.MainSequence?.FrameRate,
                SafeAreaTopPercent = safeTop,
                SafeAreaRightPercent = safeRight,
                SafeAreaBottomPercent = safeBottom,
                SafeAreaLeftPercent = safeLeft,
                Status = ExportVariantStatus.Ready,
            });
        }
        else
        {
            existing.Width = width;
            existing.Height = height;
            existing.SafeAreaTopPercent = safeTop;
            existing.SafeAreaRightPercent = safeRight;
            existing.SafeAreaBottomPercent = safeBottom;
            existing.SafeAreaLeftPercent = safeLeft;
            existing.Status = ExportVariantStatus.Ready;
        }
        _project.IncrementRevision();
        MarkProjectDirty("Output variant added");
        RefreshVariantsAndReceipts();
        OpenUtilityPanel(PanelId.OutputVariants, OutputVariantsTab);
    }

    private void RefreshVariantsAndReceipts()
    {
        var selectedVariantId = (OutputVariantList.SelectedItem as OutputVariantListItem)?.Variant.Id;
        _outputVariantItems.Clear();
        foreach (var variant in _project.ExportVariants)
        {
            var item = new OutputVariantListItem(variant);
            _outputVariantItems.Add(item);
            if (variant.Id == selectedVariantId) OutputVariantList.SelectedItem = item;
        }
        OutputVariantList.SelectedItem ??= _outputVariantItems.FirstOrDefault();

        var selectedReceiptId = (RenderReceiptList.SelectedItem as RenderReceiptListItem)?.Receipt.ReceiptId;
        _renderReceiptItems.Clear();
        foreach (var receipt in _project.RenderReceipts.OrderByDescending(item => item.CreatedUtc))
        {
            var item = new RenderReceiptListItem(receipt);
            _renderReceiptItems.Add(item);
            if (receipt.ReceiptId == selectedReceiptId) RenderReceiptList.SelectedItem = item;
        }
        RenderVariantButton.IsEnabled = _outputVariantItems.Count > 0;
        OpenRenderReceiptButton.IsEnabled = _renderReceiptItems.Count > 0;
    }

    private (Project Project, Sequence Sequence) CreateVariantRenderContext(ExportVariant variant)
    {
        var clone = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(_project));
        var sequence = variant.SequenceId is { } sequenceId
            ? clone.Sequences.FirstOrDefault(candidate => candidate.Id == sequenceId)
            : clone.MainSequence;
        if (sequence == null) throw new InvalidOperationException("Variant sequence is unavailable");
        sequence.Width = variant.Width;
        sequence.Height = variant.Height;
        if (variant.FrameRate.HasValue) sequence.FrameRate = variant.FrameRate.Value;

        foreach (var trackOverride in variant.TrackOverrides)
        {
            var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == trackOverride.TrackId);
            if (track == null) continue;
            if (trackOverride.Hidden.HasValue) track.Hidden = trackOverride.Hidden.Value;
            if (trackOverride.Muted.HasValue) track.Muted = trackOverride.Muted.Value;
            if (trackOverride.Solo.HasValue) track.Solo = trackOverride.Solo.Value;
        }
        foreach (var itemOverride in variant.ItemOverrides)
        {
            var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == itemOverride.ItemId));
            var item = track?.Items.FirstOrDefault(candidate => candidate.Id == itemOverride.ItemId);
            if (track == null || item == null) continue;
            if (itemOverride.Hidden)
            {
                track.Items.Remove(item);
                continue;
            }
            if (itemOverride.PositionX.HasValue) item.Transform.PositionX = itemOverride.PositionX.Value;
            if (itemOverride.PositionY.HasValue) item.Transform.PositionY = itemOverride.PositionY.Value;
            if (itemOverride.ScaleX.HasValue) item.Transform.ScaleX = Math.Max(0.001, itemOverride.ScaleX.Value);
            if (itemOverride.ScaleY.HasValue) item.Transform.ScaleY = Math.Max(0.001, itemOverride.ScaleY.Value);
            if (itemOverride.RotationDegrees.HasValue) item.Transform.RotationDegrees = itemOverride.RotationDegrees.Value;
            if (itemOverride.Opacity.HasValue) item.Opacity = Math.Clamp(itemOverride.Opacity.Value, 0, 1);
            if (itemOverride.Volume.HasValue) item.Volume = Math.Clamp(itemOverride.Volume.Value, 0, 4);
            if (itemOverride.Pan.HasValue) item.Pan = Math.Clamp(itemOverride.Pan.Value, -1, 1);
            if (itemOverride.FontSize.HasValue && item.Kind == ItemKind.Text) item.FontSize = Math.Clamp(itemOverride.FontSize.Value, 1, 1000);
            if (itemOverride.TextContent != null && item.Kind == ItemKind.Text) item.TextContent = itemOverride.TextContent;
        }
        if (variant.Overrides.TryGetValue("captionScale", out var captionScaleText)
            && double.TryParse(captionScaleText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var captionScale))
        {
            foreach (var textItem in sequence.Tracks.SelectMany(track => track.Items).Where(item => item.Kind == ItemKind.Text))
                textItem.FontSize = Math.Clamp(textItem.FontSize * captionScale, 1, 1000);
        }
        if (variant.Overrides.TryGetValue("captionYOffset", out var captionOffsetText)
            && double.TryParse(captionOffsetText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var captionOffset))
        {
            foreach (var textItem in sequence.Tracks.SelectMany(track => track.Items).Where(item => item.Kind == ItemKind.Text))
                textItem.Transform.PositionY += captionOffset;
        }
        if (variant.Overrides.TryGetValue("backgroundColor", out var backgroundColor)
            && !string.IsNullOrWhiteSpace(backgroundColor))
        {
            sequence.Background = new CanvasBackground
            {
                Kind = CanvasBackgroundKind.Solid,
                PrimaryColor = backgroundColor,
                SecondaryColor = backgroundColor,
            };
        }
        return (clone, sequence);
    }

    private async Task RenderSelectedVariantAsync()
    {
        if (OutputVariantList.SelectedItem is not OutputVariantListItem selected) return;
        var variant = selected.Variant;
        var sequence = variant.SequenceId is { } id
            ? _project.Sequences.FirstOrDefault(item => item.Id == id)
            : _project.MainSequence;
        if (sequence == null)
        {
            StatusText.Text = "Variant sequence is unavailable";
            return;
        }
        var (renderProject, renderSequence) = CreateVariantRenderContext(variant);
        var format = Enum.TryParse<TimelineExportFormat>(variant.Format, true, out var parsedFormat) ? parsedFormat : TimelineExportFormat.Mp4;
        var extension = format switch
        {
            TimelineExportFormat.WebM => ".webm",
            TimelineExportFormat.Mov => ".mov",
            TimelineExportFormat.Mkv => ".mkv",
            _ => ".mp4",
        };
        var dialog = new SaveFileDialog
        {
            Filter = format switch
            {
                TimelineExportFormat.WebM => "WebM Video (*.webm)|*.webm",
                TimelineExportFormat.Mov => "QuickTime Video (*.mov)|*.mov",
                TimelineExportFormat.Mkv => "Matroska Video (*.mkv)|*.mkv",
                _ => "MP4 Video (*.mp4)|*.mp4",
            },
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"{_project.Name}-{variant.Name}{extension}",
        };
        if (dialog.ShowDialog(this) != true) return;
        var quality = Enum.TryParse<TimelineExportQuality>(variant.Quality, true, out var parsedQuality) ? parsedQuality : TimelineExportQuality.High;
        var options = new TimelineExportOptions(format, quality);
        variant.Status = ExportVariantStatus.Rendering;
        RefreshVariantsAndReceipts();
        SetMediaOperationState(true, $"Rendering {variant.Name}…");
        try
        {
            await _mediaService.ExportTimelineAsync(
                renderProject,
                renderSequence,
                dialog.FileName,
                cancellationToken: CancellationToken.None,
                outputWidth: variant.Width,
                outputHeight: variant.Height,
                exportOptions: options);
            var receipt = await _renderReceiptService.CreateAsync(
                _project,
                renderSequence,
                dialog.FileName,
                variant.Width,
                variant.Height,
                options,
                "manual-variant-panel",
                variant.Id);
            using (var mutation = _saveCoordinator.BeginMutation()) _project.IncrementRevision();
            MarkProjectDirty("Variant render and QA receipt added");
            StatusText.Text = $"Variant verification: {receipt.Status}";
        }
        catch (Exception ex)
        {
            variant.Status = ExportVariantStatus.Failed;
            StatusText.Text = $"Variant render failed: {ex.Message}";
        }
        finally
        {
            SetMediaOperationState(false, "Variant render finished");
            RefreshVariantsAndReceipts();
        }
    }

    private void OpenSelectedRenderReceipt()
    {
        if (RenderReceiptList.SelectedItem is not RenderReceiptListItem selected) return;
        if (!File.Exists(selected.Receipt.ReceiptPath))
        {
            StatusText.Text = "Receipt file is missing";
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = selected.Receipt.ReceiptPath, UseShellExecute = true });
    }

    private void RefreshGeneratedCompositions()
    {
        var selectedId = (GeneratedCompositionList.SelectedItem as GeneratedCompositionListItem)?.Spec.Id;
        _generatedCompositionItems.Clear();
        foreach (var spec in _project.ExternalCompositions)
        {
            var item = new GeneratedCompositionListItem(spec);
            _generatedCompositionItems.Add(item);
            if (spec.Id == selectedId) GeneratedCompositionList.SelectedItem = item;
        }
        GeneratedCompositionList.SelectedItem ??= _generatedCompositionItems.FirstOrDefault();
        GeneratedCompositionEmptyText.Visibility = _generatedCompositionItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderCompositionButton.IsEnabled = _generatedCompositionItems.Count > 0;
        OpenCompositionOutputButton.IsEnabled = _generatedCompositionItems.Any(item => File.Exists(item.Spec.OutputPath));
    }

    private void RegisterLocalComposition(ExternalCompositionKind kind)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = $"Select local {kind} project",
            Multiselect = false,
        };
        if (folderDialog.ShowDialog(this) != true) return;
        var dialog = new ExternalCompositionDialog(this, kind, folderDialog.FolderName, _project.MainSequence);
        var spec = dialog.ShowCompositionDialog();
        if (spec == null) return;
        var validation = _externalCompositionService.Validate(spec, _currentProjectPath);
        if (!validation.Success)
        {
            MessageBox.Show(this, string.Join("\n", validation.Errors), "Composition Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        using var mutation = _saveCoordinator.BeginMutation();
        spec.Status = ExternalCompositionStatus.Validated;
        _project.ExternalCompositions.Add(spec);
        _project.IncrementRevision();
        MarkProjectDirty("Generated composition registered");
        RefreshGeneratedCompositions();
        OpenUtilityPanel(PanelId.GeneratedCompositions, GeneratedCompositionsTab);
        StatusText.Text = $"Registered local {kind} composition";
    }

    private async Task RenderSelectedCompositionAsync()
    {
        if (GeneratedCompositionList.SelectedItem is not GeneratedCompositionListItem selected) return;
        var spec = selected.Spec;
        SetMediaOperationState(true, $"Rendering {spec.Name}…");
        try
        {
            var result = await _externalCompositionService.RenderAsync(spec, _currentProjectPath);
            if (!result.Success || result.OutputPath == null)
            {
                StatusText.Text = $"Composition failed: {string.Join(" ", result.Errors)}";
                return;
            }
            var generatedAsset = spec.ImportAfterRender
                ? await CreateGeneratedCompositionAssetAsync(spec, result.OutputPath)
                : null;
            using (var mutation = _saveCoordinator.BeginMutation())
            {
                if (generatedAsset != null) _project.MediaLibrary.Add(generatedAsset);
                _project.IncrementRevision();
            }
            if (generatedAsset != null) RefreshMediaList();
            MarkProjectDirty("Generated composition rendered");
            StatusText.Text = $"Composition verification: {result.Verification?.Status}";
        }
        catch (Exception ex)
        {
            spec.Status = ExternalCompositionStatus.Failed;
            spec.LastError = ex.Message;
            StatusText.Text = $"Composition render failed: {ex.Message}";
        }
        finally
        {
            SetMediaOperationState(false, "Composition render finished");
            RefreshGeneratedCompositions();
        }
    }

    private async Task<MediaAsset?> CreateGeneratedCompositionAssetAsync(ExternalCompositionSpec spec, string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (_project.MediaLibrary.Any(asset => string.Equals(Path.GetFullPath(asset.OriginalPath), fullPath, StringComparison.OrdinalIgnoreCase)))
            return null;
        var probe = await _mediaService.ProbeAsync(fullPath);
        var video = probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
        return new MediaAsset
        {
            Kind = GetMediaKind(fullPath),
            OriginalPath = fullPath,
            RelativeProjectPath = fullPath,
            Duration = MediaTime.FromSeconds(probe.Duration.TotalSeconds),
            PixelWidth = video?.Width ?? spec.Width,
            PixelHeight = video?.Height ?? spec.Height,
        };
    }

    private void OpenSelectedCompositionOutput()
    {
        if (GeneratedCompositionList.SelectedItem is not GeneratedCompositionListItem selected) return;
        if (!File.Exists(selected.Spec.OutputPath))
        {
            StatusText.Text = "Composition output is missing";
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{selected.Spec.OutputPath}\"") { UseShellExecute = true });
    }

    private string ResolveMediaName(MediaAssetId assetId) =>
        Path.GetFileName(_project.MediaLibrary.FirstOrDefault(asset => asset.Id == assetId)?.OriginalPath) ?? assetId.ToString();

    private static string FormatBudget(decimal actual, decimal? limit) =>
        limit.HasValue ? $"${actual:0.00} / ${limit.Value:0.00}" : $"${actual:0.00} / local-only";

    private sealed record WorkflowStageListItem(int Number, ProductionWorkflowStage Stage)
    {
        public override string ToString()
        {
            var gate = Stage.RequiresApproval ? " • approval" : string.Empty;
            var warning = Stage.Warnings.Count > 0 ? $" • {Stage.Warnings.Count} warning(s)" : string.Empty;
            return $"{Number}. {Stage.Name} — {Stage.Status}{gate}{warning}";
        }
    }

    private sealed record TranscriptAssetListItem(string Name, MediaIntelligenceAnalysis Analysis)
    {
        public override string ToString() => $"{Name} • {Analysis.Transcript.Count} segments";
    }

    private sealed record TranscriptSegmentListItem(MediaIntelligenceTranscriptSegment Segment)
    {
        public override string ToString()
        {
            var speaker = string.IsNullOrWhiteSpace(Segment.Speaker) ? string.Empty : $"{Segment.Speaker}: ";
            var flags = $"{(Segment.ContainsFiller ? " • filler" : string.Empty)}{(Segment.RepeatedTake ? " • repeated" : string.Empty)}";
            return $"{FormatSeconds(Segment.Start.Seconds)}–{FormatSeconds(Segment.End.Seconds)}  {speaker}{Segment.Text}{flags}";
        }

        private static string FormatSeconds(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.ff");
    }

    private sealed record OutputVariantListItem(ExportVariant Variant)
    {
        public override string ToString() => $"{Variant.Name} • {Variant.Width}×{Variant.Height} • {Variant.Format}/{Variant.Quality} • {Variant.Status}";
    }

    private sealed record RenderReceiptListItem(RenderReceiptReference Receipt)
    {
        public override string ToString() => $"{Receipt.CreatedUtc.LocalDateTime:g} • {Path.GetFileName(Receipt.OutputPath)} • {Receipt.VerificationStatus}";
    }

    private sealed record GeneratedCompositionListItem(ExternalCompositionSpec Spec)
    {
        public override string ToString() => $"{Spec.Name} • {Spec.Kind} • {Spec.Width}×{Spec.Height} • {Spec.Status}";
    }
}
