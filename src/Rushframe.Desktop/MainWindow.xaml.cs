using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rushframe.Desktop.Commands;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Timeline;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Domain.Serialization;
using Rushframe.Desktop.Controllers;
using Rushframe.Desktop.Dialogs;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Workspace;
using Rushframe.Infrastructure;
using Rushframe.Application;
using Rushframe.Media.Native;
using Rushframe.Media.Abstractions;
using Microsoft.Win32;
using System.Globalization;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Rushframe.Desktop;

public partial class MainWindow : Window
{
    private const double MinimumUiScale = 0.75;
    private const double MaximumUiScale = 1.15;
    private const double UiScaleStep = 0.1;

    private readonly Stopwatch _startupClock = Stopwatch.StartNew();
    private readonly string? _startupDiagnosticPath = Environment.GetEnvironmentVariable("RUSHFRAME_STARTUP_LOG");
    private readonly WorkspaceLayoutService _workspaceService;
    private readonly SettingsService _settingsService;
    private readonly RecentProjectsService _recentProjectsService;
    private readonly IntelligenceBackendService _intelligenceBackend;
    private readonly SoundLibraryCatalogService _soundLibraryCatalogService;
    private readonly SoundLibraryWatchService _soundLibraryWatchService;
    private readonly LocalAgentBridgeService _localAgentBridge;
    private readonly string _appData;
    private WorkspaceLayout _layout;
    private EditorSettings _settings;
    private readonly VisualProviderCredentialRotationService _visualProviderCredentialRotation;

    private Project _project = new();
    private long _projectGeneration;
    private readonly UndoRedoStack _undoRedo = new();
    private readonly AutosaveService _autosave;
    private readonly ProjectRepository _projectRepo = new();
    private readonly EditorPerformanceTelemetry _performanceTelemetry = EditorPerformanceTelemetry.Shared;
    private readonly ProjectSaveCoordinator _saveCoordinator;
    private readonly ExactPreviewCache _exactPreviewCache;
    private readonly MigrationService _migrationService;
    private readonly FfmpegMediaService _mediaService = new();
    private readonly StabilizationAnalysisService _stabilizationService;
    private readonly MediaIntelligenceImportService _mediaIntelligenceImportService = new();
    private readonly MediaIntelligenceSearchService _mediaIntelligenceSearchService = new();
    private readonly MediaAgentContextBuilder _mediaAgentContextBuilder = new();
    private readonly EffectRegistry _effectRegistry = new();
    private readonly RippleState _rippleState = new();
    private readonly PreviewFrameScheduler _previewScheduler;
    private TimelineControl? _timeline;
    private TimelinePlayheadOverlay? _timelinePlayheadOverlay;
    private CopyClipCommand? _clipboard;
    private List<GroupClipboardItem>? _groupClipboard;
    private int _lastSelectedTrackIndex;
    private TimelineItem? _selectedInspectorItem;
    private TransitionSelection? _selectedTransitionSelection;
    private MediaKind? _mediaKindFilter;
    private string _mediaSearchText = string.Empty;
    private string? _currentProjectPath;
    private bool _isPreviewSeeking;
    private readonly PreviewSeekRequestGate _previewSeekRequestGate = new();
    private bool _isPreviewPlaying;
    private MediaAsset? _previewAsset;
    private TimelineItemId? _previewTimelineItemId;
    private TimelineItem? _previewTimelineItemCache;
    private double? _previewMarkInSeconds;
    private double? _previewMarkOutSeconds;
    private bool _previewFullscreen;
    private bool _previewWindowPortrait = true;
    private bool _isTimelineCompositePreview;
    private bool _timelinePreviewDirty = true;
    private string? _timelinePreviewPath;
    private double _timelinePreviewOffsetSeconds;
    private double _timelinePreviewChunkEndSeconds;
    private long _timelinePreviewRevision = -1;
    private bool _isExactPreviewChunkSwitching;
    private CancellationTokenSource? _timelinePreviewRenderCancellation;
    private readonly List<MediaAsset> _previewHistory = [];
    private bool _suppressInspectorChangeTracking;
    private bool _suppressTimelineSelectionSync;
    private bool _suppressTimelineZoomSliderChange;
    private bool _inspectorDirty;
    private bool _isMediaOperationRunning;
    private bool _projectDirty;
    private bool _allowClose;
    private bool _isClosingSaveInProgress;
    private CancellationTokenSource? _operationCancellation;
    private readonly Dictionary<string, TextBox> _effectParameterEditors = [];
    private readonly Dictionary<MediaAssetId, IReadOnlyList<float>> _waveformPeaks = [];
    private readonly Dictionary<MediaAssetId, MediaAsset> _mediaById = [];
    private readonly Dictionary<MediaAssetId, string> _mediaAssetNames = [];
    private readonly ObservableCollection<MediaListItem> _mediaItems = [];
    private readonly Dictionary<MediaAssetId, MediaListItem> _mediaItemsById = [];
    private readonly ThumbnailCache _thumbnailCache = new();
    private readonly DispatcherTimer _mediaSearchDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ICollectionView? _mediaItemsView;
    private CancellationTokenSource? _thumbnailLoadCancellation;
    private bool _fontsLoaded;
    private string[] _systemFontNames = [];
    private readonly List<InspectorFontChoice> _inspectorFontChoices = [];
    private bool _optionalCapabilitiesLoaded;
    private readonly AgentAuditLogService _agentAuditLogService;
    private readonly List<AgentAuditRecord> _agentAuditLog = [];
    private readonly ExportController _exportController;
    private readonly AgentEditCommandFactory _agentEditCommandFactory = new();
    private readonly AgentEditPlanCompiler _agentEditPlanCompiler;
    private readonly ExternalCompositionService _externalCompositionService;
    private readonly RenderReceiptService _renderReceiptService;
    private readonly EditorActionRegistry _actionRegistry = new();
    private readonly CreativeAssetPackService _creativeAssetPackService = new();
    private readonly ExtensionManifestService _extensionManifestService = new();
    private readonly List<CreativeAssetProviderManifest> _installedAssetProviders = [];
    private readonly List<ExtensionManifest> _installedExtensions = [];

    public MainWindow()
    {
        WriteStartupDiagnostic("constructor.begin");
        var qaAppDataOverride = Environment.GetEnvironmentVariable("RUSHFRAME_QA_APPDATA");
        _appData = !string.IsNullOrWhiteSpace(qaAppDataOverride)
            ? Path.GetFullPath(qaAppDataOverride)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rushframe");
        _exactPreviewCache = new ExactPreviewCache(Path.Combine(_appData, "preview-cache", "chunks"));
        _workspaceService = new WorkspaceLayoutService(_appData);
        _settingsService = new SettingsService(_appData);
        _recentProjectsService = new RecentProjectsService(_settingsService);
        _layout = _workspaceService.Load();
        _settings = _settingsService.Load("editor", new EditorSettings());
        _visualProviderCredentialRotation = new VisualProviderCredentialRotationService(
            () => _settings,
            settings => _settingsService.Save("editor", settings));
        _intelligenceBackend = new IntelligenceBackendService(Math.Clamp(_settings.IntelligenceBackendPort, 1024, 65535));
        _soundLibraryCatalogService = new SoundLibraryCatalogService(
            FindRepoRoot(),
            Path.Combine(_appData, "SoundLibrary", "catalog.sqlite"));
        _soundLibraryWatchService = new SoundLibraryWatchService(
            cancellationToken => _soundLibraryCatalogService.GetStatusAsync(cancellationToken),
            async (root, cancellationToken) =>
            {
                await IndexSoundFolderAsync(root, cancellationToken);
            });
        _soundLibraryWatchService.RootIndexed += (_, _) => Dispatcher.BeginInvoke(() =>
            _soundLibraryWindow?.RefreshAssets());
        _soundLibraryWatchService.IndexFailed += (_, message) => Dispatcher.BeginInvoke(() =>
            StatusText.Text = $"Sound-library watch failed: {message}");
        _localAgentBridge = new LocalAgentBridgeService(HandleLocalAgentBridgeRequestAsync);
        _agentEditPlanCompiler = new AgentEditPlanCompiler(_agentEditCommandFactory);
        _externalCompositionService = new ExternalCompositionService(_mediaService);
        _renderReceiptService = new RenderReceiptService(_mediaService);
        _agentAuditLogService = new AgentAuditLogService(Path.Combine(_appData, "audit", "agent-audit.jsonl"));
        _agentAuditLog.AddRange(_agentAuditLogService.ReadRecent(200));
        _autosave = new AutosaveService(Path.Combine(_appData, "autosave"));
        _saveCoordinator = new ProjectSaveCoordinator(_projectRepo, _autosave, _performanceTelemetry);
        _saveCoordinator.SaveCompleted += (_, args) => Dispatcher.BeginInvoke(() =>
        {
            if (args.IsAutosave) ShowAutosaveSaved(args.Path);
        });
        _saveCoordinator.SaveFailed += (_, error) => Dispatcher.BeginInvoke(() =>
            AutosaveStatusText.Text = $"Autosave failed: {error.Message}");
        _migrationService = new MigrationService(Path.Combine(_appData, "backups"));
        _stabilizationService = new StabilizationAnalysisService(Path.Combine(_appData, "analysis", "stabilization"));
        WriteStartupDiagnostic("services.created");

        WriteStartupDiagnostic("initialize_component.begin");
        InitializeComponent();
        InitializePanelDocking();
        InitializeInspectorUtilityTabs();
        WriteStartupDiagnostic("initialize_component.end");
        _performanceTelemetry.RecordStartupMilestone("shell_initialized", _startupClock.Elapsed);
        _previewScheduler = new PreviewFrameScheduler(
            OnPreviewFrameTick,
            UpdatePreviewTransportDisplay,
            GetPreviewTargetFramesPerSecond);
        MediaList.ItemsSource = _mediaItems;
        _mediaItemsView = CollectionViewSource.GetDefaultView(_mediaItems);
        _mediaItemsView.Filter = ShouldShowMediaItem;
        _mediaSearchDebounce.Tick += (_, _) =>
        {
            _mediaSearchDebounce.Stop();
            RefreshMediaView();
        };
        MediaList.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => QueueVisibleMediaThumbnails()));
        FontFamilyCombo.DropDownOpened += async (_, _) => await EnsureFontsLoadedAsync();
        _actionRegistry.ApplyInputBindings(this, _settings.Keybindings);
        InitializePreviewInteraction();
        InitializeGlobalFunctionSearch();
        InitializeAutomationPanels();
        _exportController = new ExportController(this, _mediaService);
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProc);
        };

        InputBindings.Add(new KeyBinding(EditorCommands.ZoomIn, new KeyGesture(Key.OemPlus, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(EditorCommands.ZoomIn, new KeyGesture(Key.Add, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(EditorCommands.ZoomOut, new KeyGesture(Key.OemMinus, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(EditorCommands.ZoomOut, new KeyGesture(Key.Subtract, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(EditorCommands.ResetZoom, new KeyGesture(Key.D0, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(EditorCommands.ResetZoom, new KeyGesture(Key.NumPad0, ModifierKeys.Control)));

        Loaded += async (_, _) =>
        {
            WriteStartupDiagnostic("window.loaded");
            _performanceTelemetry.RecordStartupMilestone("window_loaded", _startupClock.Elapsed);
            ScheduleQaAutoCloseIfRequested();
            ConstrainWindowToWorkingArea();
            UpdateResponsiveLayout();
            try
            {
                _localAgentBridge.Start();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Local agent bridge unavailable: {ex.Message}";
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await LoadOptionalCapabilitiesAsync();
            try
            {
                await _soundLibraryWatchService.RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Sound-library watch unavailable: {ex.Message}";
            }
            _performanceTelemetry.RecordStartupMilestone("optional_capabilities_loaded", _startupClock.Elapsed);
            _performanceTelemetry.RecordStartupMilestone("empty_project_ready", _startupClock.Elapsed);
            if (_settings.StartIntelligenceBackend)
                await StartIntelligenceBackendAsync();
            _performanceTelemetry.RecordStartupMilestone("startup_complete", _startupClock.Elapsed);
        };
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        StateChanged += (_, _) =>
        {
            UpdateWindowFrame();
            if (WindowState == WindowState.Normal)
                Dispatcher.BeginInvoke(ConstrainWindowToWorkingArea, DispatcherPriority.Loaded);
        };

        MinimizeWindowButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeWindowButton.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };
        CloseWindowButton.Click += (_, _) => Close();
        CancelOperationButton.Click += (_, _) => _operationCancellation?.Cancel();
        SettingsButton.Click += (_, _) => ShowSettingsDialog();
        PreviewOrientationButton.Click += (_, _) => TogglePreviewWindowOrientation();
        McpStatusButton.Click += (_, _) => ShowLocalAgentStatusDialog();
        OpenRecentMenu.SubmenuOpened += (_, _) => PopulateOpenRecentMenu();
        RecoverLatestAutosaveMenuItem.Click += async (_, _) => await RecoverLatestAutosaveAsync();
        ConfigureTrackHeaderMenu();

        ApplyEditorSettings();
        RippleToggle.Click += (_, _) => _rippleState.Enabled = RippleToggle.IsChecked ?? false;
        SnapToggle.Click += (_, _) =>
        {
            if (_timeline != null) _timeline.SnapEnabled = SnapToggle.IsChecked ?? true;
        };
        ZoomSlider.ValueChanged += (_, _) =>
        {
            if (_suppressTimelineZoomSliderChange) return;
            _timeline?.SetZoomScale(ZoomSlider.Value);
        };

        MediaList.MouseDoubleClick += (_, _) => PreviewSelectedMedia();
        MediaList.SelectionChanged += (_, _) =>
        {
            var hasSelection = MediaList.SelectedItem != null;
            AddToTimelineButton.IsEnabled = hasSelection;
            PreviewSelectedMediaButton.IsEnabled = hasSelection;
            UpdateMediaIntelligenceActionState();
            CommandManager.InvalidateRequerySuggested();
        };
        AddToTimelineButton.Click += async (_, _) => await AddSelectedMediaToTimelineAsync();
        CreativeAssetsButton.Click += async (_, _) => await ShowCreativeAssetsAsync();
        PreviewSelectedMediaButton.Click += (_, _) => PreviewSelectedMedia();
        RunMediaIntelligenceButton.Click += async (_, _) => await RunMediaIntelligenceAsync();
        ApplyMediaIntelligenceButton.Click += async (_, _) => await ApplyCurrentMediaIntelligenceToTimelineAsync();
        OpenMediaAnalysisButton.Click += (_, _) => OpenSelectedMediaAnalysisOutput();
        SearchMediaContextButton.Click += (_, _) => SearchMediaContext(findHooks: false);
        FindHooksButton.Click += (_, _) => SearchMediaContext(findHooks: true);
        MediaContextSearchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || !SearchMediaContextButton.IsEnabled) return;
            SearchMediaContext(findHooks: false);
            args.Handled = true;
        };
        MediaIntelligenceTab.SizeChanged += (_, _) => UpdateMediaIntelligenceResponsiveLayout();
        AnalyzeTranscriptToggle.Checked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        AnalyzeTranscriptToggle.Unchecked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        AnalyzeMusicToggle.Checked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        AnalyzeMusicToggle.Unchecked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        AnalyzeVisualsToggle.Checked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        AnalyzeVisualsToggle.Unchecked += (_, _) => UpdateMediaIntelligenceFeatureDependencies();
        UpdateMediaIntelligenceFeatureDependencies();
        UpdateMediaIntelligenceActionState();
        MediaSearchBox.TextChanged += (_, _) =>
        {
            _mediaSearchText = MediaSearchBox.Text.Trim();
            MediaSearchHint.Visibility = string.IsNullOrEmpty(MediaSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            _mediaSearchDebounce.Stop();
            _mediaSearchDebounce.Start();
        };
        MediaSearchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            MediaSearchBox.Clear();
            args.Handled = true;
        };
        AllMediaFilterButton.Click += (_, _) => SetMediaFilter(null);
        VideoFilterButton.Click += (_, _) => SetMediaFilter(MediaKind.Video);
        ImageFilterButton.Click += (_, _) => SetMediaFilter(MediaKind.Image);
        AudioFilterButton.Click += (_, _) => SetMediaFilter(MediaKind.Audio);

        PreviewPlayButton.Click += async (_, _) => await PlayPreviewAsync();
        PreviewPauseButton.Click += (_, _) => PausePreview();
        PreviewStopButton.Click += (_, _) => StopPreview();
        PreviewPlayer.MediaOpened += (_, _) => OnPreviewMediaOpened();
        PreviewPlayer.MediaEnded += (_, _) =>
        {
            if (_isTimelineCompositePreview && _project.MainSequence is { } sequence)
            {
                if (_timelinePreviewChunkEndSeconds < sequence.Duration.Seconds - 0.001)
                {
                    _ = SwitchExactPreviewChunkAsync(_timelinePreviewChunkEndSeconds, resumePlayback: true);
                    return;
                }
                if (PreviewLoopToggle.IsChecked == true)
                {
                    _ = SwitchExactPreviewChunkAsync(0, resumePlayback: true);
                    return;
                }
            }

            if (PreviewLoopToggle.IsChecked == true)
            {
                PreviewPlayer.Position = TimeSpan.FromSeconds(_previewMarkInSeconds ?? 0);
                PreviewPlayer.Play();
                _isPreviewPlaying = true;
            }
            else
            {
                StopPreview();
            }
        };
        PreviewPlayer.MediaFailed += (_, args) =>
        {
            StopPreview();
            SetPreviewControlsEnabled(false);
            PreviewSourceNameText.Text = $"Preview failed: {args.ErrorException?.Message ?? "unknown error"}";
        };
        PreviewSeekSlider.PreviewMouseLeftButtonDown += (_, _) =>
        {
            _isPreviewSeeking = true;
            _previewSeekRequestGate.BeginPointerSeek();
        };
        PreviewSeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _isPreviewSeeking = false;
            _previewSeekRequestGate.CompletePointerSeek();
            SeekPreview(PreviewSeekSlider.Value);
        };
        PreviewSeekSlider.ValueChanged += (_, args) =>
        {
            if (!_previewSeekRequestGate.ShouldSeekFromSliderValueChanged()) return;
            SeekPreview(args.NewValue);
        };
        PreviewPreviousFrameButton.Click += (_, _) => StepPreviewFrame(-1);
        PreviewNextFrameButton.Click += (_, _) => StepPreviewFrame(1);
        PreviewMarkInButton.Click += (_, _) => SetPreviewMark(true);
        PreviewMarkOutButton.Click += (_, _) => SetPreviewMark(false);
        PreviewClearMarksButton.Click += (_, _) => ClearPreviewMarks();
        PreviewInsertButton.Click += (_, _) => AddPreviewRangeToTimeline(overwrite: false);
        PreviewOverwriteButton.Click += (_, _) => AddPreviewRangeToTimeline(overwrite: true);
        PreviewMuteToggle.Checked += (_, _) =>
        {
            PreviewPlayer.IsMuted = true;
            ApplyRealtimeAudioSettings();
        };
        PreviewMuteToggle.Unchecked += (_, _) =>
        {
            PreviewPlayer.IsMuted = false;
            ApplyRealtimeAudioSettings();
        };
        PreviewVolumeSlider.ValueChanged += (_, _) =>
        {
            PreviewPlayer.Volume = PreviewVolumeSlider.Value;
            ApplyRealtimeAudioSettings();
        };
        PreviewSpeedButton.Click += (_, _) => CyclePreviewSpeed();
        PreviewZoomButton.Click += (_, _) => CyclePreviewZoom();
        PreviewGuidesToggle.Checked += (_, _) =>
        {
            PreviewGuidesOverlay.Visibility = Visibility.Visible;
            RefreshPreviewGuidesOverlay();
        };
        PreviewGuidesToggle.Unchecked += (_, _) => PreviewGuidesOverlay.Visibility = Visibility.Collapsed;
        PreviewCanvasSettingsButton.Click += (_, _) => OpenCanvasSettings();
        PreviewSnapshotButton.Click += async (_, _) => await SavePreviewSnapshotAsync();
        PreviewFullscreenButton.Click += (_, _) => TogglePreviewFullscreen();
        PreviewTimeBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            if (TryParsePreviewTime(PreviewTimeBox.Text, out var seconds))
            {
                SeekPreview(seconds);
                StatusText.Text = $"Preview moved to {FormatPreviewTime(TimeSpan.FromSeconds(seconds))}";
            }
            else
            {
                StatusText.Text = "Invalid time. Use seconds or HH:MM:SS.mmm";
                PreviewTimeBox.SelectAll();
            }
            args.Handled = true;
        };
        PreviewKeyDown += OnPreviewKeyboardShortcut;
        SetPreviewControlsEnabled(false);

        ApplyInspectorButton.Click += (_, _) => ApplyInspectorSettings();
        ResetInspectorButton.Click += (_, _) => ResetInspectorEdits();
        OpenAnimationEditorButton.Click += (_, _) => OpenAnimationEditor();
        AddEffectButton.Click += (_, _) => AddSelectedEffect();
        EffectCombo.SelectionChanged += (_, _) =>
            AddEffectButton.IsEnabled = CanEditSelectedEffects()
                && EffectCombo.SelectedItem != null;
        EffectList.SelectionChanged += (_, _) => UpdateSelectedEffectEditor();
        EffectRemoveButton.Click += (_, _) => RemoveSelectedEffect();
        EffectMoveUpButton.Click += (_, _) => MoveSelectedEffect(-1);
        EffectMoveDownButton.Click += (_, _) => MoveSelectedEffect(1);
        EffectToggleButton.Click += (_, _) => ToggleSelectedEffect();
        EffectDuplicateButton.Click += (_, _) => DuplicateSelectedEffect();
        EffectResetButton.Click += (_, _) => ResetSelectedEffect();
        EffectApplyParametersButton.Click += (_, _) => ApplySelectedEffectParameters();
        AnalyzeStabilizationButton.Click += async (_, _) => await AnalyzeSelectedStabilizationAsync();
        RegisterInspectorChangeTracking();
        BindColorSlider(BrightnessSlider, BrightnessBox);
        BindColorSlider(ContrastSlider, ContrastBox);
        BindColorSlider(SaturationSlider, SaturationBox);
        UpdateMediaFilterButtons();

        WriteStartupDiagnostic("editor_layout.begin");
        BuildPanelsMenu();
        ApplyLayout();
        InitTimeline();
        ApplyEditorSettings();
        RestartAutosave();
        WriteStartupDiagnostic("editor_layout.end");

        CommandBindings.Add(new CommandBinding(EditorCommands.NewProject, NewProject_Executed, ProjectReplacement_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.OpenProject, OpenProject_Executed, ProjectReplacement_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.SaveProject, SaveProject_Executed));
        CommandBindings.Add(new CommandBinding(EditorCommands.ImportMedia, ImportMedia_Executed, MediaOperation_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.RelinkMedia, RelinkMedia_Executed, SelectedMedia_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.GenerateMediaCache, GenerateMediaCache_Executed, SelectedMedia_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.ExtractAudio, ExtractAudio_Executed, ExtractAudio_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.ImportMediaIntelligence, ImportMediaIntelligence_Executed, MediaIntelligence_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Render, Render_Executed, Render_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.AddText, AddText_Executed, Sequence_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.AddMarker, AddMarker_Executed, Sequence_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Undo, Undo_Executed, Undo_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Redo, Redo_Executed, Redo_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Cut, Cut_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Copy, Copy_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Paste, Paste_Executed, Paste_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.SplitClip, SplitClip_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.DeleteClip, DeleteClip_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Duplicate, Duplicate_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.RippleDelete, RippleDelete_Executed, SelectedClip_CanExecute));
        CommandBindings.Add(new CommandBinding(EditorCommands.Settings, Settings_Executed));
        CommandBindings.Add(new CommandBinding(EditorCommands.ZoomIn, (_, _) => ChangeUiScale(UiScaleStep)));
        CommandBindings.Add(new CommandBinding(EditorCommands.ZoomOut, (_, _) => ChangeUiScale(-UiScaleStep)));
        CommandBindings.Add(new CommandBinding(EditorCommands.ResetZoom, (_, _) => SetUiScale(1, persist: true)));

        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            _previewScheduler.Dispose();
            _mediaSearchDebounce.Stop();
            _thumbnailLoadCancellation?.Cancel();
            _thumbnailLoadCancellation?.Dispose();
            _thumbnailCache.Dispose();
            PreviewPlayer.Stop();
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _timelinePreviewRenderCancellation?.Cancel();
            _timelinePreviewRenderCancellation?.Dispose();
            _timelinePlayheadOverlay?.Dispose();
            _saveCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _autosave.StopBackgroundAsync().GetAwaiter().GetResult();
            _soundLibraryWatchService.Dispose();
            _intelligenceBackend.Dispose();
            _localAgentBridge.Dispose();
            if (_performanceTelemetry.DetailedEnabled)
            {
                _performanceTelemetry.WriteSnapshot(Path.Combine(
                    _appData,
                    "performance",
                    $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
            }
            SaveLayout();
        };
        WriteStartupDiagnostic("constructor.end");
    }

    private void WriteStartupDiagnostic(string milestone)
    {
        if (string.IsNullOrWhiteSpace(_startupDiagnosticPath)) return;
        try
        {
            var path = Path.GetFullPath(_startupDiagnosticPath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(
                path,
                $"{DateTime.UtcNow:O}|{_startupClock.Elapsed.TotalMilliseconds:0.###}|{milestone}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must never affect normal application startup.
        }
    }

    private void ScheduleQaAutoCloseIfRequested()
    {
        if (!int.TryParse(
                Environment.GetEnvironmentVariable("RUSHFRAME_QA_AUTOCLOSE_MS"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var milliseconds)
            || milliseconds is < 1000 or > 300_000)
            return;

        WriteStartupDiagnostic($"qa_autoclose.scheduled|milliseconds={milliseconds}");
        _ = CloseAfterDelayForQaAsync(milliseconds);
    }

    private async Task CloseAfterDelayForQaAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.InvokeAsync(() =>
        {
            WriteStartupDiagnostic("qa_autoclose.executing");
            _allowClose = true;
            Close();
        }, DispatcherPriority.Send);
    }

    private void InitTimeline()
    {
        _timeline = new TimelineControl
        {
            Sequence = _project.MainSequence,
            ProjectRevision = _project.Revision,
        };
        RebuildMediaIndexes();
        _timeline.AssetNameResolver = assetId =>
            _mediaAssetNames.TryGetValue(assetId, out var name) ? name : "Media";
        _timeline.AssetWaveformResolver = ResolveWaveformPeaks;
        _timeline.ClipContextMenu = (ContextMenu)FindResource("TimelineClipContextMenu");
        _timeline.TrackHeaderContextMenu = (ContextMenu)FindResource("TrackHeaderContextMenu");
        _timeline.SnapEnabled = SnapToggle.IsChecked ?? true;

        _timeline.ClipSelected += (_, item) =>
        {
            if (_suppressTimelineSelectionSync) return;
            if (!TryResolvePendingInspectorChanges())
            {
                RestoreInspectorTimelineSelection();
                return;
            }

            _contextTrackIndex = item != null ? _timeline.SelectedTrackIndex : -1;
            if (item != null && _timeline.SelectedTrackIndex >= 0)
                _lastSelectedTrackIndex = _timeline.SelectedTrackIndex;
            if (item == null) _contextTrackIndex = -1;
            _selectedInspectorItem = item;
            if (item != null) _selectedTransitionSelection = null;
            UpdateInspector(item);
            UpdatePreviewInteractionOverlay(item);
            if (item != null) _ = PreviewTimelineItemAsync(item);
            CommandManager.InvalidateRequerySuggested();
        };
        _timeline.TransitionSelected += (_, selection) => SelectTransition(selection);
        _timeline.PlayheadMoved += (_, _) => SeekPreviewToTimelinePlayhead();
        _timeline.PlayPauseRequested += async (_, _) => await TogglePreviewPlaybackAsync();
        _timeline.MarkerEditRequested += (_, marker) => EditMarker(marker);
        _timeline.TrackHeaderContextRequested += (_, trackIndex) => PrepareTrackHeaderContextMenu(trackIndex);
        _timeline.DeleteSelectedClipRequested += (_, _) => DeleteSelectedClip();
        _timeline.ClipMoveRequested += (_, args) => MoveClip(args);
        _timeline.ClipTrimRequested += (_, args) => TrimClip(args);
        _timeline.ClipVolumeRequested += (_, args) => SetClipVolume(args);
        _timeline.GroupMoveRequested += (_, args) => MoveClipGroup(args);
        _timeline.GroupTrimRequested += (_, args) => TrimClipGroup(args);
        _timeline.MediaDropRequested += HandleTimelineMediaDrop;
        _timeline.MediaDragPreviewRequested += HandleTimelineMediaDragPreview;
        _timeline.MediaDragPreviewCleared += (_, _) => StatusText.Text = "Ready";
        _timeline.ZoomScaleChanged += (_, scale) =>
        {
            var sliderValue = Math.Clamp(scale, ZoomSlider.Minimum, ZoomSlider.Maximum);
            if (Math.Abs(ZoomSlider.Value - sliderValue) < 0.001) return;

            try
            {
                _suppressTimelineZoomSliderChange = true;
                ZoomSlider.Value = sliderValue;
            }
            finally
            {
                _suppressTimelineZoomSliderChange = false;
            }
        };

        var timelineLayers = new Grid();
        timelineLayers.Children.Add(_timeline);
        _timelinePlayheadOverlay = new TimelinePlayheadOverlay(_timeline);
        timelineLayers.Children.Add(_timelinePlayheadOverlay);
        TimelineHost.Content = timelineLayers;
        RefreshMediaList();
        EffectCombo.ItemsSource = _effectRegistry.GetAll();
        TransitionKindCombo.ItemsSource = Enum.GetValues<TransitionKind>();
        TransitionAudioModeCombo.ItemsSource = Enum.GetValues<TransitionAudioMode>();
        VisualTransitionInCombo.ItemsSource = Enum.GetValues<ItemTransitionKind>();
        VisualTransitionOutCombo.ItemsSource = Enum.GetValues<ItemTransitionKind>();
        UpdateInspector(null);
    }

    private void ConfigureTrackHeaderMenu()
    {
        if (FindResource("TrackHeaderContextMenu") is not ContextMenu menu) return;
        var rename = FindTrackMenuItem(menu, "Rename Track");
        var duplicate = FindTrackMenuItem(menu, "Duplicate Track");
        var mute = FindTrackMenuItem(menu, "Mute Track");
        var solo = FindTrackMenuItem(menu, "Solo Track");
        var @lock = FindTrackMenuItem(menu, "Lock Track");
        var delete = FindTrackMenuItem(menu, "Delete Track");
        if (rename == null || duplicate == null || mute == null || solo == null || @lock == null || delete == null)
            return;

        rename.Click += (_, _) => RenameContextTrack();
        duplicate.Click += (_, _) => ExecuteForContextTrack(track => new DuplicateTrackCommand { TrackId = track.Id });
        mute.Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackMuteCommand { TrackId = track.Id });
        solo.Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackSoloCommand { TrackId = track.Id });
        @lock.Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackLockCommand { TrackId = track.Id });
        delete.Click += (_, _) => DeleteContextTrack();
    }

    private void PrepareTrackHeaderContextMenu(int trackIndex)
    {
        _contextTrackIndex = trackIndex;
        var track = GetContextTrack();
        if (FindResource("TrackHeaderContextMenu") is not ContextMenu menu) return;
        if (track == null)
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = false;
            return;
        }

        var rename = FindTrackMenuItem(menu, "Rename Track");
        var duplicate = FindTrackMenuItem(menu, "Duplicate Track");
        var mute = FindTrackMenuItem(menu, "Mute Track");
        var solo = FindTrackMenuItem(menu, "Solo Track");
        var @lock = FindTrackMenuItem(menu, "Lock Track");
        var delete = FindTrackMenuItem(menu, "Delete Track");
        if (rename == null || duplicate == null || mute == null || solo == null || @lock == null || delete == null)
            return;

        rename.IsEnabled = true;
        duplicate.IsEnabled = true;
        mute.IsEnabled = true;
        solo.IsEnabled = true;
        @lock.IsEnabled = true;
        delete.IsEnabled = _project.MainSequence?.Tracks.Count > 1;
        mute.IsChecked = track.Muted;
        solo.IsChecked = track.Solo;
        @lock.IsChecked = track.Locked;
    }

    private static MenuItem? FindTrackMenuItem(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().FirstOrDefault(item =>
            string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

    private Track? GetContextTrack()
    {
        var tracks = _project.MainSequence?.Tracks;
        return tracks != null && _contextTrackIndex >= 0 && _contextTrackIndex < tracks.Count
            ? tracks[_contextTrackIndex]
            : null;
    }

    private void ExecuteForContextTrack(Func<Track, IEditCommand> createCommand)
    {
        var track = GetContextTrack();
        if (track == null) return;
        Execute(createCommand(track));
        _timeline?.InvalidateVisual();
    }

    private void RenameContextTrack()
    {
        var track = GetContextTrack();
        if (track == null) return;
        var name = PromptForTrackName(track.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), track.Name, StringComparison.Ordinal)) return;
        Execute(new RenameTrackCommand { TrackId = track.Id, NewName = name.Trim() });
    }

    private string? PromptForTrackName(string currentName)
    {
        var dialog = CreateOwnedDialog(
            "Rename Track",
            width: 390,
            height: 180,
            minimumWidth: 340,
            minimumHeight: 170,
            resizeMode: ResizeMode.NoResize);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Track name",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(label);

        var input = new TextBox
        {
            Text = currentName,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(input, 1);
        root.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button
        {
            Content = "Rename",
            MinWidth = 86,
            IsDefault = true,
            Style = (Style)FindResource("PrimaryButtonStyle"),
        };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                input.Focus();
                return;
            }
            dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        dialog.Content = CreateDialogFrame(dialog, "Rename Track", root, new Thickness(14));
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void DeleteContextTrack()
    {
        var sequence = _project.MainSequence;
        var track = GetContextTrack();
        if (sequence == null || track == null) return;
        if (sequence.Tracks.Count <= 1)
        {
            StatusText.Text = "The last timeline track cannot be deleted.";
            return;
        }

        if (track.Items.Count > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"Delete '{track.Name}' and its {track.Items.Count} clip(s)?",
                "Delete Track",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        Execute(new DeleteTrackCommand { TrackId = track.Id });
        _contextTrackIndex = -1;
    }

    private int _contextTrackIndex = -1;

    private void BuildPanelsMenu()
    {
        foreach (var panel in PanelRegistry.All)
        {
            if (panel.Id == PanelId.RenderQueue)
                PanelsMenu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = panel.Title,
                IsCheckable = true,
                IsChecked = panel.CanClose ? _layout.IsPanelOpen(panel.Id) : true,
                IsEnabled = panel.CanClose,
                ToolTip = panel.CanClose ? null : $"{panel.Title} is always available.",
                Tag = panel.Id,
            };
            if (panel.CanClose) item.Click += PanelMenuItem_Click;
            PanelsMenu.Items.Add(item);
        }

        PanelsMenu.Items.Add(new Separator());
        var soundLibraryItem = new MenuItem
        {
            Header = "Sound Library",
            ToolTip = "Open the local sound library and drag audio onto the timeline",
        };
        soundLibraryItem.Click += (_, _) => ShowSoundLibrary();
        PanelsMenu.Items.Add(soundLibraryItem);
    }

    private void PanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not PanelId panelId) return;
        TogglePanel(panelId);
    }

    private void TogglePanel_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not string panelKey) return;
        TogglePanel(new PanelId(panelKey));
    }

    private void TogglePanel(PanelId panelId)
    {
        if (PanelRegistry.Find(panelId)?.CanClose == false) return;
        var current = _layout.IsPanelOpen(panelId);
        var opening = !current;
        if (!opening && !CanClosePrimaryPanelWithoutEmptyCells(panelId))
        {
            var utilitiesOpen = GetUtilityPanelEntries().Any(entry => _layout.IsPanelOpen(entry.Id));
            StatusText.Text = panelId == PanelId.Inspector && utilitiesOpen
                ? "Inspector must remain open because utility tabs cannot fit in a separate window at this size."
                : "That window cannot close because it would leave an unusable empty grid area.";
            SynchronizePanelMenuChecks();
            return;
        }
        _layout = _layout.WithPanelToggled(panelId, opening);
        foreach (var item in PanelsMenu.Items.OfType<MenuItem>())
            if (item.Tag is PanelId id && id == panelId) item.IsChecked = opening;
        ApplyLayout();
        if (opening && IsUtilityPanel(panelId) && GetUtilityInspectorTab(panelId) is { } tab)
            tab.IsSelected = true;
        UpdateMediaFilterButtons();
        SaveLayout();
    }

    private void EnsureMediaPanelOpen()
    {
        if (_layout.IsPanelOpen(PanelId.Media)) return;
        TogglePanel(PanelId.Media);
    }

    private void ApplyLayout()
    {
        var mediaOpen = _layout.IsPanelOpen(PanelId.Media);
        var previewOpen = _layout.IsPanelOpen(PanelId.Preview);
        var inspectorOpen = _layout.IsPanelOpen(PanelId.Inspector);
        var renderQueueOpen = _layout.IsPanelOpen(PanelId.RenderQueue);
        var intelligenceOpen = _layout.IsPanelOpen(PanelId.MediaIntelligence);
        var workflowOpen = _layout.IsPanelOpen(PanelId.ProductionWorkflow);
        var transcriptOpen = _layout.IsPanelOpen(PanelId.TranscriptEditor);
        var variantsOpen = _layout.IsPanelOpen(PanelId.OutputVariants);
        var compositionsOpen = _layout.IsPanelOpen(PanelId.GeneratedCompositions);
        var activityOpen = renderQueueOpen || intelligenceOpen || workflowOpen || transcriptOpen || variantsOpen || compositionsOpen;

        RenderQueueTab.Visibility = Vis(renderQueueOpen);
        MediaIntelligenceTab.Visibility = Vis(intelligenceOpen);
        ProductionWorkflowTab.Visibility = Vis(workflowOpen);
        TranscriptEditorTab.Visibility = Vis(transcriptOpen);
        OutputVariantsTab.Visibility = Vis(variantsOpen);
        GeneratedCompositionsTab.Visibility = Vis(compositionsOpen);
        UpdateUtilityPanelHosting(
            _previewWindowPortrait,
            mediaOpen,
            previewOpen,
            inspectorOpen,
            activityOpen);
        if (!inspectorOpen && activityOpen && !_utilityWindowSeparate)
        {
            inspectorOpen = true;
            _layout = _layout.WithPanelToggled(PanelId.Inspector, true);
            SynchronizePanelMenuChecks();
            SaveLayout();
            StatusText.Text = "Inspector restored to host utility tabs at this window size.";
        }
        var inspectorWindowOpen = inspectorOpen || (activityOpen && !_utilityWindowSeparate);
        var visiblePrimaryPanels = GetVisiblePrimaryPanels(mediaOpen, previewOpen, inspectorWindowOpen);
        ResolveEffectivePrimaryAreas(_previewWindowPortrait, visiblePrimaryPanels);
        ApplyAdaptiveGridPlacements(_previewWindowPortrait);
        if (_utilityWindowSeparate)
            PlaceUtilityWindowInGrid(_utilityWindowArea);

        MediaBorder.Visibility = Vis(mediaOpen);
        PreviewBorder.Visibility = Vis(previewOpen);
        TimelineWindow.Visibility = Visibility.Visible;
        RightPanelHost.Visibility = Vis(inspectorWindowOpen);
        AssetPanelColumn.Width = new GridLength(1, GridUnitType.Star);
        AssetPanelContent.Visibility = Vis(mediaOpen);

        InspectorBorder.Visibility = Vis(inspectorWindowOpen);
        RightInspectorRow.Height = inspectorWindowOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightTasksRow.Height = new GridLength(0);
        var visibleUtilityTabs = new[]
        {
            (Tab: RenderQueueTab, Open: renderQueueOpen),
            (Tab: MediaIntelligenceTab, Open: intelligenceOpen),
            (Tab: ProductionWorkflowTab, Open: workflowOpen),
            (Tab: TranscriptEditorTab, Open: transcriptOpen),
            (Tab: OutputVariantsTab, Open: variantsOpen),
            (Tab: GeneratedCompositionsTab, Open: compositionsOpen),
        };
        if (_utilityWindowSeparate)
        {
            SelectFirstVisibleUtilityTab();
        }
        else
        {
            if (InspectorTabs.SelectedItem is TabItem selectedTab && selectedTab.Visibility != Visibility.Visible)
                SelectFirstVisibleInspectorTab();
            if (!inspectorOpen
                && activityOpen
                && !visibleUtilityTabs.Any(entry => entry.Open && entry.Tab.IsSelected))
                visibleUtilityTabs.FirstOrDefault(entry => entry.Open).Tab?.SetCurrentValue(TabItem.IsSelectedProperty, true);
        }

        var previewArea = GetEffectivePrimaryArea(PanelId.Preview, _previewWindowPortrait);
        PreviewBorder.BorderThickness = _previewWindowPortrait
            ? new Thickness(1, 0, 1, 0)
            : new Thickness(0, 0, 0, 1);
        PreviewSourceNameText.MaxWidth = previewArea.ColumnSpan > 1 ? 260 : 120;
        ConfigureAdaptiveGridSplitters(
            _previewWindowPortrait,
            mediaOpen,
            previewOpen,
            inspectorWindowOpen);

        if (_previewFullscreen)
        {
            Panel.SetZIndex(PreviewBorder, 1000);
            Grid.SetColumn(PreviewBorder, 0);
            Grid.SetColumnSpan(PreviewBorder, 5);
            Grid.SetRow(PreviewBorder, 0);
            Grid.SetRowSpan(PreviewBorder, 3);
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (!IsInitialized) return;

        ApplyWindowSizeGuardrails();
        var effectiveWidth = ActualWidth > 0 ? ActualWidth / Math.Max(_settings.UiScale, 0.01) : 0;
        var compact = effectiveWidth > 0 && effectiveWidth < 1380;
        var veryCompact = effectiveWidth > 0 && effectiveWidth < 1120;

        GlobalFunctionSearchBox.Width = veryCompact ? 230 : compact ? 300 : 360;
        GlobalFunctionSearchButton.Visibility = Visibility.Visible;
        HeaderMenuScrollViewer.MaxWidth = Math.Max(110, ActualWidth - 430);
        HeaderBrandIcon.Visibility = Visibility.Visible;
        HeaderBrandText.Visibility = Visibility.Collapsed;
        HeaderExportButton.Visibility = Visibility.Visible;
        LocalEditingStatusText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TimelineZoomControls.Visibility = Visibility.Visible;
        UpdatePreviewOrientationButton();
        UpdateMediaIntelligenceResponsiveLayout();

        ApplyLayout();
    }

    private void HeaderMenuScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollHorizontallyOnMouseWheel(sender, e);

    private void PreviewTransportScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollHorizontallyOnMouseWheel(sender, e);

    private static void ScrollHorizontallyOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableWidth <= 0) return;
        scroller.ScrollToHorizontalOffset(Math.Clamp(scroller.HorizontalOffset - e.Delta, 0, scroller.ScrollableWidth));
        e.Handled = true;
    }

    private void Sequence_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _project.MainSequence != null;
        e.Handled = true;
    }

    private void SelectedClip_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        var selected = _timeline?.SelectedItem;
        var track = selected == null
            ? null
            : _project.MainSequence?.Tracks.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == selected.Id));

        e.CanExecute = selected != null && !selected.Locked && track?.Locked != true;
        e.Handled = true;
    }

    private void SelectedMedia_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_isMediaOperationRunning && MediaList.SelectedItem is MediaListItem;
        e.Handled = true;
    }

    private void MediaOperation_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_isMediaOperationRunning;
        e.Handled = true;
    }

    private void MediaIntelligence_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_isMediaOperationRunning
            && (MediaList.SelectedItem is MediaListItem || _selectedInspectorItem?.MediaAssetId != null);
        e.Handled = true;
    }

    private void ExtractAudio_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        var item = _selectedInspectorItem;
        var asset = item?.MediaAssetId is { } assetId
            ? _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId)
            : null;
        e.CanExecute = !_isMediaOperationRunning
            && item != null
            && ResolveInspectorProfile(item).CanExtractAudio
            && asset is { Kind: MediaKind.Video, IsOffline: false }
            && File.Exists(asset.OriginalPath);
        e.Handled = true;
    }

    private void Render_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_isMediaOperationRunning
            && _project.MainSequence?.Tracks.Any(track => track.Items.Count > 0) == true;
        e.Handled = true;
    }

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _undoRedo.CanUndo;
        e.Handled = true;
    }

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _undoRedo.CanRedo;
        e.Handled = true;
    }

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = (_clipboard?.Clipboard != null || _groupClipboard is { Count: > 0 })
            && _project.MainSequence?.Tracks.Any(track => !track.Locked) == true;
        e.Handled = true;
    }

    private void RegisterInspectorChangeTracking()
    {
        var numericTextBoxes = new[]
        {
            PositionXBox, PositionYBox, ScaleXBox, ScaleYBox, RotationBox, OpacityBox, SpeedBox,
            BrightnessBox, ContrastBox, SaturationBox, VolumeBox, PanBox, FadeInBox, FadeOutBox,
            FontSizeBox, TextOutlineWidthBox, TextShadowOpacityBox,
            TransitionDurationBox, TransitionAlignmentBox,
            VisualTransitionInDurationBox, VisualTransitionOutDurationBox,
        };

        foreach (var textBox in numericTextBoxes)
        {
            textBox.TextChanged += InspectorControlChanged;
            textBox.TextChanged += (_, _) => ClearInspectorValidation(textBox);
            textBox.PreviewTextInput += NumericTextBox_PreviewTextInput;
            DataObject.AddPastingHandler(textBox, NumericTextBox_Paste);
        }

        foreach (var textBox in new[]
                 {
                     TextContentBox, TextFillColorBox, TextOutlineColorBox, TextShadowColorBox,
                 })
        {
            textBox.TextChanged += InspectorControlChanged;
        }

        foreach (var checkBox in new[] { ReverseToggle, BlackWhiteToggle, StabilizeToggle, FontBoldToggle })
        {
            checkBox.Checked += InspectorControlChanged;
            checkBox.Unchecked += InspectorControlChanged;
        }

        TransitionKindCombo.SelectionChanged += InspectorControlChanged;
        TransitionAudioModeCombo.SelectionChanged += InspectorControlChanged;
        VisualTransitionInCombo.SelectionChanged += InspectorControlChanged;
        VisualTransitionOutCombo.SelectionChanged += InspectorControlChanged;
        FontFamilyCombo.SelectionChanged += InspectorControlChanged;
        FontFamilyCombo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(InspectorControlChanged));
        FontAlignCombo.SelectionChanged += InspectorControlChanged;
    }

    private void BindColorSlider(Slider slider, TextBox textBox)
    {
        slider.ValueChanged += (_, _) =>
        {
            if (_suppressInspectorChangeTracking) return;
            _suppressInspectorChangeTracking = true;
            textBox.Text = Format(slider.Value);
            _suppressInspectorChangeTracking = false;
            SetInspectorDirty(true);
        };

        textBox.LostKeyboardFocus += (_, _) =>
        {
            if (!TryReadNumber(textBox, "color value", out var value)) return;
            _suppressInspectorChangeTracking = true;
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            textBox.Text = Format(slider.Value);
            _suppressInspectorChangeTracking = false;
        };
    }

    private void InspectorControlChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressInspectorChangeTracking) return;
        SetInspectorDirty(true);
    }

    private void SetInspectorDirty(bool dirty)
    {
        _inspectorDirty = dirty && (_selectedInspectorItem != null || _selectedTransitionSelection != null);
        UpdateInspectorActionState();
    }

    private void UpdateInspectorActionState()
    {
        var hasTarget = _selectedInspectorItem != null || _selectedTransitionSelection != null;
        var canEdit = CanEditInspectorTarget();
        ApplyInspectorButton.IsEnabled = hasTarget && canEdit && _inspectorDirty;
        ResetInspectorButton.IsEnabled = hasTarget && _inspectorDirty;
        InspectorDirtyText.Text = !hasTarget
            ? "Select a clip or transition to edit"
            : _inspectorDirty ? "Apply pending changes" : string.Empty;
        InspectorDirtyText.Foreground = _inspectorDirty
            ? (Brush)FindResource("AccentHoverBrush")
            : (Brush)FindResource("TextMutedBrush");
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        e.Handled = !IsNumericTextEditValid(textBox, e.Text);
    }

    private void NumericTextBox_Paste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsNumericTextEditValid(textBox, pastedText))
            e.CancelCommand();
    }

    private static bool IsNumericTextEditValid(TextBox textBox, string incomingText)
    {
        if (string.IsNullOrEmpty(incomingText)) return true;

        var proposed = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, incomingText);

        if (string.IsNullOrWhiteSpace(proposed)
            || proposed == "-"
            || proposed == "."
            || proposed == "-.")
            return true;

        return InspectorValueLogic.TryParseFiniteNumber(proposed, out _);
    }

    private void ResetInspectorEdits()
    {
        if (_selectedTransitionSelection != null)
            UpdateTransitionInspector(_selectedTransitionSelection);
        else
            UpdateInspector(_selectedInspectorItem);
    }

    private bool TryReadNumber(TextBox textBox, string label, out double value)
    {
        if (InspectorValueLogic.TryParseFiniteNumber(textBox.Text, out value))
        {
            ClearInspectorValidation(textBox);
            return true;
        }

        SetInspectorValidation(textBox, $"Enter a finite number for {label}.", label);
        return false;
    }

    private void SetInspectorValidation(Control control, string tooltip, string label)
    {
        control.BorderBrush = (Brush)FindResource("AccentPressedBrush");
        control.BorderThickness = new Thickness(2);
        control.ToolTip = tooltip;
        InspectorDirtyText.Text = $"Invalid {label}";
        InspectorDirtyText.Foreground = (Brush)FindResource("AccentHoverBrush");
        control.Focus();
    }

    private void ClearInspectorValidation(Control control)
    {
        control.BorderBrush = (Brush)FindResource("BorderStrongBrush");
        control.BorderThickness = new Thickness(1);
        if (control.ToolTip is string tooltip
            && (tooltip.StartsWith("Enter a", StringComparison.Ordinal)
                || tooltip.StartsWith("Choose a", StringComparison.Ordinal)))
            control.ToolTip = null;
    }

    private void OpenUtilityPanel(PanelId panelId, TabItem tab)
    {
        if (!_layout.IsPanelOpen(panelId))
        {
            _layout = _layout.WithPanelToggled(panelId, true);
            foreach (var item in PanelsMenu.Items.OfType<MenuItem>())
                if (item.Tag is PanelId id && id == panelId) item.IsChecked = true;
            ApplyLayout();
            SaveLayout();
        }

        tab.IsSelected = true;
    }

    private void OpenInspectorTab(int tabIndex)
    {
        if (!_layout.IsPanelOpen(PanelId.Inspector))
        {
            _layout = _layout.WithPanelToggled(PanelId.Inspector, true);
            foreach (var item in PanelsMenu.Items.OfType<MenuItem>())
                if (item.Tag is PanelId id && id == PanelId.Inspector) item.IsChecked = true;
            ApplyLayout();
        }

        EnsureInspectorCoreTabVisible(tabIndex);
    }

    private void ConstrainWindowToWorkingArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0) return;

        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        if (WindowState == WindowState.Normal)
        {
            Width = Math.Min(Math.Max(Width, MinWidth), workArea.Width);
            Height = Math.Min(Math.Max(Height, MinHeight), workArea.Height);

            var left = double.IsNaN(Left) ? workArea.Left : Left;
            var top = double.IsNaN(Top) ? workArea.Top : Top;
            Left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }

        UpdateWindowFrame();
    }

    private void UpdateWindowFrame()
    {
        var isEdgeToEdge = WindowState == WindowState.Maximized;
        WindowFrameBorder.BorderThickness = isEdgeToEdge ? new Thickness(0) : new Thickness(1);
        WindowFrameBorder.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(10);
        MaximizeWindowIcon.Data = Geometry.Parse(isEdgeToEdge
            ? "M6,3 L15,3 L15,12 L12,12 M3,6 L12,6 L12,15 L3,15 Z"
            : "M4,4 L14,4 L14,14 L4,14 Z");
        MaximizeWindowButton.ToolTip = isEdgeToEdge ? "Restore" : "Maximize";

        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            chrome.ResizeBorderThickness = isEdgeToEdge ? new Thickness(0) : new Thickness(6);
            chrome.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(10);
        }

        if (isEdgeToEdge)
        {
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }

        UpdateResponsiveLayout();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var monitorInfo = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.RcWork;
        var monitorArea = monitorInfo.RcMonitor;

        minMaxInfo.PtMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        minMaxInfo.PtMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        minMaxInfo.PtMaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.PtMaxSize.Y = workArea.Bottom - workArea.Top;
        minMaxInfo.PtMaxTrackSize = minMaxInfo.PtMaxSize;

        var dpi = VisualTreeHelper.GetDpi(this);
        var minimumWidthPixels = Math.Max(1, (int)Math.Ceiling(MinWidth * dpi.DpiScaleX));
        var minimumHeightPixels = Math.Max(1, (int)Math.Ceiling(MinHeight * dpi.DpiScaleY));
        minMaxInfo.PtMinTrackSize.X = Math.Min(minMaxInfo.PtMaxTrackSize.X, minimumWidthPixels);
        minMaxInfo.PtMinTrackSize.Y = Math.Min(minMaxInfo.PtMaxTrackSize.Y, minimumHeightPixels);

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint PtReserved;
        public NativePoint PtMaxSize;
        public NativePoint PtMaxPosition;
        public NativePoint PtMinTrackSize;
        public NativePoint PtMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int CbSize;
        public NativeRect RcMonitor;
        public NativeRect RcWork;
        public uint DwFlags;
    }

    private Window CreateOwnedDialog(
        string title,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        ResizeMode resizeMode)
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(320, workArea.Width - 32);
        var availableHeight = Math.Max(220, workArea.Height - 32);

        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = Math.Min(width, availableWidth),
            Height = Math.Min(height, availableHeight),
            MinWidth = Math.Min(minimumWidth, availableWidth),
            MinHeight = Math.Min(minimumHeight, availableHeight),
            MaxWidth = availableWidth,
            MaxHeight = availableHeight,
            ResizeMode = resizeMode,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextBrush"),
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        return dialog;
    }

    private Border CreateDialogFrame(Window dialog, string title, UIElement content, Thickness padding)
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Background = (Brush)FindResource("ChromeBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
        };
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed)
                dialog.DragMove();
        };

        var headerGrid = new Grid { Margin = new Thickness(14, 0, 0, 0) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var closeButton = new Button
        {
            Content = "X",
            Style = (Style)FindResource("WindowControlButtonStyle"),
            ToolTip = "Close",
        };
        closeButton.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(closeButton);
        header.Child = headerGrid;
        shell.Children.Add(header);

        var body = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            Padding = padding,
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Child = content,
        };
        Grid.SetRow(body, 1);
        shell.Children.Add(body);

        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            Child = shell,
        };
    }

    private static Visibility Vis(bool open) => open ? Visibility.Visible : Visibility.Collapsed;
    private void SaveLayout() => _workspaceService.Save(_layout);

    private void ApplyEditorSettings()
    {
        _settings.TimelineZoom = Math.Clamp(_settings.TimelineZoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        _settings.AutosaveIntervalSeconds = Math.Clamp(_settings.AutosaveIntervalSeconds, 5, 3600);
        SetUiScale(_settings.UiScale, persist: false);

        RippleToggle.IsChecked = _settings.RippleEnabled;
        SnapToggle.IsChecked = _settings.SnapEnabled;
        ZoomSlider.Value = _settings.TimelineZoom;
        _rippleState.Enabled = _settings.RippleEnabled;
        if (_timeline != null)
        {
            _timeline.SnapEnabled = _settings.SnapEnabled;
            _timeline.SetZoomScale(_settings.TimelineZoom);
        }
    }

    private void ChangeUiScale(double delta) => SetUiScale(_settings.UiScale + delta, persist: true);

    private void SetUiScale(double scale, bool persist)
    {
        _settings.UiScale = Math.Clamp(Math.Round(scale, 2), MinimumUiScale, MaximumUiScale);
        WindowFrameBorder.LayoutTransform = new ScaleTransform(_settings.UiScale, _settings.UiScale);
        StatusText.Text = $"UI scale: {Math.Round(_settings.UiScale * 100)}%";
        UpdateResponsiveLayout();
        ConstrainWindowToWorkingArea();
        if (persist)
            _settingsService.Save("editor", _settings);
    }

    private void RestartAutosave()
    {
        var interval = Math.Clamp(_settings.AutosaveIntervalSeconds, 5, 3600);
        _saveCoordinator.ConfigureAutosave(
            _settings.AutosaveEnabled,
            TimeSpan.FromSeconds(interval));
        AutosaveStatusText.Text = _settings.AutosaveEnabled
            ? $"Autosave: {interval} seconds · edit bursts coalesced"
            : "Autosave: off";
    }

    private void ShowAutosaveSaved(string path)
    {
        AutosaveStatusText.Text = $"Autosaved {DateTime.Now:HH:mm:ss}";
        AutosaveStatusText.ToolTip = path;
    }

    private async Task RecoverLatestAutosaveAsync()
    {
        if (!await ConfirmCanReplaceCurrentProjectAsync()) return;

        try
        {
            var restored = await _autosave.LoadMostRecentAsync();
            if (restored == null)
            {
                AutosaveStatusText.Text = "No autosave is available";
                return;
            }

            LoadProjectIntoEditor(restored, null, $"{restored.Name} (recovered)");
            _projectDirty = true;
            AutosaveStatusText.Text = "Recovered latest autosave";
        }
        catch (Exception ex)
        {
            AutosaveStatusText.Text = $"Autosave recovery failed: {ex.Message}";
        }
    }

    private async Task LoadOptionalCapabilitiesAsync()
    {
        if (_optionalCapabilitiesLoaded) return;
        _optionalCapabilitiesLoaded = true;
        try
        {
            var assetDirectory = Path.Combine(_appData, "asset-packs");
            var extensionDirectory = Path.Combine(_appData, "extensions");
            var providersTask = Task.Run(() => _creativeAssetPackService.LoadDirectory(assetDirectory));
            var extensionsTask = Task.Run(() => _extensionManifestService.LoadDirectory(extensionDirectory));
            await Task.WhenAll(providersTask, extensionsTask);

            _installedAssetProviders.Clear();
            _installedAssetProviders.AddRange(await providersTask);
            _installedExtensions.Clear();
            _installedExtensions.AddRange(await extensionsTask);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Optional asset capabilities unavailable: {ex.Message}";
        }
    }

    private async Task EnsureFontsLoadedAsync()
    {
        try
        {
            if (!_fontsLoaded)
            {
                _systemFontNames = await Task.Run(() => Fonts.SystemFontFamilies
                    .Select(font => font.Source)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
                _fontsLoaded = true;
            }

            var selectedValue = _selectedInspectorItem?.FontFamily ?? FontFamilyCombo.Text;
            var projectFonts = _project.MediaLibrary
                .Where(asset => asset.Kind == MediaKind.Font && !asset.IsOffline && File.Exists(asset.OriginalPath))
                .Select(asset => new InspectorFontChoice($"Project · {Path.GetFileName(asset.OriginalPath)}", asset.OriginalPath, true))
                .OrderBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase);

            _inspectorFontChoices.Clear();
            _inspectorFontChoices.AddRange(projectFonts);
            _inspectorFontChoices.AddRange(_systemFontNames.Select(name => new InspectorFontChoice(name, name, false)));

            _suppressInspectorChangeTracking = true;
            try
            {
                FontFamilyCombo.ItemsSource = _inspectorFontChoices.ToArray();
                SelectInspectorFont(selectedValue);
            }
            finally
            {
                _suppressInspectorChangeTracking = false;
            }
        }
        catch
        {
            _fontsLoaded = false;
            throw;
        }
    }

    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e) => ShowSettingsDialog();

    private async Task StartIntelligenceBackendAsync()
    {
        var repoRoot = FindRepoRoot();
        var apiKey = (_settings.ProtectedGroqApiKeys ?? [])
            .Select(SecretProtectionService.Unprotect)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        StatusText.Text = "Starting intelligence backend…";
        var started = await _intelligenceBackend.StartAsync(
            repoRoot,
            apiKey,
            _localAgentBridge.SessionToken,
            _soundLibraryCatalogService.CatalogPath);
        StatusText.Text = started
            ? $"Intelligence backend ready on {_intelligenceBackend.BaseUri}"
            : "Intelligence backend unavailable — run the intelligence setup script";
    }

    private void ShowLocalAgentSetupDialog()
    {
        const string coreCommand = ".\\scripts\\setup-intelligence.ps1";
        const string advancedCommand = ".\\scripts\\setup-intelligence.ps1 -Advanced";
        const string doctorCommand = ".\\tools\\intelligence-venv\\Scripts\\python.exe -m rushframe_intelligence doctor --ffmpeg .\\tools\\bin\\ffmpeg.exe";

        var dialog = CreateOwnedDialog(
            "MCP / Local Agent Setup",
            width: 620,
            height: 650,
            minimumWidth: 520,
            minimumHeight: 520,
            resizeMode: ResizeMode.CanResize);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Local Agent Setup",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentHoverBrush"),
            Margin = new Thickness(0, 0, 0, 14),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Rushframe starts the local intelligence backend automatically. Local agents can use the backend context and search endpoints while the editor is open.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 16),
        });
        panel.Children.Add(CreateSetupSection(
            "Required on the computer",
            "• .NET 10 SDK\n• Python 3\n• FFmpeg at .tools\\bin\\ffmpeg.exe\n• 16 GB RAM is enough for the core CPU setup"));
        panel.Children.Add(CreateSetupSection(
            "Recommended core install",
            coreCommand + "\n\nInstalls scene detection, faster-whisper, audio/beat analysis, OpenCV, semantic search, and Groq visual support."));
        panel.Children.Add(CreateSetupSection(
            "Optional advanced install",
            advancedCommand + "\n\nAdds WhisperX word alignment, speaker detection, PaddleOCR, and sound-event recognition. This is much heavier and is not recommended on a CPU-only 16 GB machine unless needed."));
        panel.Children.Add(CreateSetupSection(
            "Verify installation",
            doctorCommand));
        panel.Children.Add(CreateSetupSection(
            "Local agent connection",
            $"MCP endpoint: {GetMcpEndpoint()}\nHealth: {_intelligenceBackend.BaseUri}health\nTools: rushframe.capabilities, rushframe.get_editing_context, rushframe.search_moments, rushframe.get_agent_context, rushframe.get_timeline_state, rushframe.preview_edit_plan, rushframe.review_edit_plan, rushframe.apply_edit_plan, rushframe.apply_timeline_edit, rushframe.render_timeline\nThe endpoint and live editor bridge are local-only and available while Rushframe is running."));
        panel.Children.Add(CreateSetupSection(
            "Recommended settings for 16 GB RAM / no GPU",
            "Whisper: base, CPU int8\nAI input: 5–10 minutes\nVisual provider: GroqCloud\nOCR/alignment/diarization/sound events: Off\nParallel analysis jobs: 1"));

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var copyCore = new Button { Content = "Copy Core Install", MinWidth = 130, Margin = new Thickness(0, 0, 8, 8) };
        var copyAdvanced = new Button { Content = "Copy Advanced Install", MinWidth = 150, Margin = new Thickness(0, 0, 8, 8) };
        var copyMcp = new Button { Content = "Copy MCP Config", MinWidth = 130, Margin = new Thickness(0, 0, 8, 8) };
        var saveConfigs = new Button { Content = "Save Config Files", MinWidth = 130, Margin = new Thickness(0, 0, 8, 8) };
        var openHealth = new Button { Content = "Open Health", MinWidth = 100, Margin = new Thickness(0, 0, 8, 8) };
        var close = new Button { Content = "Close", MinWidth = 86, Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(0, 0, 0, 8) };
        copyCore.Click += (_, _) => CopyAgentInstallCommand(advanced: false);
        copyAdvanced.Click += (_, _) => CopyAgentInstallCommand(advanced: true);
        copyMcp.Click += (_, _) => CopyMcpJsonConfig();
        saveConfigs.Click += (_, _) => SaveMcpConfigFiles();
        openHealth.Click += (_, _) => OpenAgentBackendHealth();
        close.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(copyCore);
        buttons.Children.Add(copyAdvanced);
        buttons.Children.Add(copyMcp);
        buttons.Children.Add(saveConfigs);
        buttons.Children.Add(openHealth);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);

        dialog.Content = CreateDialogFrame(
            dialog,
            "MCP / Local Agents",
            new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            new Thickness(18));
        dialog.ShowDialog();
    }

    private Border CreateSetupSection(string title, string body)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            LineHeight = 19,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return new Border
        {
            Child = stack,
            Background = (Brush)FindResource("PanelRaisedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
        };
    }

    private void ShowLocalAgentStatusDialog()
    {
        var repoRoot = FindRepoRoot();
        var dialog = CreateOwnedDialog(
            "MCP / Local Agent Setup",
            width: 720,
            height: 640,
            minimumWidth: 520,
            minimumHeight: 500,
            resizeMode: ResizeMode.CanResize);

        var statusPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var logBox = new TextBox
        {
            IsReadOnly = true,
            MinHeight = 108,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)FindResource("InputBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 0),
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = (Brush)FindResource("AccentSurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock
        {
            Text = "Local Agent Setup",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = "Check and install the local media-intelligence environment used by Rushframe and MCP agents.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        });
        header.Child = headerStack;
        root.Children.Add(header);

        var panel = new StackPanel();
        panel.Children.Add(statusPanel);
        panel.Children.Add(CreateSetupSection(
            "Local agent connection",
            $"MCP endpoint: {GetMcpEndpoint()}\nHealth: {_intelligenceBackend.BaseUri}health\nTools: rushframe.capabilities, rushframe.get_editing_context, rushframe.search_moments, rushframe.get_agent_context, rushframe.get_timeline_state, rushframe.preview_edit_plan, rushframe.review_edit_plan, rushframe.apply_edit_plan, rushframe.apply_timeline_edit, rushframe.render_timeline\nThe endpoint and live editor bridge are local-only and available while Rushframe is running."));
        panel.Children.Add(CreateSetupSection(
            "Recommended settings for 16 GB RAM / no GPU",
            "Whisper: base, CPU int8\nAI input: 5-10 minutes\nVisual provider: GroqCloud\nOCR/alignment/diarization/sound events: Off\nParallel analysis jobs: 1"));
        panel.Children.Add(new TextBlock
        {
            Text = "Setup log",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 2, 0, 8),
        });
        panel.Children.Add(logBox);

        var scroller = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var refresh = new Button { Content = "Check Status", MinWidth = 96, Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        var installCore = new Button { Content = "Install Core", MinWidth = 104, Height = 32, Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(0, 0, 6, 0) };
        var installAdvanced = new Button { Content = "Install Advanced", MinWidth = 116, Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        var saveConfigs = new Button { Content = "Save Configs", MinWidth = 108, Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        var openHealth = new Button { Content = "Health", MinWidth = 74, Height = 32, Margin = new Thickness(0, 0, 6, 0) };
        var close = new Button { Content = "Close", MinWidth = 74, Height = 32 };

        refresh.Click += async (_, _) => await RefreshLocalAgentStatusAsync(statusPanel, logBox, repoRoot);
        installCore.Click += async (_, _) => await RunIntelligenceSetupAsync(advanced: false, statusPanel, logBox, repoRoot);
        installAdvanced.Click += async (_, _) => await RunIntelligenceSetupAsync(advanced: true, statusPanel, logBox, repoRoot);
        saveConfigs.Click += (_, _) =>
        {
            SaveMcpConfigFiles();
            _ = RefreshLocalAgentStatusAsync(statusPanel, logBox, repoRoot);
        };
        openHealth.Click += (_, _) => OpenAgentBackendHealth();
        close.Click += (_, _) => dialog.DialogResult = true;

        buttons.Children.Add(refresh);
        buttons.Children.Add(installCore);
        buttons.Children.Add(installAdvanced);
        buttons.Children.Add(saveConfigs);
        buttons.Children.Add(openHealth);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = CreateDialogFrame(
            dialog,
            "MCP / Local Agents",
            root,
            new Thickness(18));
        _ = RefreshLocalAgentStatusAsync(statusPanel, logBox, repoRoot);
        dialog.ShowDialog();
    }

    private async Task RefreshLocalAgentStatusAsync(StackPanel statusPanel, TextBox logBox, string repoRoot)
    {
        statusPanel.Children.Clear();
        var python = Path.Combine(repoRoot, ".tools", "intelligence-venv", "Scripts", "python.exe");
        var ffmpeg = Path.Combine(repoRoot, ".tools", "bin", "ffmpeg.exe");
        var setupScript = Path.Combine(repoRoot, "scripts", "setup-intelligence.ps1");
        var mcpJson = Path.Combine(_appData, "mcp", "rushframe-mcp.json");
        var codexToml = Path.Combine(_appData, "mcp", "rushframe-codex.toml");
        var backendHealthy = await _intelligenceBackend.IsHealthyAsync();

        AddSetupStatusRow(statusPanel, "Python environment", File.Exists(python), python);
        AddSetupStatusRow(statusPanel, "FFmpeg", File.Exists(ffmpeg), ffmpeg);
        AddSetupStatusRow(statusPanel, "Setup script", File.Exists(setupScript), setupScript);
        AddSetupStatusRow(statusPanel, "MCP JSON config", File.Exists(mcpJson), mcpJson);
        AddSetupStatusRow(statusPanel, "Codex TOML config", File.Exists(codexToml), codexToml);
        AddSetupStatusRow(statusPanel, "Backend health", backendHealthy, _intelligenceBackend.BaseUri.ToString());

        logBox.Text = backendHealthy
            ? "Local agent backend is reachable."
            : "Backend is not reachable yet. Install core dependencies, then restart Rushframe or open the health page after startup.";
    }

    private async Task RunIntelligenceSetupAsync(bool advanced, StackPanel statusPanel, TextBox logBox, string repoRoot)
    {
        var script = Path.Combine(repoRoot, "scripts", "setup-intelligence.ps1");
        if (!File.Exists(script))
        {
            logBox.Text = $"Setup script not found: {script}";
            return;
        }

        logBox.Text = advanced ? "Installing advanced intelligence packages..." : "Installing core intelligence packages...";
        StatusText.Text = logBox.Text;
        var args = advanced
            ? $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Advanced"
            : $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
        var result = await RunProcessAsync("powershell", args, repoRoot);
        logBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                result.StandardOutput.Trim(),
                result.StandardError.Trim(),
                $"Exit code: {result.ExitCode}",
            }.Where(text => !string.IsNullOrWhiteSpace(text)));
        StatusText.Text = result.ExitCode == 0
            ? "Intelligence setup completed"
            : "Intelligence setup failed";
        await RefreshLocalAgentStatusAsync(statusPanel, logBox, repoRoot);
    }

    private void AddSetupStatusRow(StackPanel panel, string label, bool ok, string detail)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel();
        labelStack.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        labelStack.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 3, 12, 0),
        });
        row.Children.Add(labelStack);

        var badge = new Border
        {
            Background = ok ? (Brush)FindResource("AccentSurfaceBrush") : (Brush)FindResource("PanelBrush"),
            BorderBrush = ok ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            MinWidth = 74,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = ok ? "Installed" : "Missing",
                Foreground = ok ? (Brush)FindResource("AccentHoverBrush") : (Brush)FindResource("TextMutedBrush"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        panel.Children.Add(new Border
        {
            Background = (Brush)FindResource("PanelRaisedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row,
        });
    }

    private void CopyAgentInstallCommand(bool advanced)
    {
        var command = advanced
            ? ".\\scripts\\setup-intelligence.ps1 -Advanced"
            : ".\\scripts\\setup-intelligence.ps1";
        Clipboard.SetText(command);
        StatusText.Text = advanced ? "Advanced intelligence install command copied" : "Core intelligence install command copied";
    }

    private string GetMcpEndpoint() => new Uri(_intelligenceBackend.BaseUri, "mcp").ToString();

    private string GetMcpJsonConfig() => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["rushframe"] = new
            {
                url = GetMcpEndpoint(),
                headers = new Dictionary<string, string>
                {
                    ["X-Rushframe-Session"] = _localAgentBridge.SessionToken,
                },
            },
        },
    }, new JsonSerializerOptions { WriteIndented = true });

    private string GetCodexMcpConfig() =>
        $"[mcp_servers.rushframe]{Environment.NewLine}" +
        $"url = \"{GetMcpEndpoint()}\"{Environment.NewLine}" +
        $"http_headers = {{ \"X-Rushframe-Session\" = \"{_localAgentBridge.SessionToken}\" }}{Environment.NewLine}";

    private void CopyMcpUrl()
    {
        Clipboard.SetText(GetMcpEndpoint());
        StatusText.Text = "Rushframe MCP URL copied";
    }

    private void CopyMcpJsonConfig()
    {
        Clipboard.SetText(GetMcpJsonConfig());
        StatusText.Text = "Rushframe MCP JSON config copied";
    }

    private void CopyCodexMcpConfig()
    {
        Clipboard.SetText(GetCodexMcpConfig());
        StatusText.Text = "Rushframe Codex MCP config copied";
    }

    private void SaveMcpConfigFiles()
    {
        var directory = Path.Combine(_appData, "mcp");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "rushframe-mcp.json"), GetMcpJsonConfig(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "rushframe-codex.toml"), GetCodexMcpConfig(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        StatusText.Text = "Rushframe MCP config files saved";
    }

    private void OpenAgentBackendHealth()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = new Uri(_intelligenceBackend.BaseUri, "health").ToString(),
            UseShellExecute = true,
        });
    }

    private async Task<object> HandleLocalAgentBridgeRequestAsync(
        string path,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
            return await HandleLocalAgentBridgeRequestOnUiThreadAsync(path, payload, cancellationToken);

        var operation = Dispatcher.InvokeAsync(() =>
            HandleLocalAgentBridgeRequestOnUiThreadAsync(path, payload, cancellationToken));
        return await await operation.Task;
    }

    private async Task<object> HandleLocalAgentBridgeRequestOnUiThreadAsync(
        string path,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        return path switch
        {
            "" or "health" => new { ok = true, service = "rushframe-editor-bridge", schemaVersion = 2 },
            "capabilities" => BuildAgentCapabilities(),
            "timeline" => BuildAgentTimelineState(),
            "transcript" => BuildAgentTranscriptState(payload ?? default),
            "search-context" => BuildAgentMediaSearch(payload ?? default),
            "agent-context" => BuildAgentMediaContext(payload ?? default),
            "editing-context" => BuildAgentEditingContext(payload ?? default),
            "sound-library-registrations" => BuildAgentSoundLibraryRegistrations(),
            "workflow" => BuildAgentWorkflowState(),
            "providers" => BuildAgentProviderState(),
            "upsert-provider" => UpsertAgentProvider(payload ?? default),
            "estimate-cost" => EstimateAgentProviderCost(payload ?? default),
            "reconcile-cost" => ReconcileAgentProviderCost(payload ?? default),
            "set-workflow-stage" => SetAgentWorkflowStage(payload ?? default),
            "record-decision" => RecordAgentProductionDecision(payload ?? default),
            "variants" => BuildAgentVariantState(),
            "upsert-variant" => UpsertAgentVariant(payload ?? default),
            "render-variant" => await RenderAgentVariantAsync(payload ?? default, cancellationToken),
            "compositions" => BuildAgentCompositionState(),
            "register-composition" => RegisterAgentComposition(payload ?? default),
            "render-composition" => await RenderAgentCompositionAsync(payload ?? default, cancellationToken),
            "render-jobs" => new { ok = true, jobs = _project.RenderJobs.TakeLast(50).ToArray() },
            "retry-render-job" => await RetryAgentRenderJobAsync(payload ?? default, cancellationToken),
            "receipts" => new { ok = true, receipts = _project.RenderReceipts.TakeLast(50).ToArray() },
            "audit" => new { ok = true, entries = _agentAuditLog.TakeLast(50).ToArray() },
            "plan" => PreviewAgentEditPlan(payload ?? default),
            "review-plan" => await ReviewAgentEditPlanAsync(payload ?? default, cancellationToken),
            "apply-plan" => await ApplyAgentEditPlanAsync(payload ?? default, cancellationToken),
            "edit" => await ApplyAgentTimelineEditAsync(payload ?? default, cancellationToken),
            "render" => await RenderAgentTimelineAsync(payload ?? default, cancellationToken),
            _ => new { ok = false, error = $"Unknown bridge endpoint: {path}" },
        };
    }

    private object BuildAgentCapabilities() => new
    {
        ok = true,
        protocolVersion = 2,
        projectSchemaVersion = Project.CurrentSchemaVersion,
        editingContext = new
        {
            endpoint = "editing-context",
            schemaVersion = AgentEditingContextBuilder.SchemaVersion,
            compact = true,
            bounded = true,
            pathSafe = true,
            includes = new[] { "campaign", "editing-brief", "tasks", "playhead", "selection", "timeline-summary", "locks", "media-readiness", "quality-issues", "edit-skill-summary" },
        },
        editPlan = new
        {
            endpoint = "apply-plan",
            previewEndpoint = "plan",
            roughCutReviewEndpoint = "review-plan",
            maximumOperations = 100,
            atomic = true,
            undoable = true,
            revisionRequired = true,
            approvalDefault = true,
            actionCatalogVersion = AgentEditSkillCatalog.SchemaVersion,
            actions = AgentEditCommandFactory.SupportedActions,
            skills = AgentEditSkillCatalog.Skills,
        },
        recommendedWorkflow = new[]
        {
            "get_editing_context",
            "search_moments_when_needed",
            "preview_edit_plan",
            "review_edit_plan_when_quality_risk_is_material",
            "apply_edit_plan_after_user_approval",
            "refresh_context_after_revision_change",
        },
        evidence = new[] { "editing-context", "timeline", "transcript", "workflow", "variants", "compositions", "receipts", "audit", "sound-library-registrations" },
        constraints = new[]
        {
            "registered-local-media-only",
            "catalog-sounds-require-explicit-project-registration-before-agent-use",
            "no-source-file-mutation",
            "locked-items-and-tracks-are-protected",
            "manual-edits-win-through-project-revision-conflicts",
            "external-compositions-render-to-local-assets",
        },
    };

    private object BuildAgentTranscriptState(JsonElement payload)
    {
        MediaIntelligenceAnalysis? analysis = null;
        if (AgentPayloadReader.ReadString(payload, "media_asset_id") is { Length: > 0 } assetText
            && Guid.TryParse(assetText, out var assetGuid))
        {
            analysis = _project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == new MediaAssetId(assetGuid));
        }
        else if (_project.MediaIntelligence.Count == 1)
        {
            analysis = _project.MediaIntelligence[0];
        }

        if (analysis == null)
            return new { ok = false, error = "Select media_asset_id when more than one analyzed asset exists" };

        var start = Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "start", 0));
        var end = AgentPayloadReader.ReadSeconds(payload, "end", analysis.Metadata.Duration.Seconds);
        var segments = analysis.Transcript
            .Where(segment => segment.End.Seconds > start && segment.Start.Seconds < end)
            .Select(segment => new
            {
                id = segment.SegmentId,
                start = segment.Start.Seconds,
                end = segment.End.Seconds,
                text = segment.Text,
                speaker = segment.Speaker,
                confidence = segment.Confidence,
                containsFiller = segment.ContainsFiller,
                repeatedTake = segment.RepeatedTake,
                hookScore = segment.HookScore,
                recommendedUse = segment.RecommendedUse,
                words = segment.Words.Select(word => new
                {
                    start = word.Start.Seconds,
                    end = word.End.Seconds,
                    text = word.Text,
                    confidence = word.Confidence,
                }),
            })
            .ToArray();
        return new
        {
            ok = true,
            mediaAssetId = analysis.MediaAssetId.ToString(),
            duration = analysis.Metadata.Duration.Seconds,
            segments,
            silence = analysis.Audio.Silence
                .Where(range => range.End.Seconds > start && range.Start.Seconds < end)
                .Select(range => new { start = range.Start.Seconds, end = range.End.Seconds, duration = range.Duration.Seconds }),
            audioEvents = analysis.Audio.Events
                .Where(value => value.End.Seconds > start && value.Start.Seconds < end)
                .Select(value => new { id = value.EventId, start = value.Start.Seconds, end = value.End.Seconds, type = value.EventType, value.Label, value.Confidence, value.Speaker }),
            moments = analysis.Moments
                .Where(value => value.End.Seconds > start && value.Start.Seconds < end)
                .OrderByDescending(value => value.Scores.Overall)
                .Select(value => new
                {
                    id = value.MomentId,
                    start = value.Start.Seconds,
                    end = value.End.Seconds,
                    value.Summary,
                    roles = value.EditingRoles,
                    value.Tags,
                    score = value.Scores.Overall,
                    hook = value.Scores.HookPotential,
                    value.Confidence,
                }),
            duplicateTakeGroups = analysis.DuplicateTakeGroups,
            warnings = analysis.Warnings,
            editPolicy = _project.TranscriptEditPolicy,
        };
    }

    private object BuildAgentMediaSearch(JsonElement payload)
    {
        var analysis = ResolveAgentMediaAnalysis(payload);
        var query = AgentPayloadReader.ReadString(payload, "query") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("query is required");
        var limit = Math.Clamp(AgentPayloadReader.ReadInt(payload, "limit", 12), 1, 50);
        var minimumScore = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "min_score", 0), 0, 1);
        double? maximumDuration = AgentPayloadReader.HasProperty(payload, "max_duration")
            ? Math.Max(0.001, AgentPayloadReader.ReadSeconds(payload, "max_duration", 0))
            : null;
        var results = _mediaIntelligenceSearchService.Search(
            analysis,
            new MediaMomentSearchQuery(
                query,
                analysis.MediaAssetId,
                ReadStringArray(payload, "roles"),
                minimumScore,
                maximumDuration,
                limit));
        return new
        {
            ok = true,
            mediaAssetId = analysis.MediaAssetId.ToString(),
            results,
        };
    }

    private object BuildAgentMediaContext(JsonElement payload)
    {
        var analysis = ResolveAgentMediaAnalysis(payload);
        var context = _mediaAgentContextBuilder.Build(
            analysis,
            new MediaAgentContextRequest(
                AgentPayloadReader.ReadString(payload, "query") ?? string.Empty,
                analysis.MediaAssetId,
                ReadStringArray(payload, "roles"),
                Math.Clamp(AgentPayloadReader.ReadInt(payload, "limit", 20), 1, 50),
                Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "min_score", 0), 0, 1)));
        var relationships = _project.MediaRelationships
            .Where(value => value.Source.MediaAssetId == analysis.MediaAssetId || value.Target.MediaAssetId == analysis.MediaAssetId)
            .OrderByDescending(value => value.Score)
            .Take(100)
            .ToArray();
        return new { ok = true, context, relationships };
    }

    private object BuildAgentEditingContext(JsonElement payload)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return new { ok = false, error = "No active sequence" };

        var context = AgentEditingContextBuilder.Build(
            _project,
            sequence,
            _timeline?.PlayheadTime.Seconds ?? 0,
            _timeline?.SelectedItem?.Id,
            new AgentEditingContextRequest(
                Math.Clamp(AgentPayloadReader.ReadInt(payload, "item_limit", 250), 25, 500),
                Math.Clamp(AgentPayloadReader.ReadInt(payload, "media_asset_limit", 200), 1, 500),
                AgentPayloadReader.ReadBool(payload, "include_completed_tasks", false)));

        object? mediaContext = null;
        IReadOnlyList<MediaRelationship> relationships = [];
        var includeMediaContext = AgentPayloadReader.ReadBool(payload, "include_media_context", true);
        var availableAnalyses = _project.MediaIntelligence
            .Where(candidate => _project.MediaLibrary.Any(asset => asset.Id == candidate.MediaAssetId))
            .Take(2)
            .ToArray();
        if (includeMediaContext)
        {
            MediaIntelligenceAnalysis? analysis = null;
            if (AgentPayloadReader.ReadString(payload, "media_asset_id") is { Length: > 0 })
                analysis = ResolveAgentMediaAnalysis(payload);
            else if (availableAnalyses.Length == 1)
                analysis = availableAnalyses[0];

            if (analysis != null)
            {
                var rawContext = _mediaAgentContextBuilder.Build(
                    analysis,
                    new MediaAgentContextRequest(
                        AgentPayloadReader.ReadString(payload, "query") ?? string.Empty,
                        analysis.MediaAssetId,
                        ReadStringArray(payload, "roles"),
                        Math.Clamp(AgentPayloadReader.ReadInt(payload, "moment_limit", 12), 1, 50),
                        Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "min_score", 0), 0, 1)));
                mediaContext = new
                {
                    rawContext.ContextSchemaVersion,
                    source = new
                    {
                        rawContext.Source.MediaAssetId,
                        sourceName = Path.GetFileName(rawContext.Source.SourcePath),
                        rawContext.Source.SourceChecksum,
                        rawContext.Source.DurationSeconds,
                        rawContext.Source.Orientation,
                        rawContext.Source.Width,
                        rawContext.Source.Height,
                        rawContext.Source.FramesPerSecond,
                        rawContext.Source.HasVideo,
                        rawContext.Source.HasAudio,
                    },
                    rawContext.Summary,
                    rawContext.Moments,
                    rawContext.DuplicateTakeGroups,
                    rawContext.Warnings,
                };
                relationships = _project.MediaRelationships
                    .Where(value => value.Source.MediaAssetId == analysis.MediaAssetId || value.Target.MediaAssetId == analysis.MediaAssetId)
                    .OrderByDescending(value => value.Score)
                    .Take(100)
                    .ToArray();
            }
        }

        return new
        {
            ok = true,
            context,
            mediaContext,
            mediaContextSelectionRequired = includeMediaContext && mediaContext == null && availableAnalyses.Length > 1,
            relationships,
        };
    }

    private MediaIntelligenceAnalysis ResolveAgentMediaAnalysis(JsonElement payload)
    {
        if (AgentPayloadReader.ReadString(payload, "media_asset_id") is { Length: > 0 } assetText)
        {
            if (!Guid.TryParse(assetText, out var assetGuid))
                throw new InvalidOperationException("media_asset_id is invalid");
            var assetId = new MediaAssetId(assetGuid);
            if (_project.MediaLibrary.All(asset => asset.Id != assetId))
                throw new InvalidOperationException("Media asset is not registered in the open project");
            return _project.MediaIntelligence.FirstOrDefault(candidate => candidate.MediaAssetId == assetId)
                   ?? throw new InvalidOperationException("Registered media has no loaded analysis");
        }

        if (_project.MediaIntelligence.Count == 1)
        {
            var analysis = _project.MediaIntelligence[0];
            if (_project.MediaLibrary.Any(asset => asset.Id == analysis.MediaAssetId)) return analysis;
        }

        throw new InvalidOperationException("media_asset_id is required when the open project has zero or multiple analyzed assets");
    }

    private object BuildAgentWorkflowState() => new
    {
        ok = true,
        activeStageId = _project.Workflow.ActiveStageId,
        budget = new
        {
            limitUsd = _project.Workflow.BudgetLimitUsd,
            estimatedUsd = _project.Workflow.EstimatedSpendUsd,
            actualUsd = _project.Workflow.ActualSpendUsd,
        },
        stages = _project.Workflow.Stages,
        decisions = _project.Workflow.Decisions,
    };

    private object BuildAgentProviderState() => new
    {
        ok = true,
        policy = new
        {
            localFirst = _project.Workflow.LocalFirst,
            paidProvidersEnabled = _project.Workflow.PaidProvidersEnabled,
            budgetMode = _project.Workflow.BudgetMode,
            budgetLimitUsd = _project.Workflow.BudgetLimitUsd,
            estimatedSpendUsd = _project.Workflow.EstimatedSpendUsd,
            actualSpendUsd = _project.Workflow.ActualSpendUsd,
            requireApprovalForPaidOperations = _project.Workflow.RequireApprovalForPaidOperations,
            singleActionApprovalThresholdUsd = _project.Workflow.SingleActionApprovalThresholdUsd,
        },
        providers = _project.AutomationProviders,
        costEvents = _project.Workflow.CostEvents.TakeLast(100).ToArray(),
    };

    private object UpsertAgentProvider(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var providerId = AgentPayloadReader.ReadRequiredString(payload, "provider_id");
        var existing = _project.AutomationProviders.FirstOrDefault(candidate => candidate.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        var local = AgentPayloadReader.ReadBool(payload, "local", existing?.Local ?? false);
        var paid = AgentPayloadReader.ReadBool(payload, "paid", existing?.Paid ?? false);
        var enabled = AgentPayloadReader.ReadBool(payload, "enabled", existing?.Enabled ?? false);
        var endpoint = AgentPayloadReader.ReadString(payload, "endpoint") ?? existing?.Endpoint;
        var endpointError = ValidateProviderEndpoint(local, endpoint);
        if (endpointError != null) return new { ok = false, error = endpointError };
        var enablePaidPolicy = AgentPayloadReader.ReadNullableBool(payload, "paid_providers_enabled");
        var paidPolicyWillBeEnabled = enablePaidPolicy ?? _project.Workflow.PaidProvidersEnabled;
        if (paid && enabled && !paidPolicyWillBeEnabled)
            return new { ok = false, error = "Paid providers are disabled for this project. Enable the paid-provider policy with explicit user approval first." };
        if (paid && enabled && !ConfirmAgentEdit($"Enable paid provider '{providerId}'? Charges may be incurred."))
            return new { ok = false, rejected = true, error = "User rejected paid provider" };

        if (enablePaidPolicy == true
            && !_project.Workflow.PaidProvidersEnabled
            && !ConfirmAgentEdit("Enable paid automation providers for this project? Every paid action will still require budget checks and approval."))
            return new { ok = false, rejected = true, error = "User rejected paid-provider policy" };

        using var mutation = _saveCoordinator.BeginMutation();
        var provider = existing ?? new AutomationProviderManifest { Id = providerId };
        provider.Name = AgentPayloadReader.ReadString(payload, "name") ?? provider.Name;
        provider.Local = local;
        provider.Paid = paid;
        provider.Enabled = enabled;
        provider.Endpoint = endpoint;
        provider.Notes = AgentPayloadReader.ReadString(payload, "notes") ?? provider.Notes;
        if (enabled && paid) provider.ApprovedUtc = DateTimeOffset.UtcNow;
        if (AgentPayloadReader.TryGetProperty(payload, "capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
        {
            provider.Capabilities.Clear();
            provider.Capabilities.AddRange(capabilities.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        if (AgentPayloadReader.TryGetProperty(payload, "estimated_unit_costs_usd", out var costs) && costs.ValueKind == JsonValueKind.Object)
        {
            provider.EstimatedUnitCostsUsd.Clear();
            foreach (var property in costs.EnumerateObject())
            {
                if (decimal.TryParse(property.Value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cost)
                    && cost >= 0)
                    provider.EstimatedUnitCostsUsd[property.Name] = cost;
            }
        }
        if (existing == null) _project.AutomationProviders.Add(provider);
        if (enablePaidPolicy.HasValue) _project.Workflow.PaidProvidersEnabled = enablePaidPolicy.Value;
        if (AgentPayloadReader.ReadString(payload, "budget_mode") is { Length: > 0 } budgetModeText
            && Enum.TryParse<ProductionBudgetMode>(budgetModeText, true, out var budgetMode))
            _project.Workflow.BudgetMode = budgetMode;
        if (AgentPayloadReader.HasProperty(payload, "budget_limit_usd"))
            _project.Workflow.BudgetLimitUsd = (decimal)Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "budget_limit_usd", 0));
        _project.IncrementRevision();
        MarkProjectDirty("Automation provider policy updated");
        AddAgentAudit("upsert_provider", $"Provider {provider.Id}: enabled={provider.Enabled}, paid={provider.Paid}", true, null);
        return new { ok = true, revision = _project.Revision, provider, policy = BuildAgentProviderState() };
    }

    private object EstimateAgentProviderCost(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var providerId = AgentPayloadReader.ReadRequiredString(payload, "provider_id");
        var provider = _project.AutomationProviders.FirstOrDefault(candidate => candidate.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (provider == null || !provider.Enabled) return new { ok = false, error = "Provider is not registered and enabled" };
        var operation = AgentPayloadReader.ReadRequiredString(payload, "operation");
        var estimate = (decimal)Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "estimated_usd", 0));
        if (!provider.Paid && estimate > 0) return new { ok = false, error = "A free/local provider cannot reserve a non-zero paid cost" };
        if (provider.Paid && !_project.Workflow.PaidProvidersEnabled) return new { ok = false, error = "Paid providers are disabled" };

        var reserved = _project.Workflow.CostEvents.Where(value => value.Status == ProductionCostStatus.Reserved).Sum(value => value.ReservedUsd);
        var projected = _project.Workflow.ActualSpendUsd + reserved + estimate;
        var overBudget = _project.Workflow.BudgetLimitUsd.HasValue && projected > _project.Workflow.BudgetLimitUsd.Value;
        if (overBudget && _project.Workflow.BudgetMode == ProductionBudgetMode.Cap)
            return new { ok = false, budgetExceeded = true, error = $"Projected spend ${projected:0.00} exceeds the ${_project.Workflow.BudgetLimitUsd:0.00} project cap" };

        var localAlternative = _project.Workflow.LocalFirst && !provider.Local
            ? _project.AutomationProviders.FirstOrDefault(candidate => candidate.Enabled && candidate.Local && candidate.Capabilities.Intersect(provider.Capabilities, StringComparer.OrdinalIgnoreCase).Any())
            : null;
        var requiresApproval = provider.Paid && _project.Workflow.RequireApprovalForPaidOperations
                               || estimate > _project.Workflow.SingleActionApprovalThresholdUsd
                               || localAlternative != null;
        if (requiresApproval)
        {
            var alternative = localAlternative == null ? string.Empty : $"\nLocal alternative available: {localAlternative.Name}.";
            if (!ConfirmAgentEdit($"Reserve ${estimate:0.00} for {provider.Name}: {operation}?{alternative}"))
                return new { ok = false, rejected = true, error = "User rejected cost reservation" };
        }

        var costEvent = new ProductionCostEvent
        {
            ProviderId = provider.Id,
            Operation = operation,
            EstimatedUsd = estimate,
            ReservedUsd = estimate,
            Status = ProductionCostStatus.Reserved,
            UserApproved = requiresApproval,
        };
        using var mutation = _saveCoordinator.BeginMutation();
        _project.Workflow.CostEvents.Add(costEvent);
        RecalculateWorkflowCosts();
        _project.IncrementRevision();
        MarkProjectDirty("Automation cost reserved");
        AddAgentAudit("estimate_cost", $"{provider.Name}/{operation}: ${estimate:0.00}", true, overBudget ? "Budget warning" : null);
        return new { ok = true, revision = _project.Revision, costEvent, budgetWarning = overBudget, localAlternative = localAlternative?.Name };
    }

    private object ReconcileAgentProviderCost(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var eventId = AgentPayloadReader.ReadRequiredString(payload, "cost_event_id");
        var costEvent = _project.Workflow.CostEvents.FirstOrDefault(candidate => candidate.Id == eventId);
        if (costEvent == null) return new { ok = false, error = "Cost event not found" };
        if (costEvent.Status is ProductionCostStatus.Completed or ProductionCostStatus.Failed or ProductionCostStatus.Refunded)
            return new { ok = false, error = "Cost event is already finalized" };
        var actual = (decimal)Math.Max(0, AgentPayloadReader.ReadSeconds(payload, "actual_usd", 0));
        var success = AgentPayloadReader.ReadBool(payload, "success", true);
        using var mutation = _saveCoordinator.BeginMutation();
        costEvent.ActualUsd = actual;
        costEvent.ReservedUsd = 0;
        costEvent.Status = success ? ProductionCostStatus.Completed : ProductionCostStatus.Failed;
        costEvent.Error = AgentPayloadReader.ReadString(payload, "error");
        costEvent.CompletedUtc = DateTimeOffset.UtcNow;
        RecalculateWorkflowCosts();
        _project.IncrementRevision();
        MarkProjectDirty("Automation cost reconciled");
        AddAgentAudit("reconcile_cost", $"{costEvent.ProviderId}/{costEvent.Operation}: ${actual:0.00}", success, costEvent.Error);
        return new { ok = true, revision = _project.Revision, costEvent, policy = BuildAgentProviderState() };
    }

    private void RecalculateWorkflowCosts()
    {
        _project.Workflow.EstimatedSpendUsd = _project.Workflow.CostEvents
            .Where(value => value.Status == ProductionCostStatus.Reserved)
            .Sum(value => value.ReservedUsd);
        _project.Workflow.ActualSpendUsd = _project.Workflow.CostEvents
            .Where(value => value.Status is ProductionCostStatus.Completed or ProductionCostStatus.Failed)
            .Sum(value => value.ActualUsd);
    }

    private static string? ValidateProviderEndpoint(bool local, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return "Provider endpoint is not a valid absolute URI";
        if (local)
        {
            if (uri.IsFile) return null;
            if (uri.Scheme is "http" or "https" && System.Net.IPAddress.TryParse(uri.Host, out var address) && System.Net.IPAddress.IsLoopback(address)) return null;
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;
            return "Local provider endpoints must be file URIs or loopback HTTP endpoints";
        }
        return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Remote provider endpoints must use HTTPS";
    }

    private object BuildAgentVariantState() => new
    {
        ok = true,
        variants = _project.ExportVariants,
    };

    private object BuildAgentCompositionState() => new
    {
        ok = true,
        rule = "External composition engines may only render local generated assets. Rushframe remains the timeline and project source of truth.",
        compositions = _project.ExternalCompositions,
    };

    private object SetAgentWorkflowStage(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        _project.Workflow.EnsureDefaults();
        var stageId = AgentPayloadReader.ReadRequiredString(payload, "stage_id");
        var stage = _project.Workflow.Stages.FirstOrDefault(candidate => candidate.Id.Equals(stageId, StringComparison.OrdinalIgnoreCase));
        if (stage == null) return new { ok = false, error = $"Workflow stage '{stageId}' was not found" };
        var statusText = AgentPayloadReader.ReadString(payload, "status") ?? stage.Status.ToString();
        if (!Enum.TryParse<ProductionStageStatus>(statusText, true, out var status))
            return new { ok = false, error = $"Unknown workflow status '{statusText}'" };
        var requiresHumanDecision = stage.RequiresApproval && status is ProductionStageStatus.Approved or ProductionStageStatus.Completed;
        if (requiresHumanDecision
            && !ConfirmAgentEdit($"Approve workflow stage '{stage.Name}' as {status}?"))
            return new { ok = false, rejected = true, error = "User rejected workflow stage change" };

        using var mutation = _saveCoordinator.BeginMutation();
        stage.Status = status;
        stage.Summary = AgentPayloadReader.ReadString(payload, "summary") ?? stage.Summary;
        if (status == ProductionStageStatus.Running) stage.StartedUtc ??= DateTimeOffset.UtcNow;
        if (status == ProductionStageStatus.Approved)
        {
            stage.ApprovedUtc = DateTimeOffset.UtcNow;
            stage.ApprovedBy = "local-user";
        }
        if (status is ProductionStageStatus.Completed or ProductionStageStatus.Failed or ProductionStageStatus.Skipped)
            stage.CompletedUtc = DateTimeOffset.UtcNow;
        ReplaceStringsFromPayload(payload, "inputs", stage.Inputs);
        ReplaceStringsFromPayload(payload, "outputs", stage.Outputs);
        ReplaceStringsFromPayload(payload, "warnings", stage.Warnings);
        ReplaceStringsFromPayload(payload, "artifact_paths", stage.ArtifactPaths);
        stage.Revision++;
        _project.Workflow.ActiveStageId = stage.Id;
        ReadyNextWorkflowStage(stage);
        _project.IncrementRevision();
        MarkProjectDirty("Production workflow updated");
        AddAgentAudit("set_workflow_stage", $"{stage.Name}: {status}", true, null);
        return new { ok = true, revision = _project.Revision, stage, workflow = BuildAgentWorkflowState() };
    }

    private object RecordAgentProductionDecision(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var category = AgentPayloadReader.ReadRequiredString(payload, "category");
        var question = AgentPayloadReader.ReadRequiredString(payload, "question");
        var selected = AgentPayloadReader.ReadRequiredString(payload, "selected_option");
        var options = ReadStringArray(payload, "options_considered");
        if (options.Count < 2) return new { ok = false, error = "A production decision must record at least two considered options" };
        if (!options.Contains(selected, StringComparer.OrdinalIgnoreCase))
            return new { ok = false, error = "selected_option must be present in options_considered" };
        var userVisible = AgentPayloadReader.ReadBool(payload, "user_visible", true);
        const bool requireApproval = true;
        if (!ConfirmAgentEdit($"Production decision:\n{question}\n\nChoose: {selected}"))
            return new { ok = false, rejected = true, error = "User rejected production decision" };

        var decision = new ProductionDecision
        {
            Category = category,
            Question = question,
            SelectedOption = selected,
            Reason = AgentPayloadReader.ReadString(payload, "reason") ?? string.Empty,
            Confidence = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "confidence", 0.5), 0, 1),
            UserVisible = userVisible,
            Status = requireApproval ? ProductionDecisionStatus.Approved : ProductionDecisionStatus.Proposed,
            ResolvedUtc = requireApproval ? DateTimeOffset.UtcNow : null,
        };
        decision.OptionsConsidered.AddRange(options);
        using var mutation = _saveCoordinator.BeginMutation();
        _project.Workflow.Decisions.Add(decision);
        _project.IncrementRevision();
        MarkProjectDirty("Production decision recorded");
        AddAgentAudit("record_decision", $"{category}: {selected}", true, null);
        return new { ok = true, revision = _project.Revision, decision };
    }

    private object UpsertAgentVariant(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var id = AgentPayloadReader.ReadString(payload, "variant_id");
        var existing = !string.IsNullOrWhiteSpace(id)
            ? _project.ExportVariants.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        if (existing == null && _project.ExportVariants.Count >= 20)
            return new { ok = false, error = "A project may contain at most 20 export variants" };
        var name = AgentPayloadReader.ReadString(payload, "name") ?? existing?.Name ?? "Variant";
        var width = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "width", existing?.Width ?? 1080), 2, 16384));
        var height = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "height", existing?.Height ?? 1920), 2, 16384));
        var summary = $"Configure export variant '{name}' at {width}×{height}";
        if (!ConfirmAgentEdit(summary))
            return new { ok = false, rejected = true, error = "User rejected export variant change" };

        ExportVariant variant;
        using (var mutation = _saveCoordinator.BeginMutation())
        {
            if (existing == null)
            {
                variant = new ExportVariant { Name = name, Width = width, Height = height };
                _project.ExportVariants.Add(variant);
            }
            else
            {
                variant = existing;
                variant.Name = name;
                variant.Width = width;
                variant.Height = height;
            }
            if (AgentPayloadReader.ReadString(payload, "sequence_id") is { Length: > 0 } sequenceText
                && Guid.TryParse(sequenceText, out var sequenceGuid))
                variant.SequenceId = new SequenceId(sequenceGuid);
            else
                variant.SequenceId ??= _project.MainSequence?.Id;
            if (AgentPayloadReader.HasProperty(payload, "fps"))
                variant.FrameRate = FrameRate.FromDouble(Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fps", 30), 1, 240));
            variant.Format = AgentPayloadReader.ReadString(payload, "format") ?? variant.Format;
            variant.Quality = AgentPayloadReader.ReadString(payload, "quality") ?? variant.Quality;
            variant.MaximumDurationSeconds = AgentPayloadReader.ReadNullableDouble(payload, "maximum_duration") ?? variant.MaximumDurationSeconds;
            variant.SafeAreaTopPercent = ClampPercent(payload, "safe_top", variant.SafeAreaTopPercent);
            variant.SafeAreaRightPercent = ClampPercent(payload, "safe_right", variant.SafeAreaRightPercent);
            variant.SafeAreaBottomPercent = ClampPercent(payload, "safe_bottom", variant.SafeAreaBottomPercent);
            variant.SafeAreaLeftPercent = ClampPercent(payload, "safe_left", variant.SafeAreaLeftPercent);
            variant.ShareTimelineEdits = AgentPayloadReader.ReadBool(payload, "share_timeline_edits", variant.ShareTimelineEdits);
            if (AgentPayloadReader.TryGetProperty(payload, "overrides", out var overrides) && overrides.ValueKind == JsonValueKind.Object)
            {
                variant.Overrides.Clear();
                foreach (var property in overrides.EnumerateObject()) variant.Overrides[property.Name] = property.Value.ToString();
            }
            if (AgentPayloadReader.TryGetProperty(payload, "track_overrides", out var trackOverrides) && trackOverrides.ValueKind == JsonValueKind.Array)
            {
                variant.TrackOverrides.Clear();
                variant.TrackOverrides.AddRange(
                    JsonSerializer.Deserialize<List<VariantTrackOverride>>(trackOverrides.GetRawText(), AgentPayloadReader.JsonOptions) ?? []);
            }
            if (AgentPayloadReader.TryGetProperty(payload, "item_overrides", out var itemOverrides) && itemOverrides.ValueKind == JsonValueKind.Array)
            {
                variant.ItemOverrides.Clear();
                variant.ItemOverrides.AddRange(
                    JsonSerializer.Deserialize<List<VariantItemOverride>>(itemOverrides.GetRawText(), AgentPayloadReader.JsonOptions) ?? []);
            }
            variant.Status = ExportVariantStatus.Ready;
            _project.IncrementRevision();
        }
        MarkProjectDirty("Export variant updated");
        AddAgentAudit("upsert_variant", summary, true, null);
        return new { ok = true, revision = _project.Revision, variant };
    }

    private async Task<object> RenderAgentVariantAsync(
        JsonElement payload,
        CancellationToken cancellationToken,
        bool approvalAlreadyGranted = false)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var variantId = AgentPayloadReader.ReadRequiredString(payload, "variant_id");
        var variant = _project.ExportVariants.FirstOrDefault(candidate => candidate.Id == variantId);
        if (variant == null) return new { ok = false, error = "Export variant not found" };
        var sequence = variant.SequenceId is { } sequenceId
            ? _project.Sequences.FirstOrDefault(candidate => candidate.Id == sequenceId)
            : _project.MainSequence;
        if (sequence == null) return new { ok = false, error = "Variant sequence not found" };
        var variantLicenseIssues = SoundLicenseGuard.FindIssues(_project, sequence);
        if (variantLicenseIssues.Count > 0)
            return new { ok = false, error = SoundLicenseGuard.FormatBlockingMessage(variantLicenseIssues), soundLicenseIssues = variantLicenseIssues };
        if (variant.MaximumDurationSeconds.HasValue && sequence.Duration.Seconds > variant.MaximumDurationSeconds.Value + 0.001)
            return new { ok = false, error = $"Timeline exceeds variant maximum duration of {variant.MaximumDurationSeconds:0.##}s" };
        var outputPath = ValidateAgentOutputPath(AgentPayloadReader.ReadRequiredString(payload, "output_path"));
        var summary = $"Render variant '{variant.Name}' to {outputPath}";
        if (!approvalAlreadyGranted && !ConfirmAgentEdit(summary))
            return new { ok = false, rejected = true, error = "User rejected variant render" };

        var projectContext = CaptureProjectOperationContext();
        var (renderProject, renderSequence) = CreateVariantRenderContext(variant);
        var format = Enum.TryParse<TimelineExportFormat>(variant.Format, true, out var parsedFormat) ? parsedFormat : TimelineExportFormat.Mp4;
        var quality = Enum.TryParse<TimelineExportQuality>(variant.Quality, true, out var parsedQuality) ? parsedQuality : TimelineExportQuality.High;
        var options = new TimelineExportOptions(format, quality, IncludeAudio: true, HardwareEncoding: false);
        var renderJob = StartAgentRenderJob(
            payload,
            RenderJobKind.Variant,
            outputPath,
            variant.Width,
            variant.Height,
            format.ToString(),
            quality.ToString(),
            options.IncludeAudio,
            options.HardwareEncoding,
            variantId: variant.Id);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await _mediaService.ExportTimelineAsync(
                renderProject,
                renderSequence,
                outputPath,
                cancellationToken: cancellationToken,
                outputWidth: variant.Width,
                outputHeight: variant.Height,
                exportOptions: options);
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; render state was not committed." };
            SetAgentRenderJobVerifying(renderJob);
            var receipt = await _renderReceiptService.CreateAsync(
                renderProject,
                renderSequence,
                outputPath,
                variant.Width,
                variant.Height,
                options,
                approvalSource: "agent-variant-render",
                variantId: variant.Id,
                cancellationToken);
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; render receipt was not committed." };
            CompleteAgentRenderJob(renderJob, receipt);
            AddAgentAudit("render_variant", summary, receipt.Status != RenderVerificationStatus.Failed, receipt.Status == RenderVerificationStatus.Failed ? "Verification failed" : null);
            return new { ok = receipt.Status != RenderVerificationStatus.Failed, revision = _project.Revision, outputPath, renderJob, receipt };
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                FailAgentRenderJob(renderJob, "Variant render was canceled", canceled: true);
                AddAgentAudit("render_variant", summary, false, "Variant render was canceled");
            }
            throw;
        }
        catch (Exception ex)
        {
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; failed render state was not committed." };
            FailAgentRenderJob(renderJob, ex.Message);
            AddAgentAudit("render_variant", summary, false, ex.Message);
            return new { ok = false, revision = _project.Revision, renderJob, error = ex.Message };
        }
    }

    private object RegisterAgentComposition(JsonElement payload)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var spec = ParseCompositionSpec(payload);
        var validation = _externalCompositionService.Validate(spec, _currentProjectPath);
        if (!validation.Success) return new { ok = false, errors = validation.Errors, warnings = validation.Warnings };
        var summary = $"Register local {spec.Kind} composition '{spec.Name}'";
        if (!ConfirmAgentEdit(summary))
            return new { ok = false, rejected = true, error = "User rejected composition registration" };
        using (var mutation = _saveCoordinator.BeginMutation())
        {
            var existing = _project.ExternalCompositions.FirstOrDefault(candidate => candidate.Id == spec.Id);
            if (existing != null) _project.ExternalCompositions.Remove(existing);
            spec.Status = ExternalCompositionStatus.Validated;
            _project.ExternalCompositions.Add(spec);
            _project.IncrementRevision();
        }
        MarkProjectDirty("External composition registered");
        AddAgentAudit("register_composition", summary, true, null);
        return new { ok = true, revision = _project.Revision, composition = spec, warnings = validation.Warnings };
    }

    private async Task<object> RenderAgentCompositionAsync(
        JsonElement payload,
        CancellationToken cancellationToken,
        bool approvalAlreadyGranted = false)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var id = AgentPayloadReader.ReadRequiredString(payload, "composition_id");
        var spec = _project.ExternalCompositions.FirstOrDefault(candidate => candidate.Id == id);
        if (spec == null) return new { ok = false, error = "External composition not found" };
        var summary = $"Render local {spec.Kind} composition '{spec.Name}'";
        if (!approvalAlreadyGranted && !ConfirmAgentEdit(summary))
            return new { ok = false, rejected = true, error = "User rejected composition render" };

        var projectContext = CaptureProjectOperationContext();
        var compositionOutputPath = string.IsNullOrWhiteSpace(spec.OutputPath)
            ? Path.Combine(spec.ProjectDirectory, "renders", $"{spec.Name}.{(spec.TransparentBackground ? "webm" : "mp4")}")
            : spec.OutputPath;
        var renderJob = StartAgentRenderJob(
            payload,
            RenderJobKind.ExternalComposition,
            compositionOutputPath,
            spec.Width,
            spec.Height,
            spec.TransparentBackground ? "webm" : Path.GetExtension(compositionOutputPath).TrimStart('.'),
            "generated",
            includeAudio: true,
            hardwareEncoding: false,
            compositionId: spec.Id);

        ExternalCompositionRenderResult result;
        try
        {
            result = await _externalCompositionService.RenderAsync(spec, _currentProjectPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                FailAgentRenderJob(renderJob, "Composition render was canceled", canceled: true);
                AddAgentAudit("render_composition", summary, false, "Composition render was canceled");
            }
            throw;
        }
        catch (Exception ex)
        {
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; failed composition state was not committed." };
            FailAgentRenderJob(renderJob, ex.Message);
            AddAgentAudit("render_composition", summary, false, ex.Message);
            return new { ok = false, revision = _project.Revision, renderJob, error = ex.Message };
        }
        if (!IsCurrentProjectOperation(projectContext))
            return new { ok = false, conflict = true, error = "The originating project is no longer open; composition state was not committed." };
        if (!result.Success || result.OutputPath == null)
        {
            var error = string.Join(" ", result.Errors);
            FailAgentRenderJob(renderJob, string.IsNullOrWhiteSpace(error) ? "Composition render failed" : error);
            AddAgentAudit("render_composition", summary, false, error);
            return new { ok = false, revision = _project.Revision, renderJob, errors = result.Errors, warnings = result.Warnings, verification = result.Verification };
        }

        var fullOutput = Path.GetFullPath(result.OutputPath);
        var imported = spec.ImportAfterRender
            ? _project.MediaLibrary.FirstOrDefault(asset =>
                string.Equals(Path.GetFullPath(asset.OriginalPath), fullOutput, StringComparison.OrdinalIgnoreCase))
            : null;
        var generatedAsset = spec.ImportAfterRender && imported == null
            ? await CreateGeneratedCompositionAssetAsync(spec, fullOutput, cancellationToken)
            : null;
        if (!IsCurrentProjectOperation(projectContext))
            return new { ok = false, conflict = true, error = "The originating project is no longer open; generated media was not imported." };
        CompleteAgentRenderJob(renderJob, applyAdditionalState: () =>
        {
            spec.OutputPath = fullOutput;
            spec.LastOutputSha256 = result.OutputSha256;
            spec.LastRenderedUtc = DateTimeOffset.UtcNow;
            spec.Status = result.Verification?.Status == MediaExportVerificationStatus.Failed
                ? ExternalCompositionStatus.Failed
                : ExternalCompositionStatus.Rendered;
            spec.LastError = spec.Status == ExternalCompositionStatus.Failed
                ? string.Join(" ", result.Verification?.Errors ?? result.Errors)
                : null;
            if (generatedAsset != null)
            {
                _project.MediaLibrary.Add(generatedAsset);
                imported = generatedAsset;
            }
        });
        if (generatedAsset != null) RefreshMediaList();
        MarkProjectDirty("External composition rendered and imported");
        AddAgentAudit("render_composition", summary, true, null);
        return new
        {
            ok = true,
            revision = _project.Revision,
            outputPath = result.OutputPath,
            outputSha256 = result.OutputSha256,
            renderJob,
            verification = result.Verification,
            importedMediaAssetId = imported?.Id.ToString(),
            warnings = result.Warnings,
        };
    }

    private ExternalCompositionSpec ParseCompositionSpec(JsonElement payload)
    {
        var kindText = AgentPayloadReader.ReadString(payload, "kind") ?? nameof(ExternalCompositionKind.Remotion);
        if (!Enum.TryParse<ExternalCompositionKind>(kindText, true, out var kind))
            throw new InvalidOperationException($"Unknown composition kind '{kindText}'");
        var fps = Math.Clamp(AgentPayloadReader.ReadSeconds(payload, "fps", 30), 1, 240);
        var spec = new ExternalCompositionSpec
        {
            Id = AgentPayloadReader.ReadString(payload, "composition_id") ?? Guid.NewGuid().ToString("N"),
            Name = AgentPayloadReader.ReadString(payload, "name") ?? "Generated composition",
            Kind = kind,
            ProjectDirectory = AgentPayloadReader.ReadRequiredString(payload, "project_directory"),
            EntryPoint = AgentPayloadReader.ReadString(payload, "entry_point"),
            CompositionId = AgentPayloadReader.ReadString(payload, "remotion_composition_id"),
            OutputPath = AgentPayloadReader.ReadString(payload, "output_path") ?? string.Empty,
            Width = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "width", 1920), 2, 16384)),
            Height = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "height", 1080), 2, 16384)),
            FrameRate = FrameRate.FromDouble(fps),
            DurationSeconds = Math.Max(0.1, AgentPayloadReader.ReadSeconds(payload, "duration", 5)),
            TransparentBackground = AgentPayloadReader.ReadBool(payload, "transparent", false),
            ImportAfterRender = AgentPayloadReader.ReadBool(payload, "import_after_render", true),
        };
        if (AgentPayloadReader.TryGetProperty(payload, "parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
            foreach (var property in parameters.EnumerateObject()) spec.Parameters[property.Name] = property.Value.ToString();
        return spec;
    }

    private bool TryValidateAgentRevision(JsonElement payload, out object? conflict)
    {
        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (baseRevision == _project.Revision)
        {
            conflict = null;
            return true;
        }
        conflict = new
        {
            ok = false,
            conflict = true,
            error = "Project revision conflict. Refresh state before mutating the project.",
            expectedRevision = _project.Revision,
            receivedRevision = baseRevision,
        };
        return false;
    }

    private string ValidateAgentOutputPath(string requestedPath)
    {
        var allowedDirectory = !string.IsNullOrWhiteSpace(_currentProjectPath)
            ? Path.GetDirectoryName(Path.GetFullPath(_currentProjectPath))!
            : Path.Combine(_appData, "agent-exports");
        return LocalOutputPathGuard.Resolve(allowedDirectory, requestedPath);
    }

    private void ReadyNextWorkflowStage(ProductionWorkflowStage current)
    {
        if (current.Status is not (ProductionStageStatus.Completed or ProductionStageStatus.Approved)) return;
        var index = _project.Workflow.Stages.IndexOf(current);
        if (index < 0 || index + 1 >= _project.Workflow.Stages.Count) return;
        var next = _project.Workflow.Stages[index + 1];
        if (next.Status == ProductionStageStatus.Pending) next.Status = ProductionStageStatus.Ready;
    }

    private static List<string> ReadStringArray(JsonElement payload, string name)
    {
        if (!AgentPayloadReader.TryGetProperty(payload, name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
    }

    private static void ReplaceStringsFromPayload(JsonElement payload, string name, List<string> target)
    {
        if (!AgentPayloadReader.HasProperty(payload, name)) return;
        target.Clear();
        target.AddRange(ReadStringArray(payload, name));
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;

    private static double ClampPercent(JsonElement payload, string name, double fallback) =>
        Math.Clamp(AgentPayloadReader.ReadSeconds(payload, name, fallback), 0, 50);

    private RenderJobRecord StartAgentRenderJob(
        JsonElement payload,
        RenderJobKind kind,
        string outputPath,
        int width,
        int height,
        string format,
        string quality,
        bool includeAudio,
        bool hardwareEncoding,
        string? variantId = null,
        string? compositionId = null)
    {
        var retryJobId = AgentPayloadReader.ReadString(payload, "retry_job_id");
        var job = !string.IsNullOrWhiteSpace(retryJobId)
            ? _project.RenderJobs.FirstOrDefault(candidate => candidate.JobId == retryJobId)
            : null;
        using var mutation = _saveCoordinator.BeginMutation();
        if (job == null)
        {
            job = new RenderJobRecord
            {
                Kind = kind,
                OutputPath = outputPath,
                VariantId = variantId,
                CompositionId = compositionId,
                SourceRevision = _project.Revision,
                Width = width,
                Height = height,
                Format = format,
                Quality = quality,
                IncludeAudio = includeAudio,
                HardwareEncoding = hardwareEncoding,
            };
            _project.RenderJobs.Add(job);
        }
        else
        {
            job.Kind = kind;
            job.OutputPath = outputPath;
            job.VariantId = variantId;
            job.CompositionId = compositionId;
            job.SourceRevision = _project.Revision;
            job.Width = width;
            job.Height = height;
            job.Format = format;
            job.Quality = quality;
            job.IncludeAudio = includeAudio;
            job.HardwareEncoding = hardwareEncoding;
        }
        job.Status = RenderJobStatus.Rendering;
        if (!string.IsNullOrWhiteSpace(variantId))
        {
            var liveVariant = _project.ExportVariants.FirstOrDefault(candidate => candidate.Id == variantId);
            if (liveVariant != null) liveVariant.Status = ExportVariantStatus.Rendering;
        }
        job.AttemptCount++;
        job.StartedUtc = DateTimeOffset.UtcNow;
        job.CompletedUtc = null;
        job.ReceiptId = null;
        job.LastError = null;
        _project.IncrementRevision();
        MarkProjectDirty("Render job started");
        return job;
    }

    private void SetAgentRenderJobVerifying(RenderJobRecord job)
    {
        using var mutation = _saveCoordinator.BeginMutation();
        job.Status = RenderJobStatus.Verifying;
        _project.IncrementRevision();
        MarkProjectDirty("Render job verifying");
    }

    private void CompleteAgentRenderJob(
        RenderJobRecord job,
        RenderReceiptDocument? receipt = null,
        Action? applyAdditionalState = null)
    {
        using var mutation = _saveCoordinator.BeginMutation();
        if (receipt != null) RenderReceiptService.ApplyToProject(_project, receipt);
        applyAdditionalState?.Invoke();
        job.Status = receipt?.Status == RenderVerificationStatus.Failed
            ? RenderJobStatus.Failed
            : RenderJobStatus.Completed;
        job.ReceiptId = receipt?.ReceiptId;
        job.LastError = receipt?.Status == RenderVerificationStatus.Failed
            ? "Render verification failed. Review the linked receipt."
            : null;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        _project.IncrementRevision();
        MarkProjectDirty("Render job completed");
    }

    private void FailAgentRenderJob(RenderJobRecord job, string error, bool canceled = false)
    {
        using var mutation = _saveCoordinator.BeginMutation();
        if (!string.IsNullOrWhiteSpace(job.VariantId))
        {
            var variant = _project.ExportVariants.FirstOrDefault(candidate => candidate.Id == job.VariantId);
            if (variant != null) variant.Status = ExportVariantStatus.Failed;
        }
        if (!string.IsNullOrWhiteSpace(job.CompositionId))
        {
            var composition = _project.ExternalCompositions.FirstOrDefault(candidate => candidate.Id == job.CompositionId);
            if (composition != null)
            {
                composition.Status = ExternalCompositionStatus.Failed;
                composition.LastError = error;
            }
        }
        job.Status = canceled ? RenderJobStatus.Canceled : RenderJobStatus.Failed;
        job.LastError = error;
        job.CompletedUtc = DateTimeOffset.UtcNow;
        _project.IncrementRevision();
        MarkProjectDirty("Render job failed");
    }

    private async Task<object> RetryAgentRenderJobAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (!TryValidateAgentRevision(payload, out var conflict)) return conflict!;
        var jobId = AgentPayloadReader.ReadRequiredString(payload, "job_id");
        var job = _project.RenderJobs.FirstOrDefault(candidate => candidate.JobId == jobId);
        if (job == null) return new { ok = false, error = "Render job not found" };
        if (job.Status is not (RenderJobStatus.Failed or RenderJobStatus.Canceled))
            return new { ok = false, error = $"Only failed or canceled jobs can be retried; current status is {job.Status}" };

        var revisionNote = job.SourceRevision == _project.Revision
            ? string.Empty
            : $"\n\nThe project changed from revision {job.SourceRevision} to {_project.Revision}; the retry will use the current project state.";
        if (!ConfirmAgentEdit($"Retry {job.Kind} render to {job.OutputPath}?{revisionNote}"))
            return new { ok = false, rejected = true, error = "User rejected render retry" };

        var values = new Dictionary<string, object?>
        {
            ["base_revision"] = _project.Revision,
            ["retry_job_id"] = job.JobId,
            ["output_path"] = job.OutputPath,
            ["width"] = job.Width,
            ["height"] = job.Height,
            ["format"] = job.Format,
            ["quality"] = job.Quality,
            ["include_audio"] = job.IncludeAudio,
            ["hardware_encoding"] = job.HardwareEncoding,
        };
        if (!string.IsNullOrWhiteSpace(job.VariantId)) values["variant_id"] = job.VariantId;
        if (!string.IsNullOrWhiteSpace(job.CompositionId)) values["composition_id"] = job.CompositionId;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values, AgentPayloadReader.JsonOptions));
        var retryPayload = document.RootElement.Clone();
        return job.Kind switch
        {
            RenderJobKind.Variant => await RenderAgentVariantAsync(retryPayload, cancellationToken, approvalAlreadyGranted: true),
            RenderJobKind.ExternalComposition => await RenderAgentCompositionAsync(retryPayload, cancellationToken, approvalAlreadyGranted: true),
            _ => await RenderAgentTimelineAsync(retryPayload, cancellationToken, approvalAlreadyGranted: true),
        };
    }

    private object PreviewAgentEditPlan(JsonElement payload)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return new { ok = false, error = "No active sequence" };
        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (baseRevision != _project.Revision)
        {
            return new
            {
                ok = false,
                conflict = true,
                error = "Timeline revision conflict. Refresh timeline state before previewing the plan.",
                expectedRevision = _project.Revision,
                receivedRevision = baseRevision,
            };
        }

        var plan = _agentEditPlanCompiler.Compile(_project, sequence, payload, _timeline?.PlayheadTime.Seconds ?? 0);
        if (!plan.Success)
            return new { ok = false, planId = plan.PlanId, error = plan.Error };
        return new
        {
            ok = true,
            preview = true,
            planId = plan.PlanId,
            summary = plan.Summary,
            baseRevision = _project.Revision,
            operationCount = plan.Operations.Count,
            operations = plan.Operations,
            affectedRanges = plan.AffectedRanges,
            creativePlan = plan.CreativePlan,
            qualityScores = plan.QualityScores,
            qualityIssues = plan.QualityIssues,
            prompt = new { id = plan.PromptId, version = plan.PromptVersion },
            warnings = plan.Warnings,
            requiresApproval = true,
        };
    }

    private async Task<object> ReviewAgentEditPlanAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var liveSequence = _project.MainSequence;
        if (liveSequence == null) return new { ok = false, error = "No active sequence" };
        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (baseRevision != _project.Revision)
        {
            return new
            {
                ok = false,
                conflict = true,
                error = "Timeline revision conflict. Refresh timeline state before reviewing the plan.",
                expectedRevision = _project.Revision,
                receivedRevision = baseRevision,
            };
        }

        var reviewProject = ProjectSerializer.CreateSnapshot(_project);
        var reviewSequence = reviewProject.Sequences.FirstOrDefault(candidate => candidate.Id == liveSequence.Id);
        if (reviewSequence == null) return new { ok = false, error = "The active sequence was not present in the review snapshot." };
        var plan = _agentEditPlanCompiler.Compile(reviewProject, reviewSequence, payload, _timeline?.PlayheadTime.Seconds ?? 0);
        if (!plan.Success || plan.Command == null)
            return new { ok = false, planId = plan.PlanId, error = plan.Error };

        var applyResult = plan.Command.Execute(reviewSequence);
        if (!applyResult.Success)
            return new { ok = false, planId = plan.PlanId, error = applyResult.ErrorMessage ?? "The plan could not be applied to the isolated review snapshot." };
        reviewProject.IncrementRevision();
        var qualityIssues = TimelineQualityAnalyzer.Analyze(reviewProject, reviewSequence);
        var correctionRequest = new
        {
            planId = plan.PlanId,
            originalBaseRevision = _project.Revision,
            instruction = "Create a corrected edit plan against the unchanged live project revision. Address every error and warning where possible without weakening the editing brief.",
            issues = qualityIssues,
            relationships = reviewProject.MediaRelationships.OrderByDescending(value => value.Score).Take(100).ToArray(),
        };

        if (!AgentPayloadReader.ReadBool(payload, "render_draft", true))
        {
            return new
            {
                ok = true,
                isolated = true,
                rendered = false,
                planId = plan.PlanId,
                liveRevision = _project.Revision,
                reviewRevision = reviewProject.Revision,
                qualityScores = plan.QualityScores,
                qualityIssues,
                correctionRequest,
            };
        }

        if (!ConfirmAgentEdit($"Render isolated rough-cut review for plan {plan.PlanId}? The active project will not be modified."))
            return new { ok = false, rejected = true, planId = plan.PlanId, error = "User rejected rough-cut review render" };

        var maxWidth = Math.Clamp(AgentPayloadReader.ReadInt(payload, "review_width", 960), 320, 1920);
        var scale = Math.Min(1d, maxWidth / (double)Math.Max(1, reviewSequence.Width));
        var width = MakeEven(Math.Max(2, (int)Math.Round(reviewSequence.Width * scale)));
        var height = MakeEven(Math.Max(2, (int)Math.Round(reviewSequence.Height * scale)));
        var reviewDirectory = Path.Combine(_appData, "agent-reviews", _project.Id.ToString());
        Directory.CreateDirectory(reviewDirectory);
        var outputPath = Path.Combine(reviewDirectory, $"{plan.PlanId}-{_project.Revision}.mp4");
        var options = new TimelineExportOptions(
            TimelineExportFormat.Mp4,
            TimelineExportQuality.Draft,
            IncludeAudio: true,
            HardwareEncoding: false);

        try
        {
            await _mediaService.ExportTimelineAsync(
                reviewProject,
                reviewSequence,
                outputPath,
                cancellationToken: cancellationToken,
                outputWidth: width,
                outputHeight: height,
                exportOptions: options);
            var receipt = await _renderReceiptService.CreateAsync(
                reviewProject,
                reviewSequence,
                outputPath,
                width,
                height,
                options,
                approvalSource: "agent-isolated-rough-cut-review",
                cancellationToken: cancellationToken);
            var passed = receipt.Status != RenderVerificationStatus.Failed;
            AddAgentAudit("review_plan", $"Isolated rough-cut review {plan.PlanId}", passed, passed ? null : "Rough-cut verification failed");
            return new
            {
                ok = passed,
                isolated = true,
                rendered = true,
                planId = plan.PlanId,
                liveRevision = _project.Revision,
                reviewRevision = reviewProject.Revision,
                outputPath,
                receipt,
                qualityScores = plan.QualityScores,
                qualityIssues,
                correctionRequest,
            };
        }
        catch (Exception ex)
        {
            AddAgentAudit("review_plan", $"Isolated rough-cut review {plan.PlanId}", false, ex.Message);
            return new { ok = false, isolated = true, planId = plan.PlanId, liveRevision = _project.Revision, error = ex.Message, qualityIssues, correctionRequest };
        }
    }

    private async Task<object> ApplyAgentEditPlanAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return new { ok = false, error = "No active sequence" };
        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (baseRevision != _project.Revision)
        {
            AddAgentAudit("apply_plan", "Rejected stale or missing edit-plan revision", false,
                $"Expected revision {_project.Revision}, received {baseRevision?.ToString() ?? "missing"}");
            return new
            {
                ok = false,
                conflict = true,
                error = "Timeline revision conflict. Refresh timeline state before applying the plan.",
                expectedRevision = _project.Revision,
                receivedRevision = baseRevision,
            };
        }

        var plan = _agentEditPlanCompiler.Compile(_project, sequence, payload, _timeline?.PlayheadTime.Seconds ?? 0);
        if (!plan.Success || plan.Command == null)
        {
            AddAgentAudit("apply_plan", plan.Summary, false, plan.Error);
            return new { ok = false, planId = plan.PlanId, error = plan.Error };
        }

        if (new AgentEditPlanPreviewDialog(this, plan).ShowDialog() != true)
        {
            AddAgentAudit("apply_plan", plan.Summary, false, "User rejected edit plan");
            return new { ok = false, rejected = true, planId = plan.PlanId, error = "User rejected edit plan" };
        }

        using var mutation = _saveCoordinator.BeginMutation();
        var applicationCommand = new AgentPlanApplicationCommand(_project, plan, baseRevision.Value);
        var result = _undoRedo.Execute(sequence, applicationCommand);
        if (!result.Success)
        {
            AddAgentAudit("apply_plan", plan.Summary, false, result.ErrorMessage ?? "Edit plan failed");
            return new { ok = false, planId = plan.PlanId, error = result.ErrorMessage ?? "Edit plan failed" };
        }

        _project.IncrementRevision();

        _timelinePreviewDirty = true;
        if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
        CommandManager.InvalidateRequerySuggested();
        MarkProjectDirty("Agent edit plan applied");
        StatusText.Text = $"Agent plan applied: {plan.Summary}";
        AddAgentAudit("apply_plan", plan.Summary, true, null);
        await Task.CompletedTask.WaitAsync(cancellationToken);
        return new
        {
            ok = true,
            applied = true,
            planId = plan.PlanId,
            summary = plan.Summary,
            revision = _project.Revision,
            operations = plan.Operations,
            affectedRanges = plan.AffectedRanges,
            creativePlan = plan.CreativePlan,
            qualityScores = plan.QualityScores,
            qualityIssues = plan.QualityIssues,
            prompt = new { id = plan.PromptId, version = plan.PromptVersion },
            warnings = plan.Warnings,
            timeline = BuildAgentTimelineState(),
        };
    }

    private object BuildAgentTimelineState()
    {
        var sequence = _project.MainSequence;
        if (sequence == null)
            return new { ok = false, error = "No active sequence" };

        return new
        {
            ok = true,
            schemaVersion = _project.SchemaVersion,
            revision = _project.Revision,
            modifiedUtc = _project.ModifiedUtc,
            projectPath = _currentProjectPath,
            campaignDescription = _project.CampaignDescription,
            editingBrief = _project.EditingBrief,
            editingStyleProfiles = EditingStyleProfile.BuiltIns,
            timelineQuality = TimelineQualityAnalyzer.Analyze(_project, sequence),
            tasks = _project.Tasks.Select(task => new
            {
                id = task.Id,
                title = task.Title,
                completed = task.IsCompleted,
            }),
            artifactHealth = ProjectArtifactHealthService.Inspect(_project),
            sequence = new
            {
                id = sequence.Id.ToString(),
                name = sequence.Name,
                duration = sequence.Duration.Seconds,
                frameRate = new { numerator = sequence.FrameRate.Numerator, denominator = sequence.FrameRate.Denominator, value = sequence.FrameRate.Value },
                background = sequence.Background,
                layoutGuides = sequence.LayoutGuides,
                tracks = sequence.Tracks.Select((track, index) => new
                {
                    index,
                    id = track.Id.ToString(),
                    kind = track.Kind.ToString(),
                    name = track.Name,
                    muted = track.Muted,
                    solo = track.Solo,
                    locked = track.Locked,
                    hidden = track.Hidden,
                    items = track.Items
                        .OrderBy(item => item.TimelineStart.Seconds)
                        .Select(item => new
                        {
                            id = item.Id.ToString(),
                            kind = item.Kind.ToString(),
                            mediaAssetId = item.MediaAssetId?.ToString(),
                            mediaName = item.MediaAssetId is { } mediaId
                                ? Path.GetFileName(_project.MediaLibrary.FirstOrDefault(asset => asset.Id == mediaId)?.OriginalPath)
                                : null,
                            start = item.TimelineStart.Seconds,
                            duration = item.Duration.Seconds,
                            end = item.TimelineEnd.Seconds,
                            sourceStart = item.SourceStart.Seconds,
                            sourceDuration = item.SourceDuration.Seconds,
                            speed = item.Speed,
                            reversed = item.Reversed,
                            locked = item.Locked,
                            opacity = item.Opacity,
                            volume = item.Volume,
                            muted = item.Muted,
                            pan = item.Pan,
                            blendMode = item.BlendMode,
                            transform = item.Transform,
                            crop = new { left = item.CropLeft, top = item.CropTop, right = item.CropRight, bottom = item.CropBottom },
                            colorCorrection = item.ColorCorrection,
                            speedCurve = item.SpeedCurve,
                            stabilization = item.Stabilization,
                            chromaKey = item.ChromaKey,
                            masks = item.Masks,
                            text = item.TextContent,
                            animations = item.AnimationChannels.Select(channel => new
                            {
                                property = channel.PropertyName,
                                defaultValue = channel.DefaultValue,
                                keyframes = channel.Keyframes.Select(keyframe => new
                                {
                                    time = keyframe.Time.Seconds,
                                    value = keyframe.Value,
                                    interpolation = keyframe.Interpolation.ToString(),
                                }),
                            }),
                            effects = item.Effects.Select(effect => new
                            {
                                id = effect.Id.ToString(),
                                type = effect.EffectTypeId,
                                enabled = effect.Enabled,
                                parameters = effect.Parameters,
                            }),
                        }),
                }),
                transitions = sequence.Transitions.Select(transition => new
                {
                    leftItemId = transition.LeftItemId.ToString(),
                    rightItemId = transition.RightItemId.ToString(),
                    kind = transition.Kind.ToString(),
                    duration = transition.Duration.Seconds,
                    alignment = transition.Alignment,
                }),
            },
            mediaAssets = _project.MediaLibrary.Select(asset => new
            {
                id = asset.Id.ToString(),
                kind = asset.Kind.ToString(),
                name = Path.GetFileName(asset.OriginalPath),
                path = asset.OriginalPath,
                duration = asset.Duration.Seconds,
                offline = asset.IsOffline,
            }),
        };
    }

    private async Task<object> ApplyAgentTimelineEditAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sequence = _project.MainSequence;
        if (sequence == null)
            return new { ok = false, error = "No active sequence" };

        var action = AgentPayloadReader.ReadString(payload, "action");
        if (string.IsNullOrWhiteSpace(action))
            return new { ok = false, error = "Missing action" };

        var previewOnly = AgentPayloadReader.ReadBool(payload, "preview_only", false);
        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (!previewOnly && baseRevision != _project.Revision)
        {
            AddAgentAudit(action, "Rejected stale or missing base revision", false,
                $"Expected revision {_project.Revision}, received {baseRevision?.ToString() ?? "missing"}");
            return new
            {
                ok = false,
                conflict = true,
                error = "Timeline revision conflict. Refresh timeline state before applying edits.",
                expectedRevision = _project.Revision,
                receivedRevision = baseRevision,
            };
        }

        var edit = _agentEditCommandFactory.Build(
            _project,
            sequence,
            payload,
            action,
            _timeline?.PlayheadTime.Seconds ?? 0);
        if (!edit.Success || edit.Command == null)
            return new { ok = false, error = edit.Error };

        if (previewOnly)
            return new { ok = true, preview = true, summary = edit.Summary, action };

        if (!ConfirmAgentEdit(edit.Summary))
        {
            AddAgentAudit(action, edit.Summary, false, "User rejected edit");
            return new { ok = false, rejected = true, error = "User rejected edit" };
        }

        using var mutation = _saveCoordinator.BeginMutation();
        var result = _undoRedo.Execute(sequence, edit.Command);
        if (!result.Success)
        {
            AddAgentAudit(action, edit.Summary, false, result.ErrorMessage ?? "Edit failed");
            return new { ok = false, error = result.ErrorMessage ?? "Edit failed" };
        }

        _project.IncrementRevision();
        _timelinePreviewDirty = true;
        if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
        CommandManager.InvalidateRequerySuggested();
        MarkProjectDirty("Agent edit applied");
        StatusText.Text = $"Agent edit applied: {edit.Summary}";
        AddAgentAudit(action, edit.Summary, true, null);
        await Task.CompletedTask.WaitAsync(cancellationToken);
        return new { ok = true, applied = true, summary = edit.Summary, revision = _project.Revision, timeline = BuildAgentTimelineState() };
    }

    private async Task<object> RenderAgentTimelineAsync(
        JsonElement payload,
        CancellationToken cancellationToken,
        bool approvalAlreadyGranted = false)
    {
        var sequence = _project.MainSequence;
        if (sequence == null)
            return new { ok = false, error = "No active sequence" };
        var soundLicenseIssues = SoundLicenseGuard.FindIssues(_project, sequence);
        if (soundLicenseIssues.Count > 0)
            return new { ok = false, error = SoundLicenseGuard.FormatBlockingMessage(soundLicenseIssues), soundLicenseIssues };

        var requestedOutputPath = AgentPayloadReader.ReadString(payload, "output_path");
        if (string.IsNullOrWhiteSpace(requestedOutputPath))
            return new { ok = false, error = "Missing output_path" };
        string outputPath;
        try
        {
            outputPath = ValidateAgentOutputPath(requestedOutputPath);
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }

        if (sequence.Duration.Seconds > _settings.MaxOutputDurationSeconds)
            return new { ok = false, error = $"Timeline exceeds the {_settings.MaxOutputDurationSeconds}-second export limit" };

        var baseRevision = AgentPayloadReader.ReadLong(payload, "base_revision");
        if (baseRevision != _project.Revision)
        {
            AddAgentAudit("render_timeline", "Rejected stale or missing render revision", false,
                $"Expected revision {_project.Revision}, received {baseRevision?.ToString() ?? "missing"}");
            return new
            {
                ok = false,
                conflict = true,
                error = "Timeline revision conflict. Refresh timeline state before rendering.",
                expectedRevision = _project.Revision,
                receivedRevision = baseRevision,
            };
        }

        var summary = $"Render timeline to {outputPath}";
        if (!approvalAlreadyGranted && !ConfirmAgentEdit(summary))
        {
            AddAgentAudit("render_timeline", summary, false, "User rejected render");
            return new { ok = false, rejected = true, error = "User rejected render" };
        }

        var width = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "width", sequence.Width), 2, 16384));
        var height = MakeEven(Math.Clamp(AgentPayloadReader.ReadInt(payload, "height", sequence.Height), 2, 16384));
        var formatText = AgentPayloadReader.ReadString(payload, "format") ?? Path.GetExtension(outputPath).TrimStart('.');
        var format = formatText.ToLowerInvariant() switch
        {
            "webm" => TimelineExportFormat.WebM,
            "mov" => TimelineExportFormat.Mov,
            "mkv" => TimelineExportFormat.Mkv,
            _ => TimelineExportFormat.Mp4,
        };
        var qualityText = AgentPayloadReader.ReadString(payload, "quality") ?? nameof(TimelineExportQuality.High);
        if (!Enum.TryParse<TimelineExportQuality>(qualityText, true, out var quality))
            return new { ok = false, error = $"Unknown export quality '{qualityText}'" };
        var options = new TimelineExportOptions(
            format,
            quality,
            IncludeAudio: AgentPayloadReader.ReadBool(payload, "include_audio", true),
            HardwareEncoding: AgentPayloadReader.ReadBool(payload, "hardware_encoding", false));
        var projectContext = CaptureProjectOperationContext();
        var renderProject = ProjectSerializer.CreateSnapshot(_project);
        var renderSequence = renderProject.Sequences.FirstOrDefault(candidate => candidate.Id == sequence.Id)
                             ?? throw new InvalidOperationException("The active sequence was not present in the render snapshot.");
        VariantRenderContextService.CenterPrimaryVideoForPortrait(
            renderSequence,
            sequence.Width,
            sequence.Height,
            width,
            height);
        var renderJob = StartAgentRenderJob(
            payload,
            RenderJobKind.Timeline,
            outputPath,
            width,
            height,
            format.ToString(),
            quality.ToString(),
            options.IncludeAudio,
            options.HardwareEncoding);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await _mediaService.ExportTimelineAsync(
                renderProject,
                renderSequence,
                outputPath,
                cancellationToken: cancellationToken,
                outputWidth: width,
                outputHeight: height,
                exportOptions: options);
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; render state was not committed." };
            SetAgentRenderJobVerifying(renderJob);
            var receipt = await _renderReceiptService.CreateAsync(
                renderProject,
                renderSequence,
                outputPath,
                width,
                height,
                options,
                approvalSource: "agent-timeline-render",
                cancellationToken: cancellationToken);
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; render receipt was not committed." };
            CompleteAgentRenderJob(renderJob, receipt);
            var passed = receipt.Status != RenderVerificationStatus.Failed;
            AddAgentAudit("render_timeline", summary, passed, passed ? null : "Render verification failed");
            StatusText.Text = passed
                ? $"Agent render verified: {Path.GetFileName(outputPath)}"
                : $"Agent render failed verification: {Path.GetFileName(outputPath)}";
            return new { ok = passed, outputPath, revision = _project.Revision, renderJob, receipt };
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentProjectOperation(projectContext))
            {
                FailAgentRenderJob(renderJob, "Render was canceled", canceled: true);
                AddAgentAudit("render_timeline", summary, false, "Render was canceled");
            }
            throw;
        }
        catch (Exception ex)
        {
            if (!IsCurrentProjectOperation(projectContext))
                return new { ok = false, conflict = true, error = "The originating project is no longer open; failed render state was not committed." };
            FailAgentRenderJob(renderJob, ex.Message);
            AddAgentAudit("render_timeline", summary, false, ex.Message);
            return new { ok = false, revision = _project.Revision, renderJob, error = ex.Message };
        }
    }

    private bool ConfirmAgentEdit(string summary) =>
        MessageBox.Show(
            this,
            summary,
            "Approve Agent Edit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void AddAgentAudit(string action, string summary, bool success, string? error)
    {
        var record = new AgentAuditRecord(
            DateTimeOffset.UtcNow,
            _project.Id,
            _project.Revision,
            action,
            summary,
            success,
            error,
            _localAgentBridge.SessionId);
        _agentAuditLog.Add(record);
        _agentAuditLogService.Append(record);
        if (_agentAuditLog.Count > 200)
            _agentAuditLog.RemoveRange(0, _agentAuditLog.Count - 200);
    }

    private void ShowKeyboardShortcutsDialog()
    {
        var configured = new Dialogs.KeyboardShortcutsDialog(this, _actionRegistry, _settings.Keybindings).Show();
        if (configured == null) return;
        _settings.Keybindings = configured;
        _settingsService.Save("editor", _settings);
        _actionRegistry.ApplyInputBindings(this, _settings.Keybindings);
        StatusText.Text = "Keyboard shortcuts updated";
    }

    private void ShowSettingsDialog()
    {
        StatusText.Text = "Opening settings…";
        var snapToggle = new CheckBox { Content = "Snap clips by default", IsChecked = SnapToggle.IsChecked ?? true };
        var rippleToggle = new CheckBox { Content = "Ripple editing by default", IsChecked = RippleToggle.IsChecked ?? false, Margin = new Thickness(0, 8, 0, 0) };
        var autosaveToggle = new CheckBox { Content = "Enable autosave", IsChecked = _settings.AutosaveEnabled, Margin = new Thickness(0, 14, 0, 0) };
        var autosaveBox = new TextBox { Text = Math.Clamp(_settings.AutosaveIntervalSeconds, 5, 3600).ToString(CultureInfo.InvariantCulture), Width = 92 };
        var previewFpsBox = new TextBox { Text = Math.Clamp(_settings.PreviewMaxFps, 15, 60).ToString(CultureInfo.InvariantCulture), Width = 92 };
        var previewWidthBox = new TextBox { Text = Math.Clamp(_settings.PreviewMaxWidth, 480, 1920).ToString(CultureInfo.InvariantCulture), Width = 92 };
        var backendToggle = new CheckBox { Content = "Start media-intelligence backend with Rushframe", IsChecked = _settings.StartIntelligenceBackend };
        var aiInputBox = new TextBox
        {
            Text = Math.Clamp(_settings.MaxAiInputSeconds, 30, 1800).ToString(CultureInfo.InvariantCulture),
            Width = 92,
        };
        var groqKeyEditor = new ApiKeyListEditor(
            (_settings.ProtectedGroqApiKeys ?? [])
                .Select(SecretProtectionService.Unprotect)
                .Where(value => !string.IsNullOrWhiteSpace(value)),
            "Add Groq key");
        var cloudflareCredentialEditor = new CloudflareCredentialListEditor(
            (_settings.ProtectedCloudflareCredentials ?? [])
                .Select(value => new CloudflareCredentialInput(
                    SecretProtectionService.Unprotect(value.ProtectedAccountId),
                    SecretProtectionService.Unprotect(value.ProtectedApiToken))));
        var zoomSlider = new Slider
        {
            Minimum = ZoomSlider.Minimum,
            Maximum = ZoomSlider.Maximum,
            Value = ZoomSlider.Value,
            Width = 220,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var zoomText = new TextBlock
        {
            Text = $"{zoomSlider.Value:0.0}x",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        zoomSlider.ValueChanged += (_, _) => zoomText.Text = $"{zoomSlider.Value:0.0}x";
        var uiScaleSlider = new Slider
        {
            Minimum = MinimumUiScale,
            Maximum = MaximumUiScale,
            TickFrequency = UiScaleStep,
            IsSnapToTickEnabled = true,
            Value = _settings.UiScale,
            Width = 220,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var uiScaleText = new TextBlock
        {
            Text = $"{Math.Round(uiScaleSlider.Value * 100)}%",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        uiScaleSlider.ValueChanged += (_, _) => uiScaleText.Text = $"{Math.Round(uiScaleSlider.Value * 100)}%";
        var dialog = CreateOwnedDialog(
            "Rushframe Settings",
            width: 500,
            height: 760,
            minimumWidth: 440,
            minimumHeight: 560,
            resizeMode: ResizeMode.NoResize);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = "Settings",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentHoverBrush"),
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var panel = new StackPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(panel, 1);
        panel.Children.Add(new TextBlock
        {
            Text = "Timeline",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(snapToggle);
        panel.Children.Add(rippleToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "Default timeline zoom",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 16, 0, 0),
        });
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        zoomRow.Children.Add(zoomSlider);
        zoomRow.Children.Add(zoomText);
        panel.Children.Add(zoomRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Interface scale",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 16, 0, 0),
        });
        var uiScaleRow = new StackPanel { Orientation = Orientation.Horizontal };
        uiScaleRow.Children.Add(uiScaleSlider);
        uiScaleRow.Children.Add(uiScaleText);
        panel.Children.Add(uiScaleRow);
        var shortcutsButton = new Button
        {
            Content = "Configure Keyboard Shortcuts",
            Style = (Style)FindResource("CommandButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0),
        };
        shortcutsButton.Click += (_, _) => ShowKeyboardShortcutsDialog();
        panel.Children.Add(shortcutsButton);
        panel.Children.Add(new TextBlock
        {
            Text = "Interface shortcuts: Ctrl++ enlarges the UI, Ctrl+- reduces it, and Ctrl+0 resets it.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Preview performance",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 20, 0, 8),
        });
        var previewFpsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        previewFpsRow.Children.Add(new TextBlock
        {
            Text = "Maximum preview FPS",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 180,
        });
        previewFpsRow.Children.Add(previewFpsBox);
        panel.Children.Add(previewFpsRow);
        var previewWidthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        previewWidthRow.Children.Add(new TextBlock
        {
            Text = "Draft preview width",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 180,
        });
        previewWidthRow.Children.Add(previewWidthBox);
        panel.Children.Add(previewWidthRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Lower values reduce CPU/GPU load. Exact preview and final export remain composition-correct.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Autosave",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 20, 0, 8),
        });
        panel.Children.Add(autosaveToggle);
        var autosaveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        autosaveRow.Children.Add(new TextBlock
        {
            Text = "Interval seconds",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 130,
        });
        autosaveRow.Children.Add(autosaveBox);
        panel.Children.Add(autosaveRow);
        panel.Children.Add(new TextBlock
        {
            Text = "AI",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 20, 0, 8),
        });
        panel.Children.Add(backendToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "The local backend starts on 127.0.0.1:7319 and stops when Rushframe closes.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        });
        var aiInputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        aiInputRow.Children.Add(new TextBlock
        {
            Text = "Maximum AI input",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 150,
        });
        aiInputRow.Children.Add(aiInputBox);
        aiInputRow.Children.Add(new TextBlock
        {
            Text = " seconds",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(aiInputRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Default: 900 seconds (15 minutes). Rushframe analyzes only the beginning of longer source files. Final output is always limited to 180 seconds (3 minutes).",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -6, 0, 12),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "GroqCloud API keys",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        panel.Children.Add(groqKeyEditor);
        panel.Children.Add(new TextBlock
        {
            Text = "Cloudflare Workers AI credentials",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 14, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Each row is one account ID and API token pair.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        panel.Children.Add(cloudflareCredentialEditor);
        panel.Children.Add(new TextBlock
        {
            Text = "Credentials are stored encrypted for your current Windows account. Rushframe keeps one credential fixed per provider during the app session, then rotates to the next credential the next session that provider is used.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        var panelScroller = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0),
        };
        Grid.SetRow(panelScroller, 1);
        root.Children.Add(panelScroller);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(buttons, 2);
        var cancelButton = new Button { Content = "Cancel", MinWidth = 86, Margin = new Thickness(0, 0, 8, 0) };
        var saveButton = new Button { Content = "Save", MinWidth = 86, Style = (Style)FindResource("PrimaryButtonStyle") };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;
        saveButton.Click += (_, _) =>
        {
            if (!int.TryParse(autosaveBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
                interval = _settings.AutosaveIntervalSeconds;
            if (!int.TryParse(aiInputBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aiInputSeconds))
                aiInputSeconds = _settings.MaxAiInputSeconds;
            if (!int.TryParse(previewFpsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var previewFps))
                previewFps = _settings.PreviewMaxFps;
            if (!int.TryParse(previewWidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var previewWidth))
                previewWidth = _settings.PreviewMaxWidth;

            IReadOnlyList<CloudflareCredentialInput> cloudflareCredentials;
            try
            {
                cloudflareCredentials = cloudflareCredentialEditor.GetValues();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(dialog, ex.Message, "Invalid Cloudflare Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings = new EditorSettings
            {
                SnapEnabled = snapToggle.IsChecked ?? true,
                RippleEnabled = rippleToggle.IsChecked ?? false,
                TimelineZoom = Math.Clamp(zoomSlider.Value, ZoomSlider.Minimum, ZoomSlider.Maximum),
                UiScale = Math.Clamp(uiScaleSlider.Value, MinimumUiScale, MaximumUiScale),
                PreviewMaxFps = Math.Clamp(previewFps, 15, 60),
                PreviewMaxWidth = Math.Clamp(previewWidth, 480, 1920),
                PreviewLookAheadSeconds = Math.Clamp(_settings.PreviewLookAheadSeconds, 0, 2),
                AutosaveEnabled = autosaveToggle.IsChecked ?? true,
                AutosaveIntervalSeconds = Math.Clamp(interval, 5, 3600),
                StartIntelligenceBackend = backendToggle.IsChecked ?? true,
                IntelligenceBackendPort = _settings.IntelligenceBackendPort,
                ProtectedGroqApiKeys = groqKeyEditor.GetValues()
                    .Select(SecretProtectionService.Protect)
                    .ToList(),
                ProtectedCloudflareCredentials = cloudflareCredentials
                    .Select(value => new ProtectedCloudflareCredential
                    {
                        ProtectedAccountId = SecretProtectionService.Protect(value.AccountId),
                        ProtectedApiToken = SecretProtectionService.Protect(value.ApiToken),
                    })
                    .ToList(),
                AiProviderRotationCursors = new Dictionary<string, int>(
                    _settings.AiProviderRotationCursors ?? [],
                    StringComparer.OrdinalIgnoreCase),
                MaxAiInputSeconds = Math.Clamp(aiInputSeconds, 30, 1800),
                MaxOutputDurationSeconds = 180,
                Keybindings = new Dictionary<string, string>(_settings.Keybindings),
            };
            _settingsService.Save("editor", _settings);
            ApplyEditorSettings();
            RestartAutosave();
            if (_settings.StartIntelligenceBackend)
                _ = StartIntelligenceBackendAsync();
            else
                _intelligenceBackend.Stop();
            StatusText.Text = "Settings saved";
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        root.Children.Add(buttons);

        dialog.Content = CreateDialogFrame(dialog, "Settings", root, new Thickness(18));
        if (dialog.ShowDialog() != true)
            StatusText.Text = "Settings unchanged";
    }

    private void AddText_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var seq = _project.MainSequence;
        if (seq == null || _timeline == null) return;

        var text = PromptForTextClip();
        if (string.IsNullOrWhiteSpace(text)) return;

        var commands = new List<IEditCommand>();
        var track = seq.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Text && !t.Locked);
        if (track == null)
        {
            track = new Track { Kind = TrackKind.Text, Name = "T1", Order = seq.Tracks.Count };
            commands.Add(new AddPreparedTrackCommand { Track = track });
        }

        commands.Add(new AddClipCommand
        {
            TrackId = track.Id,
            Item = new TimelineItem
            {
                Kind = ItemKind.Text,
                TimelineStart = _timeline.PlayheadTime,
                Duration = MediaTime.FromSeconds(5),
                SourceDuration = MediaTime.FromSeconds(5),
                TextContent = text.Trim(),
                FillColor = "white",
                FontSize = 64,
                Transform = { PositionX = 80, PositionY = 120 },
            },
        });
        Execute(new CompositeEditCommand("Add text clip", commands));
    }

    private string? PromptForTextClip()
    {
        var dialog = CreateOwnedDialog(
            "Add Text",
            width: 420,
            height: 190,
            minimumWidth: 360,
            minimumHeight: 170,
            resizeMode: ResizeMode.NoResize);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Text content",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        var input = new TextBox
        {
            Text = "Text",
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(input, 1);
        root.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 82,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        var add = new Button
        {
            Content = "Add Text",
            MinWidth = 92,
            IsDefault = true,
            Style = (Style)FindResource("PrimaryButtonStyle"),
        };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                input.Focus();
                return;
            }

            dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(add);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        dialog.Content = CreateDialogFrame(dialog, "Add Text", root, new Thickness(14));
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void AddMarker_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project.MainSequence == null || _timeline == null) return;
        var marker = new Marker
        {
            Label = $"Marker {_project.MainSequence.Markers.Count + 1}",
            Time = _timeline.PlayheadTime,
            Color = "#ffcc00",
        };
        var result = new Dialogs.MarkerEditorDialog(this, marker, isNew: true).Show();
        if (result == null) return;
        marker.Label = result.Label;
        marker.Note = result.Note;
        marker.Time = result.Time;
        marker.Duration = result.Duration;
        marker.Color = result.Color;
        Execute(new AddMarkerCommand { Marker = marker });
    }

    private void EditMarker(Marker marker)
    {
        var result = new Dialogs.MarkerEditorDialog(this, marker, isNew: false).Show();
        if (result == null) return;
        Execute(new EditMarkerCommand
        {
            MarkerId = marker.Id,
            NewLabel = result.Label,
            NewNote = result.Note,
            NewTime = result.Time,
            NewDuration = result.Duration,
            NewColor = result.Color,
        });
    }

    private async void Render_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<MediaJobProgress>(update =>
        {
            OperationProgressBar.IsIndeterminate = update.Percent <= 0;
            if (update.Percent > 0)
            {
                OperationProgressBar.Minimum = 0;
                OperationProgressBar.Maximum = 100;
                OperationProgressBar.Value = update.Percent;
            }
            StatusText.Text = string.IsNullOrWhiteSpace(update.Message)
                ? $"Rendering… {update.Percent:0}%"
                : $"{update.Message} ({update.Percent:0}%)";
        });

        SetMediaOperationState(true, "Rendering timeline…");
        OperationProgressBar.Visibility = Visibility.Visible;
        CancelOperationButton.Visibility = Visibility.Visible;
        try
        {
            await _exportController.ExportAsync(
                _project,
                sequence,
                _operationCancellation.Token,
                progress,
                AddRenderQueueMessage,
                message => StatusText.Text = message,
                receipt =>
                {
                    using var mutation = _saveCoordinator.BeginMutation();
                    RenderReceiptService.ApplyToProject(_project, receipt);
                    _project.IncrementRevision();
                    MarkProjectDirty("Render receipt and QA results added");
                    RefreshVariantsAndReceipts();
                });
        }
        catch (OperationCanceledException)
        {
            // The controller already reports cancellation to the activity log and status bar.
        }
        catch
        {
            // The controller already reports the render failure to the user.
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            OperationProgressBar.IsIndeterminate = true;
            CancelOperationButton.Visibility = Visibility.Collapsed;
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetMediaOperationState(false, "Export operation finished");
        }
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!TryResolvePendingInspectorChanges()) return;
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        using var mutation = _saveCoordinator.BeginMutation();
        var result = _undoRedo.Undo(sequence);
        if (!result.Success) return;
        _project.IncrementRevision();
        RefreshSelectionAfterEdit(sequence);
        if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
        if (_selectedTransitionSelection != null)
            RefreshTransitionInspectorAfterEdit(sequence);
        else
            UpdateInspector(_selectedInspectorItem);
        UpdatePreviewOrientationButton();
        RefreshPreviewGuidesOverlay();
        UpdatePreviewInteractionOverlay(_selectedInspectorItem);
        MarkProjectDirty("Undo applied");
        CommandManager.InvalidateRequerySuggested();
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!TryResolvePendingInspectorChanges()) return;
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        using var mutation = _saveCoordinator.BeginMutation();
        var result = _undoRedo.Redo(sequence);
        if (!result.Success) return;
        _project.IncrementRevision();
        RefreshSelectionAfterEdit(sequence);
        if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
        if (_selectedTransitionSelection != null)
            RefreshTransitionInspectorAfterEdit(sequence);
        else
            UpdateInspector(_selectedInspectorItem);
        UpdatePreviewOrientationButton();
        RefreshPreviewGuidesOverlay();
        UpdatePreviewInteractionOverlay(_selectedInspectorItem);
        MarkProjectDirty("Redo applied");
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshSelectionAfterEdit(Sequence sequence)
    {
        if (_selectedInspectorItem == null) return;
        var updated = sequence.Tracks.SelectMany(track => track.Items)
            .FirstOrDefault(item => item.Id == _selectedInspectorItem.Id);
        if (updated != null)
        {
            _selectedInspectorItem = updated;
            return;
        }

        _selectedInspectorItem = null;
        _selectedTransitionSelection = null;
        _timeline?.ClearSelection();
    }

    private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CopySelectedClip()) DeleteSelectedClip();
    }

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelectedClip();

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var seq = _project.MainSequence;
        if (seq == null || _timeline == null) return;

        if (_groupClipboard is { Count: > 0 })
        {
            var groupPasteStart = _timeline.PlayheadTime;
            var pasteTargets = new List<(GroupClipboardItem ClipboardItem, Track TargetTrack)>();
            foreach (var clipboardItem in _groupClipboard)
            {
                var groupTargetTrack = clipboardItem.TrackIndex >= 0 && clipboardItem.TrackIndex < seq.Tracks.Count
                    && !seq.Tracks[clipboardItem.TrackIndex].Locked
                    && TrackCompatibility.IsItemCompatibleWithTrack(clipboardItem.Item.Kind, seq.Tracks[clipboardItem.TrackIndex].Kind)
                        ? seq.Tracks[clipboardItem.TrackIndex]
                        : seq.Tracks.FirstOrDefault(track =>
                            !track.Locked && TrackCompatibility.IsItemCompatibleWithTrack(clipboardItem.Item.Kind, track.Kind));
                if (groupTargetTrack == null)
                {
                    StatusText.Text = "Group paste canceled: no compatible unlocked destination exists for every item";
                    return;
                }
                pasteTargets.Add((clipboardItem, groupTargetTrack));
            }

            var commands = pasteTargets.Select(target => (IEditCommand)new AddClipCommand
            {
                TrackId = target.TargetTrack.Id,
                Item = TimelineItemCloner.Clone(
                    target.ClipboardItem.Item,
                    groupPasteStart.Add(target.ClipboardItem.RelativeStart)),
            }).ToList();
            Execute(new CompositeEditCommand($"Paste {commands.Count} clips", commands));
            return;
        }

        if (_clipboard?.Clipboard == null) return;
        var preferredIndex = _timeline.SelectedTrackIndex >= 0
            ? _timeline.SelectedTrackIndex
            : _lastSelectedTrackIndex;
        var targetTrack = preferredIndex >= 0 && preferredIndex < seq.Tracks.Count
            && !seq.Tracks[preferredIndex].Locked
            && TrackCompatibility.IsItemCompatibleWithTrack(_clipboard.Clipboard.Kind, seq.Tracks[preferredIndex].Kind)
                ? seq.Tracks[preferredIndex]
                : seq.Tracks.FirstOrDefault(track =>
                    !track.Locked && TrackCompatibility.IsItemCompatibleWithTrack(_clipboard.Clipboard.Kind, track.Kind));
        if (targetTrack == null) return;

        var pasteStart = _timeline.PlayheadTime;
        var pasteEnd = pasteStart.Add(_clipboard.Clipboard.Duration);
        var overlapping = targetTrack.Items
            .Where(item => item.TimelineStart < pasteEnd && item.TimelineStart.Add(item.Duration) > pasteStart)
            .ToList();
        if (overlapping.Count > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"Pasting here will overlap {overlapping.Count} item(s) on {targetTrack.Name}. Continue?",
                "Confirm Paste Overlap",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = "Paste canceled";
                return;
            }
        }

        Execute(new PasteClipCommand
        {
            TrackId = targetTrack.Id,
            TimelineStart = pasteStart,
            CopyCommand = _clipboard,
        });
    }

    private void SplitClip_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var seq = _project.MainSequence;
        var timeline = _timeline;
        var item = timeline?.SelectedItem;
        if (seq == null || timeline == null || item == null) return;
        if (timeline.SelectedTrackIndex < 0 || timeline.SelectedTrackIndex >= seq.Tracks.Count) return;

        Execute(new Domain.Editing.SplitClipCommand
        {
            TrackId = seq.Tracks[timeline.SelectedTrackIndex].Id,
            ItemId = item.Id,
            SplitTime = timeline.PlayheadTime,
        });
    }

    private void DeleteClip_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        DeleteSelectedClip();
    }

    private void DeleteSelectedClip()
    {
        var selectedItems = _timeline?.SelectedItems ?? [];
        if (selectedItems.Count == 0) return;
        if (selectedItems.Count == 1)
        {
            Execute(new Domain.Editing.DeleteClipCommand { ItemId = selectedItems[0].Id });
            return;
        }

        Execute(new CompositeEditCommand(
            $"Delete {selectedItems.Count} clips",
            selectedItems.Select(item => (IEditCommand)new Domain.Editing.DeleteClipCommand { ItemId = item.Id })));
    }

    private void RippleDelete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var item = _timeline?.SelectedItem;
        if (item == null) return;
        Execute(new RippleDeleteClipCommand { ItemId = item.Id, Ripple = _rippleState });
    }

    private void Duplicate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var selectedItems = _timeline?.SelectedItems ?? [];
        if (selectedItems.Count == 0) return;
        Execute(new CompositeEditCommand(
            selectedItems.Count == 1 ? "Duplicate clip" : $"Duplicate {selectedItems.Count} clips",
            selectedItems.Select(item => (IEditCommand)new DuplicateClipCommand { ItemId = item.Id })));
    }

    private bool CopySelectedClip()
    {
        var selectedItems = _timeline?.SelectedItems ?? [];
        var seq = _project.MainSequence;
        if (selectedItems.Count == 0 || seq == null) return false;

        if (selectedItems.Count > 1)
        {
            var earliest = selectedItems.Min(item => item.TimelineStart.Seconds);
            _groupClipboard = selectedItems.Select(item =>
            {
                var trackIndex = seq.Tracks.FindIndex(track => track.Items.Any(candidate => candidate.Id == item.Id));
                return new GroupClipboardItem(
                    TimelineItemCloner.Clone(item, item.TimelineStart),
                    trackIndex,
                    MediaTime.FromSeconds(item.TimelineStart.Seconds - earliest));
            }).ToList();
            _clipboard = null;
            CommandManager.InvalidateRequerySuggested();
            StatusText.Text = $"Copied {selectedItems.Count} clips";
            return true;
        }

        var item = selectedItems[0];
        var copy = new CopyClipCommand { ItemId = item.Id };
        var result = copy.Execute(seq);
        if (!result.Success) return false;
        _clipboard = copy;
        _groupClipboard = null;
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private void AddRenderQueueMessage(string message)
    {
        RenderQueueList.Items.Add(message);
        RenderQueueEmptyText.Visibility = Visibility.Collapsed;
    }

    private void SetMediaOperationState(bool running, string? status = null)
    {
        _isMediaOperationRunning = running;
        if (!string.IsNullOrWhiteSpace(status)) StatusText.Text = status;
        UpdateMediaIntelligenceActionState();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshMediaList()
    {
        var selectedAssetId = (MediaList.SelectedItem as MediaListItem)?.Asset.Id;
        RebuildMediaIndexes();

        var activeIds = _project.MediaLibrary.Select(asset => asset.Id).ToHashSet();
        for (var index = _mediaItems.Count - 1; index >= 0; index--)
        {
            var item = _mediaItems[index];
            if (activeIds.Contains(item.Asset.Id)) continue;
            _mediaItems.RemoveAt(index);
            _mediaItemsById.Remove(item.Asset.Id);
        }

        foreach (var asset in _project.MediaLibrary)
        {
            var thumbnailPath = GetMediaThumbnailPath(asset);
            var fallbackGlyph = GetMediaFallbackGlyph(asset.Kind);
            var durationText = FormatDuration(asset.Duration);
            if (_mediaItemsById.TryGetValue(asset.Id, out var existing))
            {
                existing.Update(asset, thumbnailPath, fallbackGlyph, durationText);
                continue;
            }

            var item = new MediaListItem(asset, thumbnailPath, fallbackGlyph, durationText);
            _mediaItemsById[asset.Id] = item;
            _mediaItems.Add(item);
        }

        RefreshProjectFolderFilters();
        RefreshMediaView();
        if (selectedAssetId.HasValue
            && _mediaItemsById.TryGetValue(selectedAssetId.Value, out var selected)
            && ShouldShowMediaItem(selected))
            MediaList.SelectedItem = selected;

        Dispatcher.BeginInvoke(QueueVisibleMediaThumbnails, DispatcherPriority.ContextIdle);
    }

    private bool ShouldShowMediaItem(object value)
    {
        if (value is not MediaListItem item) return false;
        if (_mediaKindFilter.HasValue && item.Asset.Kind != _mediaKindFilter.Value) return false;
        if (!string.IsNullOrWhiteSpace(_mediaFolderFilter)
            && !string.Equals(item.FolderPath, _mediaFolderFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.IsNullOrWhiteSpace(_mediaSearchText)
               || item.FileName.Contains(_mediaSearchText, StringComparison.OrdinalIgnoreCase)
               || item.FolderPath.Contains(_mediaSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshMediaView()
    {
        _mediaItemsView?.Refresh();
        var visibleCount = _mediaItemsView?.Cast<object>().Count() ?? _mediaItems.Count;
        MediaCountText.Text = $"{visibleCount} file{(visibleCount == 1 ? string.Empty : "s")}";
        MediaEmptyState.Visibility = Vis(visibleCount == 0);
        AddToTimelineButton.IsEnabled = MediaList.SelectedItem != null;
        PreviewSelectedMediaButton.IsEnabled = MediaList.SelectedItem != null;
        StatusText.Text = visibleCount == 0
            ? "Ready"
            : $"{visibleCount} project file{(visibleCount == 1 ? string.Empty : "s")} available";
        CommandManager.InvalidateRequerySuggested();
        Dispatcher.BeginInvoke(QueueVisibleMediaThumbnails, DispatcherPriority.ContextIdle);
    }

    private void QueueVisibleMediaThumbnails()
    {
        _thumbnailLoadCancellation?.Cancel();
        _thumbnailLoadCancellation?.Dispose();
        _thumbnailLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _thumbnailLoadCancellation.Token;
        var queued = 0;

        foreach (var item in _mediaItems)
        {
            if (!ShouldShowMediaItem(item)) continue;
            var container = MediaList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
            if (container?.IsVisible != true && queued >= 48) continue;
            if (!item.TryBeginThumbnailLoad()) continue;
            _ = LoadMediaThumbnailAsync(item, cancellationToken);
            queued++;
            if (queued >= 128) break;
        }
    }

    private async Task LoadMediaThumbnailAsync(MediaListItem item, CancellationToken cancellationToken)
    {
        var path = item.ThumbnailPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            item.CompleteThumbnailLoad(path, null);
            return;
        }

        var thumbnail = await _thumbnailCache.GetAsync(path, 240, cancellationToken);
        if (!cancellationToken.IsCancellationRequested)
            item.CompleteThumbnailLoad(path, thumbnail);
    }

    private void RebuildMediaIndexes()
    {
        _mediaById.Clear();
        _mediaAssetNames.Clear();
        foreach (var asset in _project.MediaLibrary)
        {
            _mediaById[asset.Id] = asset;
            _mediaAssetNames[asset.Id] = Path.GetFileName(asset.OriginalPath);
        }
    }

    private void SetMediaFilter(MediaKind? kind)
    {
        _mediaKindFilter = kind;
        UpdateMediaFilterButtons();
        RefreshMediaView();
    }

    private void UpdateMediaFilterButtons()
    {
        SetFilterButtonState(AllMediaFilterButton, !_mediaKindFilter.HasValue);
        SetFilterButtonState(VideoFilterButton, _mediaKindFilter == MediaKind.Video);
        SetFilterButtonState(ImageFilterButton, _mediaKindFilter == MediaKind.Image);
        SetFilterButtonState(AudioFilterButton, _mediaKindFilter == MediaKind.Audio);
    }

    private void SetFilterButtonState(Button button, bool active)
    {
        button.Background = active
            ? (Brush)FindResource("SelectionBrush")
            : Brushes.Transparent;
        button.BorderBrush = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("BorderBrush");
        button.Foreground = active
            ? (Brush)FindResource("TextBrush")
            : (Brush)FindResource("TextSecondaryBrush");
    }

    private void MoveClip(ClipMoveRequestedEventArgs args)
    {
        var seq = _project.MainSequence;
        if (seq == null || args.TargetTrackIndex < 0 || args.TargetTrackIndex >= seq.Tracks.Count) return;
        if (!double.IsFinite(args.NewStart.Seconds) || args.NewStart.Seconds < 0)
        {
            StatusText.Text = "Clip move canceled because the target time was invalid.";
            _timeline?.InvalidateVisual();
            return;
        }

        try
        {
            Execute(new MoveClipCommand
            {
                ItemId = args.Item.Id,
                TargetTrackId = seq.Tracks[args.TargetTrackIndex].Id,
                NewTimelineStart = args.NewStart,
                Ripple = _rippleState,
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Clip move failed: {ex.Message}";
            _timeline?.InvalidateVisual();
        }
    }

    private void MoveClipGroup(GroupMoveRequestedEventArgs args)
    {
        var sequence = _project.MainSequence;
        if (sequence == null || args.Changes.Count == 0) return;
        var commands = args.Changes.Select(change => (IEditCommand)new MoveClipCommand
        {
            ItemId = change.Item.Id,
            TargetTrackId = sequence.Tracks[change.TrackIndex].Id,
            NewTimelineStart = change.NewStart,
            Ripple = new RippleState(),
        });
        Execute(new CompositeEditCommand($"Move {args.Changes.Count} clips", commands));
    }

    private void TrimClipGroup(GroupTrimRequestedEventArgs args)
    {
        var sequence = _project.MainSequence;
        if (sequence == null || args.Changes.Count == 0) return;
        var commands = args.Changes.Select(change => (IEditCommand)new TrimClipCommand
        {
            TrackId = sequence.Tracks[change.TrackIndex].Id,
            ItemId = change.Item.Id,
            NewStart = change.NewStart,
            NewDuration = change.NewDuration,
            NewSourceStart = change.NewSourceStart,
            Ripple = new RippleState(),
        });
        Execute(new CompositeEditCommand($"Resize {args.Changes.Count} clips", commands));
    }

    private void SetClipVolume(ClipVolumeRequestedEventArgs args)
    {
        Execute(new SetPropertyCommand
        {
            ItemId = args.Item.Id,
            PropertyName = nameof(TimelineItem.Volume),
            NewValue = Math.Clamp(args.NewVolume, 0, 2),
            Getter = item => item.Volume,
            Setter = (item, value) => item.Volume = value is double volume ? volume : 1,
        });
    }

    private void TrimClip(ClipTrimRequestedEventArgs args)
    {
        var seq = _project.MainSequence;
        if (seq == null || args.TrackIndex < 0 || args.TrackIndex >= seq.Tracks.Count) return;

        Execute(new TrimClipCommand
        {
            TrackId = seq.Tracks[args.TrackIndex].Id,
            ItemId = args.Item.Id,
            NewStart = args.NewStart,
            NewDuration = args.NewDuration,
            NewSourceStart = args.NewSourceStart,
            Ripple = _rippleState,
        });
    }

    private async Task AddSelectedMediaToTimelineAsync()
    {
        if (MediaList.SelectedItem is not MediaListItem selected) return;
        var seq = _project.MainSequence;
        if (seq == null || _timeline == null) return;

        if (selected.Asset.Kind == MediaKind.Font)
        {
            StatusText.Text = "Font is registered. Select a text clip and choose it in Inspector > Properties.";
            await EnsureFontsLoadedAsync();
            return;
        }
        if (selected.Asset.Kind == MediaKind.Subtitle)
        {
            await AddSubtitleAssetToTimelineAsync(selected.Asset, seq);
            return;
        }

        var trackKind = selected.Asset.Kind switch
        {
            MediaKind.Audio => TrackKind.Audio,
            MediaKind.Image => TrackKind.Overlay,
            _ => TrackKind.Video,
        };
        var itemKind = selected.Asset.Kind == MediaKind.Image ? ItemKind.Image : ItemKind.Clip;

        var commands = new List<IEditCommand>();
        var track = seq.Tracks.FirstOrDefault(t => t.Kind == trackKind && !t.Locked);
        if (track == null)
        {
            track = new Track { Kind = trackKind, Name = trackKind == TrackKind.Audio ? "A1" : trackKind == TrackKind.Overlay ? "O1" : "V1", Order = seq.Tracks.Count };
            commands.Add(new AddPreparedTrackCommand { Track = track });
        }

        var duration = selected.Asset.Duration.Seconds > 0
            ? selected.Asset.Duration
            : MediaTime.FromSeconds(selected.Asset.Kind == MediaKind.Image ? 5 : 10);

        commands.Add(new AddClipCommand
        {
            TrackId = track.Id,
            Item = new TimelineItem
            {
                Kind = itemKind,
                MediaAssetId = selected.Asset.Id,
                TimelineStart = _timeline.PlayheadTime,
                Duration = duration,
                SourceDuration = duration,
            },
        });
        Execute(new CompositeEditCommand("Add media to timeline", commands));
        StatusText.Text = $"Added {Path.GetFileName(selected.Asset.OriginalPath)} to {track.Name} at {FormatPreviewTime(TimeSpan.FromSeconds(_timeline.PlayheadTime.Seconds))}";
        PreviewAsset(selected.Asset);
    }

    private async Task AddSubtitleAssetToTimelineAsync(MediaAsset asset, Sequence sequence)
    {
        if (!File.Exists(asset.OriginalPath))
        {
            StatusText.Text = "Subtitle file is offline";
            return;
        }

        IReadOnlyList<SubtitleCue> cues;
        try
        {
            cues = await SubtitleParser.ParseAsync(asset.OriginalPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Subtitle import failed: {ex.Message}";
            return;
        }
        if (cues.Count == 0)
        {
            StatusText.Text = "Subtitle file contains no valid cues";
            return;
        }

        var commands = new List<IEditCommand>();
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Kind == TrackKind.Text && !candidate.Locked);
        if (track == null)
        {
            track = new Track { Kind = TrackKind.Text, Name = "Subtitles", Order = sequence.Tracks.Count };
            commands.Add(new AddPreparedTrackCommand { Track = track });
        }
        var baseTime = _timeline?.PlayheadTime ?? MediaTime.Zero;
        foreach (var cue in cues)
        {
            commands.Add(new AddClipCommand
            {
                TrackId = track.Id,
                Item = new TimelineItem
                {
                    Kind = ItemKind.Text,
                    MediaAssetId = asset.Id,
                    TimelineStart = baseTime.Add(cue.Start),
                    Duration = cue.End.Subtract(cue.Start),
                    SourceStart = cue.Start,
                    SourceDuration = cue.End.Subtract(cue.Start),
                    TextContent = cue.Text,
                    FontSize = 48,
                    FontBold = true,
                    FontAlign = "center",
                    FillColor = "#FFFFFF",
                    OutlineColor = "#000000",
                    OutlineWidth = 4,
                    ShadowColor = "#000000",
                    ShadowOffsetX = 3,
                    ShadowOffsetY = 4,
                    ShadowOpacity = 0.75,
                    Transform = { PositionX = 80, PositionY = Math.Max(80, sequence.Height - 260) },
                },
            });
        }

        if (Execute(new CompositeEditCommand($"Import {cues.Count} subtitle cues", commands)))
            StatusText.Text = $"Imported {cues.Count} subtitle cues from {Path.GetFileName(asset.OriginalPath)}";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record GroupClipboardItem(TimelineItem Item, int TrackIndex, MediaTime RelativeStart);

    private static MediaKind GetMediaKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" or ".wav" or ".aac" or ".m4a" or ".flac" or ".ogg" or ".oga" or ".opus" or ".wma" or ".aif" or ".aiff" or ".ac3" or ".amr" or ".caf" => MediaKind.Audio,
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => MediaKind.Image,
        ".srt" or ".vtt" => MediaKind.Subtitle,
        ".ttf" or ".otf" => MediaKind.Font,
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => MediaKind.Video,
        _ => MediaKind.Other,
    };

    private string GetMediaThumbnailPath(MediaAsset asset) => asset.Kind switch
    {
        MediaKind.Image => asset.OriginalPath,
        MediaKind.Video => Path.Combine(_appData, "Cache", "thumbnails", $"{asset.Id}.jpg"),
        MediaKind.Audio => Path.Combine(_appData, "Cache", "waveforms", $"{asset.Id}.png"),
        _ => string.Empty,
    };

    private static string GetMediaFallbackGlyph(MediaKind kind) => kind switch
    {
        MediaKind.Video => "VID",
        MediaKind.Audio => "AUD",
        MediaKind.Image => "IMG",
        MediaKind.Subtitle => "CC",
        MediaKind.Font => "FONT",
        _ => "FILE",
    };

    private static string FormatDuration(MediaTime duration)
    {
        if (duration.Seconds <= 0) return string.Empty;
        var value = TimeSpan.FromSeconds(duration.Seconds);
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }

    private sealed record EffectListEntry(EffectInstance Effect, string Name, string Category)
    {
        public string DisplayName => $"{(Effect.Enabled ? "On" : "Off")}  {Name}  ·  {Category}";
    }

    private sealed class MediaListItem : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;
        private string _thumbnailPath;
        private string? _loadingPath;
        private bool _thumbnailLoadAttempted;

        public MediaListItem(
            MediaAsset asset,
            string thumbnailPath,
            string fallbackGlyph,
            string durationText)
        {
            Asset = asset;
            _thumbnailPath = thumbnailPath;
            FallbackGlyph = fallbackGlyph;
            DurationText = durationText;
        }

        public MediaAsset Asset { get; private set; }
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            private set
            {
                if (ReferenceEquals(_thumbnail, value)) return;
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }
        public string ThumbnailPath => _thumbnailPath;
        public string FallbackGlyph { get; private set; }
        public string DurationText { get; private set; }
        public string FileName => Path.GetFileName(Asset.OriginalPath);
        public string KindText => Asset.Kind.ToString().ToUpperInvariant();
        public string FolderPath => Path.GetDirectoryName(Asset.OriginalPath) ?? string.Empty;
        public string FolderDisplayName
        {
            get
            {
                var trimmed = FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrWhiteSpace(trimmed)
                    ? "Project root"
                    : Path.GetFileName(trimmed);
            }
        }
        public bool HasDuration => !string.IsNullOrEmpty(DurationText);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(
            MediaAsset asset,
            string thumbnailPath,
            string fallbackGlyph,
            string durationText)
        {
            var thumbnailChanged = !string.Equals(
                _thumbnailPath,
                thumbnailPath,
                StringComparison.OrdinalIgnoreCase);
            Asset = asset;
            _thumbnailPath = thumbnailPath;
            FallbackGlyph = fallbackGlyph;
            DurationText = durationText;
            if (thumbnailChanged)
            {
                Thumbnail = null;
                _thumbnailLoadAttempted = false;
                _loadingPath = null;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        public bool TryBeginThumbnailLoad()
        {
            if (_thumbnailLoadAttempted
                || string.IsNullOrWhiteSpace(_thumbnailPath)
                || string.Equals(_loadingPath, _thumbnailPath, StringComparison.OrdinalIgnoreCase))
                return false;
            _thumbnailLoadAttempted = true;
            _loadingPath = _thumbnailPath;
            return true;
        }

        public void CompleteThumbnailLoad(string path, ImageSource? thumbnail)
        {
            if (!string.Equals(path, _thumbnailPath, StringComparison.OrdinalIgnoreCase)) return;
            _loadingPath = null;
            Thumbnail = thumbnail;
        }
    }
}
