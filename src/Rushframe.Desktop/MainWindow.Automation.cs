using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Rushframe.Desktop.Controllers;
using Rushframe.Desktop.Dialogs;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<WorkflowStageListItem> _workflowStageItems = [];
    private readonly ObservableCollection<CampaignTaskListItem> _campaignTaskItems = [];
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
        CampaignTaskList.ItemsSource = _campaignTaskItems;
        TranscriptAssetCombo.ItemsSource = _transcriptAssetItems;
        TranscriptSegmentList.ItemsSource = _transcriptSegmentItems;
        OutputVariantList.ItemsSource = _outputVariantItems;
        RenderReceiptList.ItemsSource = _renderReceiptItems;
        GeneratedCompositionList.ItemsSource = _generatedCompositionItems;

        WorkflowRefreshButton.Click += (_, _) => RefreshAutomationPanels();
        SaveCampaignDescriptionButton.Click += (_, _) => SaveCampaignDescription();
        EditEditingBriefButton.Click += (_, _) => EditEditingBrief();
        AddCampaignTaskButton.Click += (_, _) => AddCampaignTask();
        ToggleCampaignTaskButton.Click += (_, _) => ToggleSelectedCampaignTask();
        DeleteCampaignTaskButton.Click += (_, _) => DeleteSelectedCampaignTask();
        CampaignTaskInput.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter) return;
            AddCampaignTask();
            args.Handled = true;
        };
        CampaignTaskList.SelectionChanged += (_, _) => UpdateCampaignTaskButtons();
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
        CampaignDescriptionBox.Text = _project.CampaignDescription;
        var selectedTaskId = (CampaignTaskList.SelectedItem as CampaignTaskListItem)?.Task.Id;
        _campaignTaskItems.Clear();
        foreach (var task in _project.Tasks)
        {
            var taskItem = new CampaignTaskListItem(task);
            _campaignTaskItems.Add(taskItem);
            if (selectedTaskId.HasValue && task.Id == selectedTaskId.Value)
                CampaignTaskList.SelectedItem = taskItem;
        }
        CampaignTaskList.SelectedItem ??= _campaignTaskItems.FirstOrDefault();
        UpdateCampaignTaskButtons();
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
        WorkflowStatusText.Text = $"Active: {_project.Workflow.ActiveStageId}  •  Tasks: {_project.Tasks.Count(task => task.IsCompleted)}/{_project.Tasks.Count}  •  Decisions: {_project.Workflow.Decisions.Count}  •  "
                                  + $"Budget: {FormatBudget(_project.Workflow.ActualSpendUsd, _project.Workflow.BudgetLimitUsd)}";
    }

    private void SaveCampaignDescription()
    {
        if (Execute(new UpdateCampaignDescriptionCommand(_project, CampaignDescriptionBox.Text)))
        {
            RefreshWorkflowPanel();
            StatusText.Text = "Campaign description saved";
        }
    }

    private void EditEditingBrief()
    {
        var dialog = new EditingBriefDialog(this, _project.EditingBrief);
        if (dialog.ShowDialog() != true) return;
        if (Execute(new UpdateEditingBriefCommand(_project, dialog.Result)))
        {
            RefreshWorkflowPanel();
            StatusText.Text = "Structured editing brief saved";
        }
    }

    private void AddCampaignTask()
    {
        var title = CampaignTaskInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            StatusText.Text = "Enter a task title first";
            return;
        }
        if (Execute(new AddCampaignTaskCommand(_project, new CampaignTask { Title = title })))
        {
            CampaignTaskInput.Clear();
            RefreshWorkflowPanel();
            CampaignTaskList.SelectedItem = _campaignTaskItems.LastOrDefault();
            StatusText.Text = "Campaign task added";
        }
    }

    private void ToggleSelectedCampaignTask()
    {
        if (CampaignTaskList.SelectedItem is not CampaignTaskListItem selected) return;
        if (Execute(new UpdateCampaignTaskCommand(_project, selected.Task.Id, isCompleted: !selected.Task.IsCompleted)))
        {
            RefreshWorkflowPanel();
            StatusText.Text = selected.Task.IsCompleted ? "Campaign task completed" : "Campaign task reopened";
        }
    }

    private void DeleteSelectedCampaignTask()
    {
        if (CampaignTaskList.SelectedItem is not CampaignTaskListItem selected) return;
        if (Execute(new DeleteCampaignTaskCommand(_project, selected.Task.Id)))
        {
            RefreshWorkflowPanel();
            StatusText.Text = "Campaign task removed";
        }
    }

    private void UpdateCampaignTaskButtons()
    {
        var hasSelection = CampaignTaskList.SelectedItem is CampaignTaskListItem;
        ToggleCampaignTaskButton.IsEnabled = hasSelection;
        DeleteCampaignTaskButton.IsEnabled = hasSelection;
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

    private (Project Project, Sequence Sequence) CreateVariantRenderContext(ExportVariant variant) =>
        VariantRenderContextService.Create(_project, variant);

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
        var projectContext = CaptureProjectOperationContext();
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
        var operationCancellation = new CancellationTokenSource();
        _operationCancellation?.Dispose();
        _operationCancellation = operationCancellation;
        CancelOperationButton.Visibility = Visibility.Visible;
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;
        RefreshVariantsAndReceipts();
        SetMediaOperationState(true, $"Rendering {variant.Name}…");
        try
        {
            await _mediaService.ExportTimelineAsync(
                renderProject,
                renderSequence,
                dialog.FileName,
                cancellationToken: operationCancellation.Token,
                outputWidth: variant.Width,
                outputHeight: variant.Height,
                exportOptions: options);
            var receipt = await _renderReceiptService.CreateAsync(
                renderProject,
                renderSequence,
                dialog.FileName,
                variant.Width,
                variant.Height,
                options,
                "manual-variant-panel",
                variant.Id,
                operationCancellation.Token);
            if (!IsCurrentProjectOperation(projectContext))
            {
                StatusText.Text = "Variant render finished, but the originating project is no longer open; no project state was changed.";
                return;
            }
            using (var mutation = _saveCoordinator.BeginMutation())
            {
                RenderReceiptService.ApplyToProject(_project, receipt);
                _project.IncrementRevision();
            }
            MarkProjectDirty("Variant render and QA receipt added");
            StatusText.Text = $"Variant verification: {receipt.Status}";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                using (var mutation = _saveCoordinator.BeginMutation())
                {
                    variant.Status = ExportVariantStatus.Failed;
                    _project.IncrementRevision();
                }
                MarkProjectDirty("Variant render canceled");
            }
            StatusText.Text = "Variant render canceled";
        }
        catch (Exception ex)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                using (var mutation = _saveCoordinator.BeginMutation())
                {
                    variant.Status = ExportVariantStatus.Failed;
                    _project.IncrementRevision();
                }
                MarkProjectDirty("Variant render failed");
            }
            StatusText.Text = $"Variant render failed: {ex.Message}";
        }
        finally
        {
            CancelOperationButton.Visibility = Visibility.Collapsed;
            OperationProgressBar.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(_operationCancellation, operationCancellation)) _operationCancellation = null;
            operationCancellation.Dispose();
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
        var projectContext = CaptureProjectOperationContext();
        var operationCancellation = new CancellationTokenSource();
        _operationCancellation?.Dispose();
        _operationCancellation = operationCancellation;
        CancelOperationButton.Visibility = Visibility.Visible;
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;
        SetMediaOperationState(true, $"Rendering {spec.Name}…");
        try
        {
            var result = await _externalCompositionService.RenderAsync(spec, _currentProjectPath, operationCancellation.Token);
            if (!result.Success || result.OutputPath == null)
            {
                using (var mutation = _saveCoordinator.BeginMutation())
                {
                    spec.Status = ExternalCompositionStatus.Failed;
                    spec.LastError = string.Join(" ", result.Errors);
                    _project.IncrementRevision();
                }
                MarkProjectDirty("Generated composition failed");
                StatusText.Text = $"Composition failed: {spec.LastError}";
                return;
            }
            var generatedAsset = spec.ImportAfterRender
                ? await CreateGeneratedCompositionAssetAsync(spec, result.OutputPath, operationCancellation.Token)
                : null;
            if (!IsCurrentProjectOperation(projectContext))
            {
                StatusText.Text = "Composition render finished, but the originating project is no longer open; no project state was changed.";
                return;
            }
            using (var mutation = _saveCoordinator.BeginMutation())
            {
                spec.OutputPath = result.OutputPath;
                spec.LastOutputSha256 = result.OutputSha256;
                spec.LastRenderedUtc = DateTimeOffset.UtcNow;
                spec.Status = result.Verification?.Status == MediaExportVerificationStatus.Failed
                    ? ExternalCompositionStatus.Failed
                    : ExternalCompositionStatus.Rendered;
                spec.LastError = spec.Status == ExternalCompositionStatus.Failed
                    ? string.Join(" ", result.Verification?.Errors ?? result.Errors)
                    : null;
                if (generatedAsset != null) _project.MediaLibrary.Add(generatedAsset);
                _project.IncrementRevision();
            }
            if (generatedAsset != null) RefreshMediaList();
            MarkProjectDirty("Generated composition rendered");
            StatusText.Text = $"Composition verification: {result.Verification?.Status}";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                using (var mutation = _saveCoordinator.BeginMutation())
                {
                    spec.Status = ExternalCompositionStatus.Failed;
                    spec.LastError = "Composition render was canceled.";
                    _project.IncrementRevision();
                }
                MarkProjectDirty("Generated composition canceled");
            }
            StatusText.Text = "Composition render canceled";
        }
        catch (Exception ex)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                using (var mutation = _saveCoordinator.BeginMutation())
                {
                    spec.Status = ExternalCompositionStatus.Failed;
                    spec.LastError = ex.Message;
                    _project.IncrementRevision();
                }
                MarkProjectDirty("Generated composition failed");
            }
            StatusText.Text = $"Composition render failed: {ex.Message}";
        }
        finally
        {
            CancelOperationButton.Visibility = Visibility.Collapsed;
            OperationProgressBar.Visibility = Visibility.Collapsed;
            if (ReferenceEquals(_operationCancellation, operationCancellation)) _operationCancellation = null;
            operationCancellation.Dispose();
            SetMediaOperationState(false, "Composition render finished");
            RefreshGeneratedCompositions();
        }
    }

    private async Task<MediaAsset?> CreateGeneratedCompositionAssetAsync(
        ExternalCompositionSpec spec,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (_project.MediaLibrary.Any(asset => string.Equals(Path.GetFullPath(asset.OriginalPath), fullPath, StringComparison.OrdinalIgnoreCase)))
            return null;
        var probe = await _mediaService.ProbeAsync(fullPath, cancellationToken);
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

    private sealed record CampaignTaskListItem(CampaignTask Task)
    {
        public override string ToString() => $"{(Task.IsCompleted ? "✓" : "○")} {Task.Title}";
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
