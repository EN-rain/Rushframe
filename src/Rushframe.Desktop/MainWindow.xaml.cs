using System.Windows;
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
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Workspace;
using Rushframe.Infrastructure;
using Rushframe.Application;
using Rushframe.Media.Native;
using Rushframe.Media.Abstractions;
using Microsoft.Win32;
using System.Globalization;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Rushframe.Desktop;

public partial class MainWindow : Window
{
    private const double MinimumUiScale = 0.75;
    private const double MaximumUiScale = 1.15;
    private const double UiScaleStep = 0.1;

    private readonly WorkspaceLayoutService _workspaceService;
    private readonly SettingsService _settingsService;
    private readonly IntelligenceBackendService _intelligenceBackend;
    private readonly LocalAgentBridgeService _localAgentBridge;
    private readonly string _appData;
    private WorkspaceLayout _layout;
    private EditorSettings _settings;

    private Project _project = new();
    private readonly UndoRedoStack _undoRedo = new();
    private readonly AutosaveService _autosave;
    private readonly ProjectRepository _projectRepo = new();
    private readonly MigrationService _migrationService;
    private readonly FfmpegMediaService _mediaService = new();
    private readonly StabilizationAnalysisService _stabilizationService;
    private readonly MediaIntelligenceImportService _mediaIntelligenceImportService = new();
    private readonly MediaIntelligenceSearchService _mediaIntelligenceSearchService = new();
    private readonly EffectRegistry _effectRegistry = new();
    private readonly RippleState _rippleState = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TimelineControl? _timeline;
    private CopyClipCommand? _clipboard;
    private int _lastSelectedTrackIndex;
    private TimelineItem? _selectedInspectorItem;
    private TransitionSelection? _selectedTransitionSelection;
    private MediaKind? _mediaKindFilter;
    private string _mediaSearchText = string.Empty;
    private string? _currentProjectPath;
    private bool _isPreviewSeeking;
    private bool _isPreviewPlaying;
    private MediaAsset? _previewAsset;
    private TimelineItemId? _previewTimelineItemId;
    private double? _previewMarkInSeconds;
    private double? _previewMarkOutSeconds;
    private bool _previewFullscreen;
    private readonly List<MediaAsset> _previewHistory = [];
    private bool _suppressInspectorChangeTracking;
    private bool _suppressTaskTracking;
    private bool _suppressTimelineZoomSliderChange;
    private bool _inspectorDirty;
    private bool _isMediaOperationRunning;
    private bool _projectDirty;
    private bool _allowClose;
    private CancellationTokenSource? _operationCancellation;
    private readonly Dictionary<string, TextBox> _effectParameterEditors = [];
    private readonly List<AgentAuditEntry> _agentAuditLog = [];
    public MainWindow()
    {
        _appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rushframe");
        _workspaceService = new WorkspaceLayoutService(_appData);
        _settingsService = new SettingsService(_appData);
        _layout = _workspaceService.Load();
        _settings = _settingsService.Load("editor", new EditorSettings());
        _intelligenceBackend = new IntelligenceBackendService(Math.Clamp(_settings.IntelligenceBackendPort, 1024, 65535));
        _localAgentBridge = new LocalAgentBridgeService(HandleLocalAgentBridgeRequestAsync);
        _autosave = new AutosaveService(Path.Combine(_appData, "autosave"));
        _migrationService = new MigrationService(Path.Combine(_appData, "backups"));
        _stabilizationService = new StabilizationAnalysisService(Path.Combine(_appData, "analysis", "stabilization"));

        InitializeComponent();
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
            if (_settings.StartIntelligenceBackend)
                await StartIntelligenceBackendAsync();
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
        McpStatusButton.Click += (_, _) => ShowLocalAgentStatusDialog();
        ConfigureTrackHeaderMenu();

        SideMediaButton.Click += (_, _) =>
        {
            if (_layout.IsPanelOpen(PanelId.Media))
            {
                TogglePanel(PanelId.Media);
                return;
            }

            TogglePanel(PanelId.Media);
            SetMediaFilter(null);
            MediaSearchBox.Focus();
        };
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
            CommandManager.InvalidateRequerySuggested();
        };
        AddToTimelineButton.Click += (_, _) => AddSelectedMediaToTimeline();
        PreviewSelectedMediaButton.Click += (_, _) => PreviewSelectedMedia();
        RunMediaIntelligenceButton.Click += async (_, _) => await RunMediaIntelligenceAsync();
        ApplyMediaIntelligenceButton.Click += async (_, _) => await ApplyCurrentMediaIntelligenceToTimelineAsync();
        OpenMediaAnalysisButton.Click += (_, _) => OpenSelectedMediaAnalysisOutput();
        SearchMediaContextButton.Click += (_, _) => SearchMediaContext(findHooks: false);
        FindHooksButton.Click += (_, _) => SearchMediaContext(findHooks: true);
        MediaContextSearchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            SearchMediaContext(findHooks: false);
            args.Handled = true;
        };
        MediaSearchBox.TextChanged += (_, _) =>
        {
            _mediaSearchText = MediaSearchBox.Text.Trim();
            MediaSearchHint.Visibility = string.IsNullOrEmpty(MediaSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            RefreshMediaList();
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

        PreviewPlayButton.Click += (_, _) => PlayPreview();
        PreviewPauseButton.Click += (_, _) => PausePreview();
        PreviewStopButton.Click += (_, _) => StopPreview();
        PreviewPlayer.MediaOpened += (_, _) => OnPreviewMediaOpened();
        PreviewPlayer.MediaEnded += (_, _) =>
        {
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
        PreviewSeekSlider.PreviewMouseLeftButtonDown += (_, _) => _isPreviewSeeking = true;
        PreviewSeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _isPreviewSeeking = false;
            SeekPreview(PreviewSeekSlider.Value);
        };
        PreviewPreviousFrameButton.Click += (_, _) => StepPreviewFrame(-1);
        PreviewNextFrameButton.Click += (_, _) => StepPreviewFrame(1);
        PreviewMarkInButton.Click += (_, _) => SetPreviewMark(true);
        PreviewMarkOutButton.Click += (_, _) => SetPreviewMark(false);
        PreviewClearMarksButton.Click += (_, _) => ClearPreviewMarks();
        PreviewInsertButton.Click += (_, _) => AddPreviewRangeToTimeline(overwrite: false);
        PreviewOverwriteButton.Click += (_, _) => AddPreviewRangeToTimeline(overwrite: true);
        PreviewMuteToggle.Checked += (_, _) => PreviewPlayer.IsMuted = true;
        PreviewMuteToggle.Unchecked += (_, _) => PreviewPlayer.IsMuted = false;
        PreviewVolumeSlider.ValueChanged += (_, _) => PreviewPlayer.Volume = PreviewVolumeSlider.Value;
        PreviewSpeedCombo.SelectionChanged += (_, _) => ApplyPreviewSpeed();
        PreviewZoomCombo.SelectionChanged += (_, _) => ApplyPreviewZoom();
        PreviewGuidesToggle.Checked += (_, _) => PreviewGuidesOverlay.Visibility = Visibility.Visible;
        PreviewGuidesToggle.Unchecked += (_, _) => PreviewGuidesOverlay.Visibility = Visibility.Collapsed;
        PreviewSnapshotButton.Click += (_, _) => SavePreviewSnapshot();
        PreviewFullscreenButton.Click += (_, _) => TogglePreviewFullscreen();
        PreviewRecentCombo.SelectionChanged += (_, _) =>
        {
            if (PreviewRecentCombo.SelectedItem is MediaAsset asset && asset != _previewAsset)
                PreviewAsset(asset);
        };
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
        _previewTimer.Tick += (_, _) => UpdatePreviewProgress();
        _previewTimer.Start();
        SetPreviewControlsEnabled(false);

        ApplyInspectorButton.Click += (_, _) => ApplyInspectorSettings();
        ResetInspectorButton.Click += (_, _) => ResetInspectorEdits();
        AddEffectButton.Click += (_, _) => AddSelectedEffect();
        EffectCombo.SelectionChanged += (_, _) =>
            AddEffectButton.IsEnabled = _selectedInspectorItem != null && EffectCombo.SelectedItem != null;
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
        CampaignDescriptionBox.TextChanged += (_, _) =>
        {
            if (_suppressTaskTracking) return;
            _project.CampaignDescription = CampaignDescriptionBox.Text;
            MarkProjectDirty("Campaign brief modified");
        };
        AddTaskButton.Click += (_, _) => AddCampaignTask();
        NewTaskBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            AddCampaignTask();
            args.Handled = true;
        };
        TaskList.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler((_, _) => MarkProjectDirty("Campaign task modified")));
        UpdateMediaFilterButtons();
        RefreshTasksPanel();

        BuildPanelsMenu();
        ApplyLayout();
        InitTimeline();
        ApplyEditorSettings();
        RestoreLatestAutosaveIfAvailable();

        RestartAutosave();

        CommandBindings.Add(new CommandBinding(EditorCommands.OpenProject, OpenProject_Executed));
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
            _previewTimer.Stop();
            PreviewPlayer.Stop();
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _autosave.StopBackground();
            _intelligenceBackend.Dispose();
            _localAgentBridge.Dispose();
            SaveLayout();
        };
    }

    private void InitTimeline()
    {
        _timeline = new TimelineControl();
        _timeline.Sequence = _project.MainSequence;
        _timeline.AssetNameResolver = assetId =>
            Path.GetFileName(_project.MediaLibrary.FirstOrDefault(asset => asset.Id == assetId)?.OriginalPath)
            ?? "Media";
        _timeline.ClipContextMenu = (ContextMenu)FindResource("TimelineClipContextMenu");
        _timeline.TrackHeaderContextMenu = (ContextMenu)FindResource("TrackHeaderContextMenu");
        _timeline.SnapEnabled = SnapToggle.IsChecked ?? true;

        _timeline.ClipSelected += (_, item) =>
        {
            _contextTrackIndex = item != null ? _timeline.SelectedTrackIndex : -1;
            if (item != null && _timeline.SelectedTrackIndex >= 0)
                _lastSelectedTrackIndex = _timeline.SelectedTrackIndex;
            if (item == null) _contextTrackIndex = -1;
            _selectedInspectorItem = item;
            if (item != null) _selectedTransitionSelection = null;
            UpdateInspector(item);
            if (item != null) PreviewTimelineItem(item);
            CommandManager.InvalidateRequerySuggested();
        };
        _timeline.TransitionSelected += (_, selection) => SelectTransition(selection);
        _timeline.PlayPauseRequested += (_, _) => TogglePreviewPlayback();
        _timeline.TrackHeaderContextRequested += (_, trackIndex) => PrepareTrackHeaderContextMenu(trackIndex);
        _timeline.DeleteSelectedClipRequested += (_, _) => DeleteSelectedClip();
        _timeline.ClipMoveRequested += (_, args) => MoveClip(args);
        _timeline.ClipTrimRequested += (_, args) => TrimClip(args);
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

        TimelineHost.Content = _timeline;
        RefreshMediaList();
        EffectCombo.ItemsSource = _effectRegistry.GetAll();
        TransitionKindCombo.ItemsSource = Enum.GetValues<TransitionKind>();
        UpdateInspector(null);
    }

    private void ConfigureTrackHeaderMenu()
    {
        var menu = (ContextMenu)FindResource("TrackHeaderContextMenu");
        ((MenuItem)menu.Items[0]).Click += (_, _) => RenameContextTrack();
        ((MenuItem)menu.Items[1]).Click += (_, _) => ExecuteForContextTrack(track => new DuplicateTrackCommand { TrackId = track.Id });
        ((MenuItem)menu.Items[3]).Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackMuteCommand { TrackId = track.Id });
        ((MenuItem)menu.Items[4]).Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackSoloCommand { TrackId = track.Id });
        ((MenuItem)menu.Items[5]).Click += (_, _) => ExecuteForContextTrack(track => new ToggleTrackLockCommand { TrackId = track.Id });
        ((MenuItem)menu.Items[7]).Click += (_, _) => DeleteContextTrack();
    }

    private void PrepareTrackHeaderContextMenu(int trackIndex)
    {
        _contextTrackIndex = trackIndex;
        var track = GetContextTrack();
        var menu = (ContextMenu)FindResource("TrackHeaderContextMenu");
        if (track == null)
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = false;
            return;
        }

        var rename = (MenuItem)menu.Items[0];
        var duplicate = (MenuItem)menu.Items[1];
        var mute = (MenuItem)menu.Items[3];
        var solo = (MenuItem)menu.Items[4];
        var @lock = (MenuItem)menu.Items[5];
        var delete = (MenuItem)menu.Items[7];

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
            var item = new MenuItem
            {
                Header = panel.Title,
                IsCheckable = true,
                IsChecked = panel.CanClose ? _layout.IsPanelOpen(panel.Id) : true,
                IsEnabled = panel.CanClose,
                ToolTip = panel.CanClose ? null : "The timeline is always available.",
                Tag = panel.Id,
            };
            if (panel.CanClose) item.Click += PanelMenuItem_Click;
            PanelsMenu.Items.Add(item);
        }
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
        _layout = _layout.WithPanelToggled(panelId, !current);
        foreach (MenuItem item in PanelsMenu.Items)
            if (item.Tag is PanelId id && id == panelId) item.IsChecked = !current;
        ApplyLayout();
        UpdateMediaFilterButtons();
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
        var tasksOpen = _layout.IsPanelOpen(PanelId.Tasks);
        var renderQueueOpen = _layout.IsPanelOpen(PanelId.RenderQueue);
        var intelligenceOpen = _layout.IsPanelOpen(PanelId.MediaIntelligence);
        var lowerRightOpen = tasksOpen || renderQueueOpen || intelligenceOpen;
        var rightColumnOpen = inspectorOpen || lowerRightOpen;
        var anyTopPanelOpen = mediaOpen || previewOpen;

        var compactWidth = ActualWidth > 0 && ActualWidth < 1180;
        var spaciousWidth = ActualWidth >= 1500;
        var mediaWidth = compactWidth ? 320 : spaciousWidth ? 410 : 370;
        var inspectorWidth = compactWidth ? 270 : spaciousWidth ? 330 : 300;

        MediaBorder.Visibility = Visibility.Visible;
        MediaSplitter.Visibility = Vis(mediaOpen);
        MediaColumn.Width = mediaOpen ? new GridLength(mediaWidth) : new GridLength(58);
        MediaSplitterColumn.Width = mediaOpen ? new GridLength(4) : new GridLength(0);
        AssetPanelColumn.Width = mediaOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        AssetPanelContent.Visibility = Vis(mediaOpen);

        PreviewBorder.Visibility = Vis(previewOpen);

        InspectorBorder.Visibility = Vis(inspectorOpen);
        InspectorSplitter.Visibility = Vis(rightColumnOpen);
        InspectorColumn.Width = rightColumnOpen ? new GridLength(inspectorWidth) : new GridLength(0);
        InspectorSplitterColumn.Width = rightColumnOpen ? new GridLength(4) : new GridLength(0);
        RightInspectorRow.Height = inspectorOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        TasksBorder.Visibility = Vis(lowerRightOpen);
        TasksTab.Visibility = Vis(tasksOpen);
        RenderQueueTab.Visibility = Vis(renderQueueOpen);
        MediaIntelligenceTab.Visibility = Vis(intelligenceOpen);
        RightTasksRow.Height = lowerRightOpen
            ? inspectorOpen ? new GridLength(270) : new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        if (tasksOpen && TasksTab.IsSelected == false && !renderQueueOpen && !intelligenceOpen)
            TasksTab.IsSelected = true;
        else if (renderQueueOpen && RenderQueueTab.IsSelected == false && !tasksOpen && !intelligenceOpen)
            RenderQueueTab.IsSelected = true;
        else if (intelligenceOpen && MediaIntelligenceTab.IsSelected == false && !tasksOpen && !renderQueueOpen)
            MediaIntelligenceTab.IsSelected = true;

        TimelineBorder.Visibility = Visibility.Visible;
        TimelineTasksSplitter.Visibility = Visibility.Collapsed;
        PreviewTimelineSplitter.Visibility = Vis(anyTopPanelOpen);
        WorkspaceRow.Height = anyTopPanelOpen
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        TimelineRow.Height = anyTopPanelOpen
            ? new GridLength(ActualHeight > 0 && ActualHeight < 700 ? 180 : spaciousWidth ? 300 : 250)
            : new GridLength(1, GridUnitType.Star);
    }

    private void UpdateResponsiveLayout()
    {
        if (!IsInitialized) return;

        var effectiveWidth = ActualWidth > 0 ? ActualWidth / Math.Max(_settings.UiScale, 0.01) : 0;
        var compact = effectiveWidth > 0 && effectiveWidth < 1380;
        var veryCompact = effectiveWidth > 0 && effectiveWidth < 1120;

        HeaderCenterTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeaderBrandIcon.Visibility = Visibility.Visible;
        HeaderBrandText.Visibility = Visibility.Collapsed;
        HeaderExportButton.Visibility = Visibility.Visible;
        LocalEditingStatusText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TimelineZoomControls.Visibility = Visibility.Visible;

        ApplyLayout();
    }

    private void Sequence_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _project.MainSequence != null;
        e.Handled = true;
    }

    private void SelectedClip_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _timeline?.SelectedItem != null;
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
        e.CanExecute = !_isMediaOperationRunning && _selectedInspectorItem?.MediaAssetId != null;
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
        e.CanExecute = _clipboard?.Clipboard != null
            && _project.MainSequence?.Tracks.Any(track => !track.Locked) == true;
        e.Handled = true;
    }

    private void RegisterInspectorChangeTracking()
    {
        foreach (var textBox in new[]
                 {
                     PositionXBox, PositionYBox, ScaleBox, RotationBox, OpacityBox, SpeedBox,
                     BrightnessBox, ContrastBox, SaturationBox, VolumeBox, PanBox,
                     TransitionDurationBox, TransitionAlignmentBox,
                 })
        {
            textBox.TextChanged += InspectorControlChanged;
            textBox.TextChanged += (_, _) => ClearInspectorValidation(textBox);
        }

        foreach (var checkBox in new[] { ReverseToggle, BlackWhiteToggle, StabilizeToggle })
        {
            checkBox.Checked += InspectorControlChanged;
            checkBox.Unchecked += InspectorControlChanged;
        }

        TransitionKindCombo.SelectionChanged += InspectorControlChanged;
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
        ApplyInspectorButton.IsEnabled = hasTarget && _inspectorDirty;
        ResetInspectorButton.IsEnabled = hasTarget && _inspectorDirty;
        InspectorDirtyText.Text = !hasTarget
            ? "Select a clip or transition to edit"
            : _inspectorDirty ? "Changes not applied" : "All changes applied";
        InspectorDirtyText.Foreground = _inspectorDirty
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("TextMutedBrush");
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
        if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            ClearInspectorValidation(textBox);
            return true;
        }

        textBox.BorderBrush = (Brush)FindResource("DangerBrush");
        textBox.BorderThickness = new Thickness(2);
        textBox.ToolTip = $"Enter a valid number for {label}.";
        InspectorDirtyText.Text = $"Invalid {label}";
        InspectorDirtyText.Foreground = (Brush)FindResource("DangerBrush");
        textBox.Focus();
        return false;
    }

    private void ClearInspectorValidation(TextBox textBox)
    {
        textBox.BorderBrush = (Brush)FindResource("BorderStrongBrush");
        textBox.BorderThickness = new Thickness(1);
        if (textBox.ToolTip is string tooltip && tooltip.StartsWith("Enter a valid number", StringComparison.Ordinal))
            textBox.ToolTip = null;
    }

    private void OpenUtilityPanel(PanelId panelId, TabItem tab)
    {
        if (!_layout.IsPanelOpen(panelId))
        {
            _layout = _layout.WithPanelToggled(panelId, true);
            foreach (MenuItem item in PanelsMenu.Items)
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
            foreach (MenuItem item in PanelsMenu.Items)
                if (item.Tag is PanelId id && id == PanelId.Inspector) item.IsChecked = true;
            ApplyLayout();
        }

        InspectorTabs.SelectedIndex = Math.Clamp(tabIndex, 0, InspectorTabs.Items.Count - 1);
    }

    private void PlayPreview()
    {
        if (!PreviewPlayButton.IsEnabled || PreviewPlayer.Visibility != Visibility.Visible) return;
        PreviewPlayer.Play();
        _isPreviewPlaying = true;
    }

    private void PausePreview()
    {
        if (!PreviewPauseButton.IsEnabled || PreviewPlayer.Visibility != Visibility.Visible) return;
        PreviewPlayer.Pause();
        _isPreviewPlaying = false;
    }

    private void StopPreview()
    {
        PreviewPlayer.Stop();
        PreviewPlayer.Position = TimeSpan.Zero;
        _isPreviewPlaying = false;
        UpdatePreviewProgress();
    }

    private void TogglePreviewPlayback()
    {
        if (_isPreviewPlaying) PausePreview();
        else PlayPreview();
    }

    private void SetPreviewControlsEnabled(bool enabled)
    {
        PreviewPlayButton.IsEnabled = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewPauseButton.IsEnabled = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewStopButton.IsEnabled = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewPreviousFrameButton.IsEnabled = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewNextFrameButton.IsEnabled = enabled && PreviewPlayer.Visibility == Visibility.Visible;
        PreviewSeekSlider.IsEnabled = enabled;
        PreviewTimeBox.IsEnabled = enabled;
        PreviewMarkInButton.IsEnabled = enabled;
        PreviewMarkOutButton.IsEnabled = enabled;
        PreviewClearMarksButton.IsEnabled = enabled;
        PreviewInsertButton.IsEnabled = enabled && _previewAsset != null;
        PreviewOverwriteButton.IsEnabled = enabled && _previewAsset != null;
        PreviewSnapshotButton.IsEnabled = enabled;
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

        return new Window
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
        if (!_settings.AutosaveEnabled)
        {
            _autosave.StopBackground();
            AutosaveStatusText.Text = "Autosave: off";
            return;
        }

        var interval = Math.Clamp(_settings.AutosaveIntervalSeconds, 5, 3600);
        _autosave.StartBackground(
            _project,
            TimeSpan.FromSeconds(interval),
            path => Dispatcher.BeginInvoke(() => ShowAutosaveSaved(path)),
            error => Dispatcher.BeginInvoke(() => AutosaveStatusText.Text = $"Autosave failed: {error.Message}"));
        AutosaveStatusText.Text = $"Autosave: {interval} seconds";
    }

    private void SaveAutosaveSnapshot()
    {
        if (!_settings.AutosaveEnabled) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(_currentProjectPath))
                _projectRepo.Save(_project, _currentProjectPath);
            var autosavePath = _autosave.Save(_project);
            ShowAutosaveSaved(!string.IsNullOrWhiteSpace(_currentProjectPath) ? _currentProjectPath : autosavePath);
        }
        catch (Exception ex)
        {
            AutosaveStatusText.Text = $"Autosave failed: {ex.Message}";
        }
    }

    private void ShowAutosaveSaved(string path)
    {
        AutosaveStatusText.Text = $"Autosaved {DateTime.Now:HH:mm:ss}";
        AutosaveStatusText.ToolTip = path;
    }

    private void RestoreLatestAutosaveIfAvailable()
    {
        if (!_settings.AutosaveEnabled) return;

        try
        {
            var restored = _autosave.LoadMostRecent();
            if (restored == null) return;

            var answer = MessageBox.Show(
                this,
                $"Rushframe found an autosaved copy of '{restored.Name}'. Restore it now?",
                "Recover Autosave",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                AutosaveStatusText.Text = "Autosave recovery skipped";
                return;
            }

            LoadProjectIntoEditor(restored, null, $"{restored.Name} (recovered)");
            _projectDirty = true;
            AutosaveStatusText.Text = "Recovered autosave — save to keep it";
        }
        catch (Exception ex)
        {
            AutosaveStatusText.Text = $"Autosave restore failed: {ex.Message}";
        }
    }

    private void Settings_Executed(object sender, ExecutedRoutedEventArgs e) => ShowSettingsDialog();

    private async Task StartIntelligenceBackendAsync()
    {
        var repoRoot = FindRepoRoot();
        var apiKey = SecretProtectionService.Unprotect(_settings.ProtectedGeminiApiKey);
        StatusText.Text = "Starting intelligence backend…";
        var started = await _intelligenceBackend.StartAsync(repoRoot, apiKey);
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
            coreCommand + "\n\nInstalls scene detection, faster-whisper, audio/beat analysis, OpenCV, semantic search, and Gemini support."));
        panel.Children.Add(CreateSetupSection(
            "Optional advanced install",
            advancedCommand + "\n\nAdds WhisperX, speaker detection, OCR, sound-event recognition, and local Qwen support. This is much heavier and is not recommended on a CPU-only 16 GB machine unless needed."));
        panel.Children.Add(CreateSetupSection(
            "Verify installation",
            doctorCommand));
        panel.Children.Add(CreateSetupSection(
            "Local agent connection",
            $"MCP endpoint: {GetMcpEndpoint()}\nHealth: {_intelligenceBackend.BaseUri}health\nTools: rushframe.capabilities, rushframe.search_moments, rushframe.get_agent_context, rushframe.get_timeline_state, rushframe.apply_timeline_edit, rushframe.render_timeline\nThe endpoint and live editor bridge are local-only and available while Rushframe is running."));
        panel.Children.Add(CreateSetupSection(
            "Recommended settings for 16 GB RAM / no GPU",
            "Whisper: base, CPU int8\nAI input: 5–10 minutes\nVisual provider: Gemini\nOCR/alignment/diarization/sound events: Off\nParallel analysis jobs: 1"));

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
            $"MCP endpoint: {GetMcpEndpoint()}\nHealth: {_intelligenceBackend.BaseUri}health\nTools: rushframe.capabilities, rushframe.search_moments, rushframe.get_agent_context, rushframe.get_timeline_state, rushframe.apply_timeline_edit, rushframe.render_timeline\nThe endpoint and live editor bridge are local-only and available while Rushframe is running."));
        panel.Children.Add(CreateSetupSection(
            "Recommended settings for 16 GB RAM / no GPU",
            "Whisper: base, CPU int8\nAI input: 5-10 minutes\nVisual provider: Gemini\nOCR/alignment/diarization/sound events: Off\nParallel analysis jobs: 1"));
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
            BorderBrush = ok ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            MinWidth = 74,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = ok ? "Installed" : "Missing",
                Foreground = ok ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush"),
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
            ["rushframe"] = new { url = GetMcpEndpoint() },
        },
    }, new JsonSerializerOptions { WriteIndented = true });

    private string GetCodexMcpConfig() => $"[mcp_servers.rushframe]{Environment.NewLine}url = \"{GetMcpEndpoint()}\"{Environment.NewLine}";

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
            "" or "health" => new { ok = true, service = "rushframe-editor-bridge" },
            "timeline" => BuildAgentTimelineState(),
            "audit" => new { ok = true, entries = _agentAuditLog.TakeLast(50).ToArray() },
            "edit" => await ApplyAgentTimelineEditAsync(payload ?? default, cancellationToken),
            "render" => await RenderAgentTimelineAsync(payload ?? default, cancellationToken),
            _ => new { ok = false, error = $"Unknown bridge endpoint: {path}" },
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
            projectPath = _currentProjectPath,
            sequence = new
            {
                id = sequence.Id.ToString(),
                name = sequence.Name,
                duration = sequence.Duration.Seconds,
                tracks = sequence.Tracks.Select((track, index) => new
                {
                    index,
                    id = track.Id.ToString(),
                    kind = track.Kind.ToString(),
                    name = track.Name,
                    muted = track.Muted,
                    solo = track.Solo,
                    locked = track.Locked,
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
                            text = item.TextContent,
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

        var action = ReadString(payload, "action");
        if (string.IsNullOrWhiteSpace(action))
            return new { ok = false, error = "Missing action" };

        var requireApproval = ReadBool(payload, "require_approval", true);
        var previewOnly = ReadBool(payload, "preview_only", false);
        var edit = BuildAgentEditCommand(sequence, payload, action);
        if (!edit.Success || edit.Command == null)
            return new { ok = false, error = edit.Error };

        if (previewOnly)
            return new { ok = true, preview = true, summary = edit.Summary, action };

        if (requireApproval && !ConfirmAgentEdit(edit.Summary))
        {
            AddAgentAudit(action, edit.Summary, false, "User rejected edit");
            return new { ok = false, rejected = true, error = "User rejected edit" };
        }

        var result = _undoRedo.Execute(sequence, edit.Command);
        if (!result.Success)
        {
            AddAgentAudit(action, edit.Summary, false, result.ErrorMessage ?? "Edit failed");
            return new { ok = false, error = result.ErrorMessage ?? "Edit failed" };
        }

        _timeline?.InvalidateVisual();
        CommandManager.InvalidateRequerySuggested();
        SaveAutosaveSnapshot();
        StatusText.Text = $"Agent edit applied: {edit.Summary}";
        AddAgentAudit(action, edit.Summary, true, null);
        await Task.CompletedTask.WaitAsync(cancellationToken);
        return new { ok = true, applied = true, summary = edit.Summary, timeline = BuildAgentTimelineState() };
    }

    private async Task<object> RenderAgentTimelineAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sequence = _project.MainSequence;
        if (sequence == null)
            return new { ok = false, error = "No active sequence" };

        var outputPath = ReadString(payload, "output_path");
        if (string.IsNullOrWhiteSpace(outputPath))
            return new { ok = false, error = "Missing output_path" };

        if (sequence.Duration.Seconds > _settings.MaxOutputDurationSeconds)
            return new { ok = false, error = "Timeline exceeds the 3-minute export limit" };

        var requireApproval = ReadBool(payload, "require_approval", true);
        var summary = $"Render timeline to {outputPath}";
        if (requireApproval && !ConfirmAgentEdit(summary))
        {
            AddAgentAudit("render_timeline", summary, false, "User rejected render");
            return new { ok = false, rejected = true, error = "User rejected render" };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Environment.CurrentDirectory);
        await _mediaService.ExportTimelineAsync(_project, sequence, outputPath);
        AddAgentAudit("render_timeline", summary, true, null);
        StatusText.Text = $"Agent render completed: {Path.GetFileName(outputPath)}";
        return new { ok = true, outputPath };
    }

    private AgentEditBuildResult BuildAgentEditCommand(Sequence sequence, JsonElement payload, string action)
    {
        try
        {
            return action.Trim().ToLowerInvariant() switch
            {
                "add_text" or "add_caption" => BuildAddTextAgentCommand(sequence, payload),
                "add_clip" or "add_music" => BuildAddMediaAgentCommand(sequence, payload),
                "move_clip" => BuildMoveClipAgentCommand(sequence, payload),
                "trim_clip" => BuildTrimClipAgentCommand(sequence, payload),
                "split_clip" => BuildSplitClipAgentCommand(sequence, payload),
                "delete_clip" => BuildDeleteClipAgentCommand(sequence, payload),
                "add_transition" => BuildTransitionAgentCommand(payload),
                "add_effect" => BuildEffectAgentCommand(payload),
                _ => AgentEditBuildResult.Fail($"Unsupported action: {action}"),
            };
        }
        catch (Exception ex)
        {
            return AgentEditBuildResult.Fail(ex.Message);
        }
    }

    private AgentEditBuildResult BuildAddTextAgentCommand(Sequence sequence, JsonElement payload)
    {
        var track = ResolveTrack(sequence, payload, TrackKind.Text);
        if (track == null) return AgentEditBuildResult.Fail("No text track found. Add a text track in Rushframe first.");
        var text = ReadString(payload, "text");
        if (string.IsNullOrWhiteSpace(text)) return AgentEditBuildResult.Fail("Missing text");
        var start = ReadSeconds(payload, "start", _timeline?.PlayheadTime.Seconds ?? 0);
        var duration = ReadSeconds(payload, "duration", 3);
        var item = new TimelineItem
        {
            Kind = ItemKind.Text,
            TimelineStart = MediaTime.FromSeconds(start),
            Duration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
            SourceDuration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
            TextContent = text,
            FontSize = ReadSeconds(payload, "font_size", 48),
            FillColor = ReadString(payload, "fill_color") ?? "#FFFFFF",
            OutlineColor = ReadString(payload, "outline_color"),
            OutlineWidth = ReadSeconds(payload, "outline_width", 0),
        };
        return AgentEditBuildResult.Ok(
            new AddClipCommand { TrackId = track.Id, Item = item },
            $"Add text at {start:0.##}s for {duration:0.##}s");
    }

    private AgentEditBuildResult BuildAddMediaAgentCommand(Sequence sequence, JsonElement payload)
    {
        var assetId = ParseMediaAssetId(ReadRequiredString(payload, "media_asset_id"));
        var asset = _project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
        if (asset == null) return AgentEditBuildResult.Fail("Media asset not found");
        var defaultTrackKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;
        var track = ResolveTrack(sequence, payload, defaultTrackKind);
        if (track == null) return AgentEditBuildResult.Fail($"No compatible {defaultTrackKind} track found.");
        var start = ReadSeconds(payload, "start", _timeline?.PlayheadTime.Seconds ?? 0);
        var sourceStart = ReadSeconds(payload, "source_start", 0);
        var duration = ReadSeconds(payload, "duration", asset.Duration.Seconds > 0 ? asset.Duration.Seconds : 5);
        var kind = asset.Kind == MediaKind.Image ? ItemKind.Image : ItemKind.Clip;
        var item = new TimelineItem
        {
            Kind = kind,
            MediaAssetId = asset.Id,
            TimelineStart = MediaTime.FromSeconds(start),
            Duration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
            SourceStart = MediaTime.FromSeconds(Math.Max(0, sourceStart)),
            SourceDuration = MediaTime.FromSeconds(Math.Max(0.1, duration)),
        };
        return AgentEditBuildResult.Ok(
            new AddClipCommand { TrackId = track.Id, Item = item },
            $"Add {Path.GetFileName(asset.OriginalPath)} at {start:0.##}s");
    }

    private static AgentEditBuildResult BuildMoveClipAgentCommand(Sequence sequence, JsonElement payload)
    {
        var itemId = ParseTimelineItemId(ReadRequiredString(payload, "item_id"));
        TrackId? targetTrackId = ReadString(payload, "track_id") is { } trackIdText && !string.IsNullOrWhiteSpace(trackIdText)
            ? ParseTrackId(trackIdText)
            : null;
        var command = new MoveClipCommand
        {
            ItemId = itemId,
            TargetTrackId = targetTrackId,
            NewTimelineStart = HasProperty(payload, "start") ? MediaTime.FromSeconds(ReadSeconds(payload, "start", 0)) : null,
        };
        return sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Id == itemId)
            ? AgentEditBuildResult.Ok(command, $"Move clip {itemId}")
            : AgentEditBuildResult.Fail("Clip not found");
    }

    private static AgentEditBuildResult BuildTrimClipAgentCommand(Sequence sequence, JsonElement payload)
    {
        var itemId = ParseTimelineItemId(ReadRequiredString(payload, "item_id"));
        var track = ResolveTrackForItem(sequence, itemId);
        if (track == null) return AgentEditBuildResult.Fail("Clip not found");
        var command = new TrimClipCommand
        {
            TrackId = track.Id,
            ItemId = itemId,
            NewStart = HasProperty(payload, "start") ? MediaTime.FromSeconds(ReadSeconds(payload, "start", 0)) : null,
            NewDuration = HasProperty(payload, "duration") ? MediaTime.FromSeconds(ReadSeconds(payload, "duration", 0)) : null,
            NewSourceStart = HasProperty(payload, "source_start") ? MediaTime.FromSeconds(ReadSeconds(payload, "source_start", 0)) : null,
        };
        return AgentEditBuildResult.Ok(command, $"Trim clip {itemId}");
    }

    private static AgentEditBuildResult BuildSplitClipAgentCommand(Sequence sequence, JsonElement payload)
    {
        var itemId = ParseTimelineItemId(ReadRequiredString(payload, "item_id"));
        var track = ResolveTrackForItem(sequence, itemId);
        if (track == null) return AgentEditBuildResult.Fail("Clip not found");
        var splitTime = ReadSeconds(payload, "time", 0);
        return AgentEditBuildResult.Ok(
            new Domain.Editing.SplitClipCommand
            {
                TrackId = track.Id,
                ItemId = itemId,
                SplitTime = MediaTime.FromSeconds(splitTime),
            },
            $"Split clip {itemId} at {splitTime:0.##}s");
    }

    private static AgentEditBuildResult BuildDeleteClipAgentCommand(Sequence sequence, JsonElement payload)
    {
        var itemId = ParseTimelineItemId(ReadRequiredString(payload, "item_id"));
        return sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Id == itemId)
            ? AgentEditBuildResult.Ok(new DeleteClipCommand { ItemId = itemId }, $"Delete clip {itemId}")
            : AgentEditBuildResult.Fail("Clip not found");
    }

    private static AgentEditBuildResult BuildTransitionAgentCommand(JsonElement payload)
    {
        var left = ParseTimelineItemId(ReadRequiredString(payload, "left_item_id"));
        var right = ParseTimelineItemId(ReadRequiredString(payload, "right_item_id"));
        var kindText = ReadString(payload, "kind") ?? nameof(TransitionKind.CrossDissolve);
        if (!Enum.TryParse<TransitionKind>(kindText, ignoreCase: true, out var kind))
            kind = TransitionKind.CrossDissolve;
        var duration = ReadSeconds(payload, "duration", 0.5);
        var alignment = ReadSeconds(payload, "alignment", 0.5);
        return AgentEditBuildResult.Ok(
            new ApplyTransitionCommand
            {
                LeftItemId = left,
                RightItemId = right,
                Kind = kind,
                Duration = MediaTime.FromSeconds(Math.Max(0.05, duration)),
                Alignment = Math.Clamp(alignment, 0, 1),
            },
            $"Apply {kind} transition");
    }

    private static AgentEditBuildResult BuildEffectAgentCommand(JsonElement payload)
    {
        var itemId = ParseTimelineItemId(ReadRequiredString(payload, "item_id"));
        var effectTypeId = ReadRequiredString(payload, "effect_type_id");
        var parameters = ReadObject(payload, "parameters");
        return AgentEditBuildResult.Ok(
            new AddEffectCommand
            {
                ItemId = itemId,
                EffectTypeId = effectTypeId,
                Parameters = parameters,
            },
            $"Add effect {effectTypeId}");
    }

    private Track? ResolveTrack(Sequence sequence, JsonElement payload, TrackKind fallbackKind)
    {
        var trackIdText = ReadString(payload, "track_id");
        if (!string.IsNullOrWhiteSpace(trackIdText))
        {
            var trackId = ParseTrackId(trackIdText);
            return sequence.Tracks.FirstOrDefault(track => track.Id == trackId);
        }

        return sequence.Tracks.FirstOrDefault(track => track.Kind == fallbackKind && !track.Locked);
    }

    private static Track? ResolveTrackForItem(Sequence sequence, TimelineItemId itemId) =>
        sequence.Tracks.FirstOrDefault(track => track.Items.Any(item => item.Id == itemId));

    private bool ConfirmAgentEdit(string summary) =>
        MessageBox.Show(
            this,
            summary,
            "Approve Agent Edit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void AddAgentAudit(string action, string summary, bool success, string? error)
    {
        _agentAuditLog.Add(new AgentAuditEntry(
            DateTimeOffset.UtcNow,
            action,
            summary,
            success,
            error));
        if (_agentAuditLog.Count > 200)
            _agentAuditLog.RemoveRange(0, _agentAuditLog.Count - 200);
    }

    private static bool HasProperty(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out _);

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static string ReadRequiredString(JsonElement payload, string name) =>
        ReadString(payload, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing {name}");

    private static double ReadSeconds(JsonElement payload, string name, double fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBool(JsonElement payload, string name, bool fallback)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback,
        };
    }

    private static Dictionary<string, object> ReadObject(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
            return [];
        return JsonSerializer.Deserialize<Dictionary<string, object>>(value.GetRawText()) ?? [];
    }

    private static TimelineItemId ParseTimelineItemId(string value) => new(Guid.Parse(value));
    private static TrackId ParseTrackId(string value) => new(Guid.Parse(value));
    private static MediaAssetId ParseMediaAssetId(string value) => new(Guid.Parse(value));

    private void ShowSettingsDialog()
    {
        StatusText.Text = "Opening settings…";
        var snapToggle = new CheckBox { Content = "Snap clips by default", IsChecked = SnapToggle.IsChecked ?? true };
        var rippleToggle = new CheckBox { Content = "Ripple editing by default", IsChecked = RippleToggle.IsChecked ?? false, Margin = new Thickness(0, 8, 0, 0) };
        var autosaveToggle = new CheckBox { Content = "Enable autosave", IsChecked = _settings.AutosaveEnabled, Margin = new Thickness(0, 14, 0, 0) };
        var autosaveBox = new TextBox { Text = Math.Clamp(_settings.AutosaveIntervalSeconds, 5, 3600).ToString(CultureInfo.InvariantCulture), Width = 92 };
        var backendToggle = new CheckBox { Content = "Start media-intelligence backend with Rushframe", IsChecked = _settings.StartIntelligenceBackend };
        var aiInputBox = new TextBox
        {
            Text = Math.Clamp(_settings.MaxAiInputSeconds, 30, 1800).ToString(CultureInfo.InvariantCulture),
            Width = 92,
        };
        var geminiKeyBox = new PasswordBox
        {
            Password = SecretProtectionService.Unprotect(_settings.ProtectedGeminiApiKey),
            MinHeight = 34,
            Margin = new Thickness(0, 7, 0, 0),
        };
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
            height: 680,
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
            Text = "Media Intelligence",
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
            Text = "Gemini API key",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        panel.Children.Add(geminiKeyBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Stored encrypted for your current Windows account. Leave blank to use only local models.",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
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

            _settings = new EditorSettings
            {
                SnapEnabled = snapToggle.IsChecked ?? true,
                RippleEnabled = rippleToggle.IsChecked ?? false,
                TimelineZoom = Math.Clamp(zoomSlider.Value, ZoomSlider.Minimum, ZoomSlider.Maximum),
                UiScale = Math.Clamp(uiScaleSlider.Value, MinimumUiScale, MaximumUiScale),
                AutosaveEnabled = autosaveToggle.IsChecked ?? true,
                AutosaveIntervalSeconds = Math.Clamp(interval, 5, 3600),
                StartIntelligenceBackend = backendToggle.IsChecked ?? true,
                IntelligenceBackendPort = _settings.IntelligenceBackendPort,
                ProtectedGeminiApiKey = SecretProtectionService.Protect(geminiKeyBox.Password),
                MaxAiInputSeconds = Math.Clamp(aiInputSeconds, 30, 1800),
                MaxOutputDurationSeconds = 180,
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

    private bool ConfirmCanReplaceCurrentProject()
    {
        if (!_projectDirty) return true;

        var answer = MessageBox.Show(
            this,
            "Save changes to the current project before opening another project?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes && !SaveCurrentProject()) return false;
        return true;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_projectDirty) return;

        var answer = MessageBox.Show(
            this,
            "Save changes to the current project before closing?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (answer == MessageBoxResult.Yes)
        {
            SaveCurrentProject();
            if (_projectDirty)
            {
                e.Cancel = true;
                return;
            }
        }

        _allowClose = true;
    }

    private void MarkProjectDirty(string status)
    {
        _projectDirty = true;
        StatusText.Text = status;
    }

    private bool SaveCurrentProject()
    {
        var path = _currentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Rushframe Project (*.rushframe)|*.rushframe",
                FileName = _project.Name + ".rushframe",
            };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }

        _projectRepo.Save(_project, path);
        _currentProjectPath = path;
        ProjectNameText.Text = Path.GetFileNameWithoutExtension(path);
        _projectDirty = false;
        StatusText.Text = "Project saved";
        return true;
    }

    private void Execute(IEditCommand cmd)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;

        var result = _undoRedo.Execute(sequence, cmd);
        if (!result.Success)
        {
            StatusText.Text = result.ErrorMessage ?? "The edit could not be applied.";
            return;
        }

        if (_selectedInspectorItem != null
            && !sequence.Tracks.SelectMany(track => track.Items).Any(item => item.Id == _selectedInspectorItem.Id))
        {
            _selectedInspectorItem = null;
            _selectedTransitionSelection = null;
            _timeline?.ClearSelection();
        }

        _timeline?.InvalidateVisual();
        UpdateInspector(_selectedInspectorItem);
        MarkProjectDirty("Project modified");
        SaveAutosaveSnapshot();
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadProjectIntoEditor(Project project, string? projectPath, string displayName)
    {
        _project = project;
        _currentProjectPath = projectPath;
        _undoRedo.Clear();
        _clipboard = null;
        _selectedInspectorItem = null;
        _selectedTransitionSelection = null;
        _contextTrackIndex = -1;
        _lastSelectedTrackIndex = 0;
        _projectDirty = false;
        if (_timeline != null) _timeline.Sequence = _project.MainSequence;
        ProjectNameText.Text = string.IsNullOrWhiteSpace(displayName) ? "Untitled Project" : displayName;
        ClearPreviewSurface("Nothing selected");
        RefreshMediaList();
        RefreshTasksPanel();
        UpdateInspector(null);
        RestartAutosave();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenProject_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmCanReplaceCurrentProject()) return;

        var dialog = new OpenFileDialog { Filter = "Rushframe Project (*.rushframe)|*.rushframe|Legacy Project (*/project.json)|project.json" };
        if (dialog.ShowDialog() != true) return;

        if (dialog.FileName.EndsWith("project.json"))
        {
            var legacyDir = Path.GetDirectoryName(dialog.FileName)!;
            var result = _migrationService.MigrateLegacyProject(legacyDir);
            if (result.Success && result.Project != null)
            {
                LoadProjectIntoEditor(result.Project, null, result.Project.Name);
            }
            else
            {
                MessageBox.Show($"Migration failed:\n{string.Join("\n", result.Errors)}", "Migration Error");
            }
        }
        else
        {
            var loaded = _projectRepo.Load(dialog.FileName);
            if (loaded != null)
            {
                LoadProjectIntoEditor(
                    loaded,
                    dialog.FileName,
                    Path.GetFileNameWithoutExtension(dialog.FileName));
            }
        }
    }

    private void SaveProject_Executed(object sender, ExecutedRoutedEventArgs e) => SaveCurrentProject();

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
            foreach (var file in dialog.FileNames)
            {
                var duration = MediaTime.Zero;
                try
                {
                    var probe = await _mediaService.ProbeAsync(file);
                    duration = MediaTime.FromSeconds(probe.Duration.TotalSeconds);
                }
                catch
                {
                    // Keep import usable without FFmpeg; probing can be retried later.
                }

                _project.MediaLibrary.Add(new MediaAsset
                {
                    Kind = GetMediaKind(file),
                    OriginalPath = file,
                    RelativeProjectPath = file,
                    Duration = duration,
                });
            }
            RefreshMediaList();
            MarkProjectDirty("Media imported");
        }
        finally
        {
            SetMediaOperationState(false, "Import complete");
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
            IsOffline = false,
        };
        var index = _project.MediaLibrary.FindIndex(a => a.Id == selected.Asset.Id);
        if (index >= 0) _project.MediaLibrary[index] = replacement;
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
                await _mediaService.GenerateWaveformAsync(new(asset.OriginalPath, Path.Combine(appData, "waveforms", $"{asset.Id}.png")));

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

    private async Task RunMediaIntelligenceAsync()
    {
        if (MediaList.SelectedItem is not MediaListItem selected)
        {
            AddMediaIntelligenceMessage("Select a media file first.");
            return;
        }

        var asset = selected.Asset;
        if (!File.Exists(asset.OriginalPath))
        {
            AddMediaIntelligenceMessage($"Analysis skipped, offline: {Path.GetFileName(asset.OriginalPath)}");
            return;
        }

        var outputDir = GetMediaAnalysisOutputDirectory(asset);
        Directory.CreateDirectory(outputDir);

        if (_isMediaOperationRunning) return;
        SetMediaOperationState(true, $"Analyzing {Path.GetFileName(asset.OriginalPath)}…");
        RunMediaIntelligenceButton.IsEnabled = false;
        MediaIntelligenceTab.IsSelected = true;
        AddMediaIntelligenceMessage($"Analyzing: {Path.GetFileName(asset.OriginalPath)}");
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
            if (AnalyzeScenesToggle.IsChecked != true) args.Add("--no-scenes");
            if (AnalyzeTranscriptToggle.IsChecked != true) args.Add("--no-transcript");
            if (AnalyzeMusicToggle.IsChecked != true) args.Add("--no-audio");
            if (AnalyzeGeminiToggle.IsChecked == true)
            {
                args.Add("--visual-provider");
                args.Add(GetSelectedVisualProvider());
            }
            if (AnalyzeOcrToggle.IsChecked == true) args.Add("--ocr");
            if (AnalyzeAlignmentToggle.IsChecked == true) args.Add("--alignment");
            if (AnalyzeDiarizationToggle.IsChecked == true) args.Add("--diarization");
            if (AnalyzeAudioEventsToggle.IsChecked == true) args.Add("--audio-events");
            if (BuildEmbeddingsToggle.IsChecked == true) args.Add("--embeddings");

            var result = await RunPythonAsync(args, repoRoot);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                AddMediaIntelligenceMessage(result.StandardOutput.Trim());
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                AddMediaIntelligenceMessage(result.StandardError.Trim());

            if (result.ExitCode != 0)
            {
                AddMediaIntelligenceMessage($"Analysis failed with exit code {result.ExitCode}.");
                return;
            }

            var analysisPath = Path.Combine(outputDir, "media-analysis.json");
            SummarizeMediaAnalysis(analysisPath);
            await ApplyMediaIntelligenceToTimelineAsync(analysisPath, asset, autoApply: true);
            AddRenderQueueMessage($"Media intelligence complete: {Path.GetFileName(asset.OriginalPath)}");
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Analysis failed: {ex.Message}");
        }
        finally
        {
            RunMediaIntelligenceButton.IsEnabled = true;
            SetMediaOperationState(false, "Media analysis finished");
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

        var analysisPath = Path.Combine(GetMediaAnalysisOutputDirectory(asset), "media-analysis.json");
        await ApplyMediaIntelligenceToTimelineAsync(analysisPath, asset, autoApply: false);
    }

    private async void ImportMediaIntelligence_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenUtilityPanel(PanelId.MediaIntelligence, MediaIntelligenceTab);
        var asset = ResolveMediaIntelligenceAsset();
        if (asset == null)
        {
            MessageBox.Show(this, "Select a media item or timeline clip first.", "Media Intelligence", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Media Intelligence Analysis",
            Filter = "Media analysis JSON|media-analysis.json;*.json|JSON files|*.json|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        await ApplyMediaIntelligenceToTimelineAsync(dialog.FileName, asset, autoApply: false);
    }

    private async Task ApplyMediaIntelligenceToTimelineAsync(string analysisPath, MediaAsset asset, bool autoApply)
    {
        if (!File.Exists(analysisPath))
        {
            if (!autoApply)
                AddMediaIntelligenceMessage("Run analysis first or choose an existing media-analysis.json file.");
            return;
        }

        var target = ResolveMediaIntelligenceTarget(asset.Id);
        if (target == null)
        {
            AddMediaIntelligenceMessage($"Analysis saved, but '{Path.GetFileName(asset.OriginalPath)}' is not on the timeline yet.");
            return;
        }

        try
        {
            var analysis = await _mediaIntelligenceImportService.ImportAsync(analysisPath, asset);
            MediaIntelligenceImportService.StoreInProject(_project, analysis);
            var command = new ApplyMediaIntelligenceCommand
            {
                TargetItemId = target.Id,
                Analysis = analysis,
                AddSceneMarkers = true,
                AddCaptionClips = true,
            };
            Execute(command);

            if (!string.IsNullOrWhiteSpace(_currentProjectPath))
                _projectRepo.Save(_project, _currentProjectPath);
            else
                _autosave.Save(_project);

            var message = $"Timeline updated: {command.CreatedMarkerCount} scene markers and {command.CreatedCaptionCount} caption clips.";
            AddMediaIntelligenceMessage(message);
            StatusText.Text = message;
            _timeline?.ScrollToTime(target.TimelineStart);
        }
        catch (Exception ex)
        {
            AddMediaIntelligenceMessage($"Could not apply analysis: {ex.Message}");
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
        if (_timeline?.SelectedItem is { } selected && selected.MediaAssetId == assetId)
            return selected;

        return _project.MainSequence?.Tracks
            .SelectMany(track => track.Items)
            .FirstOrDefault(item => item.MediaAssetId == assetId);
    }

    private void OpenSelectedMediaAnalysisOutput()
    {
        if (MediaList.SelectedItem is not MediaListItem selected)
        {
            AddMediaIntelligenceMessage("Select a media file first.");
            return;
        }

        var outputDir = GetMediaAnalysisOutputDirectory(selected.Asset);
        Directory.CreateDirectory(outputDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = outputDir,
            UseShellExecute = true,
        });
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
            AddMediaIntelligenceMessage("No searchable editing moments are loaded. Run and apply media intelligence first.");
            return;
        }

        IReadOnlyList<MediaMomentSearchResult> results = findHooks
            ? _mediaIntelligenceSearchService.FindHooks(analysis, limit: 8)
            : _mediaIntelligenceSearchService.Search(
                analysis,
                new MediaMomentSearchQuery(MediaContextSearchBox.Text, Limit: 12));

        MediaIntelligenceList.Items.Clear();
        MediaIntelligenceEmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaIntelligenceEmptyText.Text = results.Count == 0 ? "No matching moments" : "Select media, then run analysis";
        foreach (var result in results)
        {
            var roles = result.Roles.Count > 0 ? string.Join(", ", result.Roles) : "context";
            MediaIntelligenceList.Items.Add(
                $"{FormatPreviewTime(TimeSpan.FromSeconds(result.Start.Seconds))}–{FormatPreviewTime(TimeSpan.FromSeconds(result.End.Seconds))}  [{roles}]  {result.Summary}");
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

    private async Task<ProcessResult> RunPythonAsync(IReadOnlyList<string> args, string workingDirectory)
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
            if (!string.IsNullOrWhiteSpace(geminiApiKey))
                psi.Environment["GEMINI_API_KEY"] = geminiApiKey;

            try
            {
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Python did not start.");
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
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
        WhisperModelCombo.SelectedItem is ComboBoxItem item && item.Content is string value
            ? value
            : "base";

    private string GetSelectedVisualProvider() =>
        VisualProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string value
            ? value
            : "gemini";

    private static int CountArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

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
                if (File.Exists(Path.Combine(directory.FullName, "rushframe_intelligence", "pipeline.py")))
                    return directory.FullName;
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
            var asset = new MediaAsset
            {
                Kind = MediaKind.Audio,
                OriginalPath = output,
                RelativeProjectPath = output,
                Duration = source.Duration,
            };
            _project.MediaLibrary.Add(asset);
            RefreshMediaList();
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

    private void AddText_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var seq = _project.MainSequence;
        if (seq == null || _timeline == null) return;

        var text = PromptForTextClip();
        if (string.IsNullOrWhiteSpace(text)) return;

        var track = seq.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Text && !t.Locked);
        if (track == null)
        {
            track = new Track { Kind = TrackKind.Text, Name = "T1", Order = seq.Tracks.Count };
            seq.Tracks.Add(track);
        }

        Execute(new AddClipCommand
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
        Execute(new AddMarkerCommand
        {
            Marker = new Marker
            {
                Label = $"Marker {_project.MainSequence.Markers.Count + 1}",
                Time = _timeline.PlayheadTime,
                Color = "#ffcc00",
            },
        });
    }

    private async void Render_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var seq = _project.MainSequence;
        if (seq == null) return;

        const double hardOutputLimitSeconds = 180;
        if (seq.Duration.Seconds > hardOutputLimitSeconds + 0.001)
        {
            MessageBox.Show(
                this,
                $"The timeline is {FormatPreviewTime(TimeSpan.FromSeconds(seq.Duration.Seconds))}. Rushframe output is limited to 03:00. Trim or remove content after 3 minutes before exporting.",
                "Output Too Long",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = "Export blocked: timeline exceeds the 3-minute limit";
            return;
        }

        var dialog = new SaveFileDialog { Filter = "MP4 Video (*.mp4)|*.mp4", FileName = $"{_project.Name}.mp4" };
        if (dialog.ShowDialog() != true) return;

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
        AddRenderQueueMessage($"Export started: {Path.GetFileName(dialog.FileName)}");
        try
        {
            await _mediaService.ExportTimelineAsync(
                _project,
                seq,
                dialog.FileName,
                progress,
                _operationCancellation.Token);
            AddRenderQueueMessage($"Export complete: {Path.GetFileName(dialog.FileName)}");
            var answer = MessageBox.Show(
                this,
                $"Export complete:\n{dialog.FileName}\n\nOpen the containing folder?",
                "Export Complete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            AddRenderQueueMessage($"Export canceled: {Path.GetFileName(dialog.FileName)}");
            StatusText.Text = "Export canceled";
        }
        catch (Exception ex)
        {
            AddRenderQueueMessage($"Export failed: {ex.Message}");
            MessageBox.Show($"Render failed:\n{ex.Message}", "Render Error");
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
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        var result = _undoRedo.Undo(sequence);
        if (!result.Success) return;
        RefreshSelectionAfterEdit(sequence);
        _timeline?.InvalidateVisual();
        UpdateInspector(_selectedInspectorItem);
        MarkProjectDirty("Undo applied");
        CommandManager.InvalidateRequerySuggested();
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var sequence = _project.MainSequence;
        if (sequence == null) return;
        var result = _undoRedo.Redo(sequence);
        if (!result.Success) return;
        RefreshSelectionAfterEdit(sequence);
        _timeline?.InvalidateVisual();
        UpdateInspector(_selectedInspectorItem);
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
        if (seq == null || _timeline == null || _clipboard?.Clipboard == null) return;

        var preferredIndex = _timeline.SelectedTrackIndex >= 0
            ? _timeline.SelectedTrackIndex
            : _lastSelectedTrackIndex;
        var targetTrack = preferredIndex >= 0 && preferredIndex < seq.Tracks.Count && !seq.Tracks[preferredIndex].Locked
            ? seq.Tracks[preferredIndex]
            : seq.Tracks.FirstOrDefault(track => !track.Locked);
        if (targetTrack == null) return;

        Execute(new PasteClipCommand
        {
            TrackId = targetTrack.Id,
            TimelineStart = _timeline.PlayheadTime,
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
        var item = _timeline?.SelectedItem;
        if (item == null) return;
        Execute(new Domain.Editing.DeleteClipCommand { ItemId = item.Id });
    }

    private void RippleDelete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var item = _timeline?.SelectedItem;
        if (item == null) return;
        Execute(new RippleDeleteClipCommand { ItemId = item.Id, Ripple = _rippleState });
    }

    private void Duplicate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var item = _timeline?.SelectedItem;
        if (item == null) return;
        Execute(new DuplicateClipCommand { ItemId = item.Id });
    }

    private bool CopySelectedClip()
    {
        var item = _timeline?.SelectedItem;
        var seq = _project.MainSequence;
        if (item == null || seq == null) return false;

        var copy = new CopyClipCommand { ItemId = item.Id };
        var result = copy.Execute(seq);
        if (!result.Success) return false;
        _clipboard = copy;
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private void RefreshTasksPanel()
    {
        _suppressTaskTracking = true;
        try
        {
            CampaignDescriptionBox.Text = _project.CampaignDescription;
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = _project.Tasks;
            TasksEmptyText.Visibility = _project.Tasks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _suppressTaskTracking = false;
        }
    }

    private void AddCampaignTask()
    {
        var title = NewTaskBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            NewTaskBox.Focus();
            return;
        }

        _project.Tasks.Add(new CampaignTask { Title = title });
        NewTaskBox.Clear();
        RefreshTasksPanel();
        MarkProjectDirty("Campaign task added");
    }

    private void DeleteCampaignTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid taskId }) return;
        var task = _project.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        if (task == null) return;
        _project.Tasks.Remove(task);
        RefreshTasksPanel();
        MarkProjectDirty("Campaign task deleted");
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
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshMediaList()
    {
        var selectedAssetId = (MediaList.SelectedItem as MediaListItem)?.Asset.Id;
        var assets = _project.MediaLibrary.AsEnumerable();

        if (_mediaKindFilter.HasValue)
            assets = assets.Where(asset => asset.Kind == _mediaKindFilter.Value);

        if (!string.IsNullOrWhiteSpace(_mediaSearchText))
        {
            assets = assets.Where(asset =>
                Path.GetFileName(asset.OriginalPath).Contains(
                    _mediaSearchText,
                    StringComparison.OrdinalIgnoreCase));
        }

        var visibleAssets = assets.ToList();
        MediaList.Items.Clear();
        foreach (var asset in visibleAssets)
            MediaList.Items.Add(CreateMediaListItem(asset));

        if (selectedAssetId.HasValue)
        {
            MediaList.SelectedItem = MediaList.Items
                .OfType<MediaListItem>()
                .FirstOrDefault(item => item.Asset.Id == selectedAssetId.Value);
        }

        MediaCountText.Text = $"{visibleAssets.Count} item{(visibleAssets.Count == 1 ? string.Empty : "s")}";
        MediaEmptyState.Visibility = Vis(visibleAssets.Count == 0);
        AddToTimelineButton.IsEnabled = MediaList.SelectedItem != null;
        PreviewSelectedMediaButton.IsEnabled = MediaList.SelectedItem != null;
        StatusText.Text = visibleAssets.Count == 0
            ? "Ready"
            : $"{visibleAssets.Count} media item{(visibleAssets.Count == 1 ? string.Empty : "s")} available";
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetMediaFilter(MediaKind? kind)
    {
        _mediaKindFilter = kind;
        UpdateMediaFilterButtons();
        RefreshMediaList();
    }

    private void UpdateMediaFilterButtons()
    {
        SetFilterButtonState(AllMediaFilterButton, !_mediaKindFilter.HasValue);
        SetFilterButtonState(VideoFilterButton, _mediaKindFilter == MediaKind.Video);
        SetFilterButtonState(ImageFilterButton, _mediaKindFilter == MediaKind.Image);
        SetFilterButtonState(AudioFilterButton, _mediaKindFilter == MediaKind.Audio);

        var mediaOpen = _layout.IsPanelOpen(PanelId.Media);
        SideMediaButton.Style = (Style)FindResource(
            mediaOpen && (_mediaKindFilter is null or MediaKind.Video) ? "ActiveRailButtonStyle" : "RailButtonStyle");
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

        Execute(new MoveClipCommand
        {
            ItemId = args.Item.Id,
            TargetTrackId = seq.Tracks[args.TargetTrackIndex].Id,
            NewTimelineStart = args.NewStart,
            Ripple = _rippleState,
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

    private void AddSelectedMediaToTimeline()
    {
        if (MediaList.SelectedItem is not MediaListItem selected) return;
        var seq = _project.MainSequence;
        if (seq == null || _timeline == null) return;

        var trackKind = selected.Asset.Kind switch
        {
            MediaKind.Audio => TrackKind.Audio,
            MediaKind.Image => TrackKind.Overlay,
            _ => TrackKind.Video,
        };
        var itemKind = selected.Asset.Kind == MediaKind.Image ? ItemKind.Image : ItemKind.Clip;

        var track = seq.Tracks.FirstOrDefault(t => t.Kind == trackKind && !t.Locked);
        if (track == null)
        {
            track = new Track { Kind = trackKind, Name = trackKind == TrackKind.Audio ? "A1" : trackKind == TrackKind.Overlay ? "O1" : "V1", Order = seq.Tracks.Count };
            seq.Tracks.Add(track);
        }

        var duration = selected.Asset.Duration.Seconds > 0
            ? selected.Asset.Duration
            : MediaTime.FromSeconds(selected.Asset.Kind == MediaKind.Image ? 5 : 10);

        Execute(new AddClipCommand
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
        StatusText.Text = $"Added {Path.GetFileName(selected.Asset.OriginalPath)} to {track.Name} at {FormatPreviewTime(TimeSpan.FromSeconds(_timeline.PlayheadTime.Seconds))}";
        PreviewAsset(selected.Asset);
    }

    private void PreviewSelectedMedia()
    {
        if (MediaList.SelectedItem is MediaListItem selected) PreviewAsset(selected.Asset);
    }

    private void PreviewTimelineItem(TimelineItem item)
    {
        if (!item.MediaAssetId.HasValue) return;
        var asset = _project.MediaLibrary.FirstOrDefault(a => a.Id == item.MediaAssetId.Value);
        if (asset == null) return;

        _previewTimelineItemId = item.Id;
        if (IsPreviewSurfaceLoadedFor(asset))
        {
            PreviewSourceNameText.Text = Path.GetFileName(asset.OriginalPath);
            return;
        }

        PreviewAsset(asset, clearTimelineSelection: false);
    }

    private void PreviewAsset(MediaAsset asset) => PreviewAsset(asset, clearTimelineSelection: true);

    private void PreviewAsset(MediaAsset asset, bool clearTimelineSelection)
    {
        if (clearTimelineSelection) _previewTimelineItemId = null;
        _previewAsset = asset;
        ClearPreviewMarks();
        _previewHistory.RemoveAll(item => item.Id == asset.Id);
        _previewHistory.Insert(0, asset);
        if (_previewHistory.Count > 12) _previewHistory.RemoveRange(12, _previewHistory.Count - 12);
        PreviewRecentCombo.ItemsSource = null;
        PreviewRecentCombo.ItemsSource = _previewHistory;
        PreviewRecentCombo.SelectedItem = asset;

        if (!File.Exists(asset.OriginalPath))
        {
            ClearPreviewSurface("Media file is offline");
            return;
        }

        PreviewSourceNameText.Text = Path.GetFileName(asset.OriginalPath);
        StopPreview();
        PreviewPlayer.Source = null;
        PreviewSeekSlider.Value = 0;
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
        PreviewPlayer.Source = new Uri(asset.OriginalPath);
    }

    private void ClearPreviewSurface(string message)
    {
        _previewTimelineItemId = null;
        StopPreview();
        PreviewPlayer.Source = null;
        PreviewPlayer.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewSeekSlider.Value = 0;
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
        {
            return PreviewImage.Source != null && PreviewImage.Visibility == Visibility.Visible;
        }

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
        PreviewSeekSlider.Maximum = Math.Max(0.001, duration.TotalSeconds);
        PreviewDurationText.Text = FormatPreviewTime(duration);
        PreviewTimeBox.Text = "00:00";
        SetPreviewControlsEnabled(true);
        UpdatePreviewProgress();
    }

    private void UpdatePreviewProgress()
    {
        var position = PreviewPlayer.Visibility == Visibility.Visible
            ? PreviewPlayer.Position
            : TimeSpan.Zero;
        if (PreviewPlayer.Visibility != Visibility.Visible) return;

        if (_previewMarkOutSeconds.HasValue && position.TotalSeconds >= _previewMarkOutSeconds.Value)
        {
            if (PreviewLoopToggle.IsChecked == true)
            {
                PreviewPlayer.Position = TimeSpan.FromSeconds(_previewMarkInSeconds ?? 0);
                position = PreviewPlayer.Position;
            }
            else
            {
                PausePreview();
                PreviewPlayer.Position = TimeSpan.FromSeconds(_previewMarkOutSeconds.Value);
                position = PreviewPlayer.Position;
            }
        }

        if (!_isPreviewSeeking)
            PreviewSeekSlider.Value = Math.Clamp(position.TotalSeconds, 0, PreviewSeekSlider.Maximum);
        var formatted = FormatPreviewTime(position);
        PreviewTimeText.Text = formatted;
        if (!PreviewTimeBox.IsKeyboardFocusWithin) PreviewTimeBox.Text = formatted;
    }

    private static string FormatPreviewTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void SeekPreview(double seconds)
    {
        if (PreviewPlayer.Visibility != Visibility.Visible) return;
        var clamped = Math.Clamp(seconds, 0, PreviewSeekSlider.Maximum);
        PreviewPlayer.Position = TimeSpan.FromSeconds(clamped);
        PreviewSeekSlider.Value = clamped;
        UpdatePreviewProgress();
    }

    private void StepPreviewFrame(int direction)
    {
        PausePreview();
        SeekPreview(PreviewPlayer.Position.TotalSeconds + direction / 30.0);
    }

    private void SetPreviewMark(bool isIn)
    {
        var value = Math.Clamp(PreviewPlayer.Position.TotalSeconds, 0, PreviewSeekSlider.Maximum);
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

    private void ClearPreviewMarks()
    {
        _previewMarkInSeconds = null;
        _previewMarkOutSeconds = null;
        UpdatePreviewMarkLabels();
        StatusText.Text = "Source marks cleared";
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
        var track = seq.Tracks.FirstOrDefault(t => t.Kind == trackKind && !t.Locked);
        if (track == null)
        {
            track = new Track
            {
                Kind = trackKind,
                Name = trackKind == TrackKind.Audio ? "A1" : trackKind == TrackKind.Overlay ? "O1" : "V1",
                Order = seq.Tracks.Count,
            };
            seq.Tracks.Add(track);
        }

        var timelineStart = _timeline.PlayheadTime;
        var duration = MediaTime.FromSeconds(durationSeconds);
        if (overwrite)
        {
            var overwriteEnd = timelineStart.Add(duration);
            var overlapping = track.Items
                .Where(item => item.TimelineStart < overwriteEnd && item.TimelineStart.Add(item.Duration) > timelineStart)
                .ToList();
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

            foreach (var existing in overlapping)
                Execute(new DeleteClipCommand { ItemId = existing.Id });
        }

        Execute(new AddClipCommand
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
        StatusText.Text = overwrite ? "Source range overwritten at playhead" : "Source range inserted at playhead";
    }

    private void ApplyPreviewSpeed()
    {
        if (PreviewSpeedCombo.SelectedItem is ComboBoxItem item
            && double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            PreviewPlayer.SpeedRatio = speed;
    }

    private void ApplyPreviewZoom()
    {
        if (PreviewZoomCombo.SelectedItem is not ComboBoxItem item
            || !double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)) return;
        PreviewScaleTransform.ScaleX = zoom;
        PreviewScaleTransform.ScaleY = zoom;
    }

    private void SavePreviewSnapshot()
    {
        if (PreviewSurface.ActualWidth <= 0 || PreviewSurface.ActualHeight <= 0) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"rushframe-frame-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog(this) != true) return;

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(PreviewSurface.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(PreviewSurface.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(PreviewSurface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        StatusText.Text = "Snapshot saved";
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
            PreviewFullscreenButton.Content = "⤢";
        }
        else
        {
            Panel.SetZIndex(PreviewBorder, 0);
            Grid.SetColumn(PreviewBorder, 2);
            Grid.SetColumnSpan(PreviewBorder, 1);
            Grid.SetRow(PreviewBorder, 0);
            Grid.SetRowSpan(PreviewBorder, 1);
            PreviewFullscreenButton.Content = "⛶";
        }
    }

    private void OnPreviewKeyboardShortcut(object sender, KeyEventArgs args)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        switch (args.Key)
        {
            case Key.Space:
                if (_isPreviewPlaying) PausePreview(); else PlayPreview();
                args.Handled = true;
                break;
            case Key.Left:
                StepPreviewFrame(-1);
                args.Handled = true;
                break;
            case Key.Right:
                StepPreviewFrame(1);
                args.Handled = true;
                break;
            case Key.I:
                SetPreviewMark(true);
                args.Handled = true;
                break;
            case Key.O:
                SetPreviewMark(false);
                args.Handled = true;
                break;
            case Key.J:
                PausePreview();
                SeekPreview(PreviewPlayer.Position.TotalSeconds - 1);
                args.Handled = true;
                break;
            case Key.K:
                PausePreview();
                args.Handled = true;
                break;
            case Key.L:
                PlayPreview();
                args.Handled = true;
                break;
            case Key.Escape when _previewFullscreen:
                TogglePreviewFullscreen();
                args.Handled = true;
                break;
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

    private void SelectTransition(TransitionSelection? selection)
    {
        if (selection == null)
        {
            _selectedTransitionSelection = null;
            if (_selectedInspectorItem == null) UpdateInspector(null);
            return;
        }

        var transition = selection.Transition;
        if (transition == null && _project.MainSequence != null)
        {
            Execute(new ApplyTransitionCommand
            {
                LeftItemId = selection.LeftItem.Id,
                RightItemId = selection.RightItem.Id,
                Kind = TransitionKind.CrossDissolve,
                Duration = MediaTime.FromSeconds(0.5),
                Alignment = 0.5,
            });
            transition = _project.MainSequence.Transitions.FirstOrDefault(candidate =>
                candidate.LeftItemId == selection.LeftItem.Id && candidate.RightItemId == selection.RightItem.Id);
            _timeline?.SelectTransition(transition, selection.TrackIndex);
        }

        _selectedInspectorItem = null;
        _selectedTransitionSelection = selection with { Transition = transition };
        UpdateTransitionInspector(_selectedTransitionSelection);
    }

    private void UpdateTransitionInspector(TransitionSelection selection)
    {
        var transition = selection.Transition;
        _suppressInspectorChangeTracking = true;
        try
        {
            InspectorPanel.IsEnabled = true;
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorEmptyState.Visibility = Visibility.Collapsed;
            InspectorTabs.SelectedIndex = 0;
            InspectorTitle.Text = transition == null ? "Transition slot" : $"{transition.Kind} transition";
            StatusText.Text = "Selected transition";
            TransitionInspectorCard.Visibility = Visibility.Visible;
            TransitionKindCombo.SelectedItem = transition?.Kind ?? TransitionKind.CrossDissolve;
            TransitionDurationBox.Text = Format(transition?.Duration.Seconds ?? 0.5);
            TransitionAlignmentBox.Text = Format((transition?.Alignment ?? 0.5) * 100);
        }
        finally
        {
            _suppressInspectorChangeTracking = false;
        }
        SetInspectorDirty(false);
    }

    private void UpdateInspector(TimelineItem? item)
    {
        _suppressInspectorChangeTracking = true;
        try
        {
            InspectorPanel.IsEnabled = item != null;
            InspectorPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            InspectorEmptyState.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
            var itemLabel = item == null ? null : GetInspectorItemLabel(item.Kind);
            InspectorTitle.Text = item == null ? "No clip selected" : itemLabel;
            StatusText.Text = item == null ? "Ready" : $"Selected {itemLabel!.ToLowerInvariant()}";
            TransitionInspectorCard.Visibility = Visibility.Collapsed;

            PositionXBox.Text = Format(item?.Transform.PositionX ?? 0);
            PositionYBox.Text = Format(item?.Transform.PositionY ?? 0);
            ScaleBox.Text = Format(item?.Transform.ScaleX ?? 1);
            RotationBox.Text = Format(item?.Transform.RotationDegrees ?? 0);
            OpacityBox.Text = Format((item?.Opacity ?? 1) * 100);
            SpeedBox.Text = Format(item?.SpeedCurve?.ConstantSpeed ?? item?.Speed ?? 1);
            ReverseToggle.IsChecked = item?.Reversed ?? false;
            VolumeBox.Text = Format((item?.Volume ?? 1) * 100);
            PanBox.Text = Format((item?.Pan ?? 0) * 100);

            var color = item?.ColorCorrection;
            var brightness = color?.Brightness ?? 0;
            var contrast = color?.Contrast ?? 0;
            var saturation = color?.Saturation ?? 1;
            BrightnessSlider.Value = brightness;
            ContrastSlider.Value = contrast;
            SaturationSlider.Value = saturation;
            BrightnessBox.Text = Format(brightness);
            ContrastBox.Text = Format(contrast);
            SaturationBox.Text = Format(saturation);
            BlackWhiteToggle.IsChecked = color?.BlackAndWhite ?? false;
            StabilizeToggle.IsChecked = item?.Stabilization?.Enabled ?? false;

            AddEffectButton.IsEnabled = item != null && EffectCombo.SelectedItem != null;
            AnalyzeStabilizationButton.IsEnabled = item?.MediaAssetId != null && !_isMediaOperationRunning;
            var selectedEffectId = (EffectList.SelectedItem as EffectListEntry)?.Effect.Id;
            EffectList.Items.Clear();
            if (item != null)
            {
                foreach (var effect in item.Effects)
                {
                    var definition = _effectRegistry.Get(effect.EffectTypeId);
                    EffectList.Items.Add(new EffectListEntry(
                        effect,
                        definition?.Name ?? effect.EffectTypeId,
                        definition?.Category ?? "custom"));
                }
            }
            if (selectedEffectId.HasValue)
            {
                EffectList.SelectedItem = EffectList.Items
                    .OfType<EffectListEntry>()
                    .FirstOrDefault(entry => entry.Effect.Id == selectedEffectId.Value);
            }
            UpdateSelectedEffectEditor();
        }
        finally
        {
            _suppressInspectorChangeTracking = false;
        }
        SetInspectorDirty(false);
        CommandManager.InvalidateRequerySuggested();
    }

    private static string GetInspectorItemLabel(ItemKind kind) => kind switch
    {
        ItemKind.Clip => "Media clip",
        ItemKind.Text => "Text clip",
        ItemKind.Image => "Image clip",
        ItemKind.Sticker => "Sticker",
        ItemKind.AdjustmentLayer => "Adjustment layer",
        _ => $"{kind} item",
    };

    private void ApplyInspectorSettings()
    {
        if (_selectedTransitionSelection != null)
        {
            ApplyTransitionInspectorSettings(_selectedTransitionSelection);
            return;
        }

        var item = _selectedInspectorItem;
        if (item == null) return;

        if (!TryReadNumber(PositionXBox, "position X", out var positionX)
            || !TryReadNumber(PositionYBox, "position Y", out var positionY)
            || !TryReadNumber(ScaleBox, "scale", out var scale)
            || !TryReadNumber(RotationBox, "rotation", out var rotation)
            || !TryReadNumber(OpacityBox, "opacity", out var opacityPercent)
            || !TryReadNumber(SpeedBox, "speed", out var speedValue)
            || !TryReadNumber(VolumeBox, "volume", out var volumePercent)
            || !TryReadNumber(PanBox, "pan", out var panPercent)
            || !TryReadNumber(BrightnessBox, "brightness", out var brightness)
            || !TryReadNumber(ContrastBox, "contrast", out var contrast)
            || !TryReadNumber(SaturationBox, "saturation", out var saturation))
            return;

        var transform = new TransformSnapshot(
            positionX,
            positionY,
            Math.Max(0.01, scale),
            rotation);
        var opacity = Math.Clamp(opacityPercent / 100, 0, 1);
        var reversed = ReverseToggle.IsChecked ?? false;
        var speed = Math.Clamp(speedValue, 0.1, 100);
        var volume = Math.Clamp(volumePercent / 100, 0, 4);
        var pan = Math.Clamp(panPercent / 100, -1, 1);
        var color = new ColorCorrection
        {
            Brightness = Math.Clamp(brightness, -1, 1),
            Contrast = Math.Clamp(contrast, -1, 3),
            Saturation = Math.Clamp(saturation, 0, 4),
            BlackAndWhite = BlackWhiteToggle.IsChecked ?? false,
        };
        var stabilization = new StabilizationSettings
        {
            Enabled = StabilizeToggle.IsChecked ?? false,
            Strength = item.Stabilization?.Strength ?? 0.5,
            CropZoomCompensation = item.Stabilization?.CropZoomCompensation ?? true,
            AnalysisComplete = item.Stabilization?.AnalysisComplete ?? false,
        };

        var transformCommand = new SetPropertyCommand
        {
            ItemId = item.Id,
            PropertyName = nameof(TimelineItem.Transform),
            NewValue = transform,
            Getter = i => new TransformSnapshot(i.Transform.PositionX, i.Transform.PositionY, i.Transform.ScaleX, i.Transform.RotationDegrees),
            Setter = (i, v) =>
            {
                if (v is not TransformSnapshot t) return;
                i.Transform.PositionX = t.X;
                i.Transform.PositionY = t.Y;
                i.Transform.ScaleX = t.Scale;
                i.Transform.ScaleY = t.Scale;
                i.Transform.RotationDegrees = t.Rotation;
            },
        };

        Execute(new CompositeEditCommand("Apply clip settings", new IEditCommand[]
        {
            transformCommand,
            SetValue(item, nameof(TimelineItem.Opacity), opacity, i => i.Opacity, (i, v) => i.Opacity = (double)v!),
            SetValue(item, nameof(TimelineItem.Reversed), reversed, i => i.Reversed, (i, v) => i.Reversed = (bool)v!),
            SetValue(item, nameof(TimelineItem.SpeedCurve), new SpeedCurve { ConstantSpeed = speed, PreservePitch = true }, i => i.SpeedCurve, (i, v) => i.SpeedCurve = (SpeedCurve?)v),
            SetValue(item, nameof(TimelineItem.Volume), volume, i => i.Volume, (i, v) => i.Volume = (double)v!),
            SetValue(item, nameof(TimelineItem.Pan), pan, i => i.Pan, (i, v) => i.Pan = (double)v!),
            SetValue(item, nameof(TimelineItem.ColorCorrection), color, i => i.ColorCorrection, (i, v) => i.ColorCorrection = (ColorCorrection?)v),
            SetValue(item, nameof(TimelineItem.Stabilization), stabilization, i => i.Stabilization, (i, v) => i.Stabilization = (StabilizationSettings?)v),
        }));
    }

    private void ApplyTransitionInspectorSettings(TransitionSelection selection)
    {
        var kind = TransitionKindCombo.SelectedItem is TransitionKind selectedKind
            ? selectedKind
            : TransitionKind.CrossDissolve;
        if (!TryReadNumber(TransitionDurationBox, "transition duration", out var durationValue)
            || !TryReadNumber(TransitionAlignmentBox, "transition alignment", out var alignmentPercent))
            return;
        var duration = Math.Clamp(durationValue, 0.05, 10);
        var alignment = Math.Clamp(alignmentPercent / 100, 0, 1);

        Execute(new ApplyTransitionCommand
        {
            LeftItemId = selection.LeftItem.Id,
            RightItemId = selection.RightItem.Id,
            Kind = kind,
            Duration = MediaTime.FromSeconds(duration),
            Alignment = alignment,
        });

        var updated = _project.MainSequence?.Transitions.FirstOrDefault(candidate =>
            candidate.LeftItemId == selection.LeftItem.Id && candidate.RightItemId == selection.RightItem.Id);
        _selectedTransitionSelection = selection with { Transition = updated };
        _timeline?.SelectTransition(updated, selection.TrackIndex);
        if (_selectedTransitionSelection != null) UpdateTransitionInspector(_selectedTransitionSelection);
    }

    private void AddSelectedEffect()
    {
        if (_selectedInspectorItem == null || EffectCombo.SelectedItem is not EffectDefinition effect) return;
        Execute(new AddEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectTypeId = effect.EffectTypeId,
            Parameters = effect.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue),
        });
    }

    private void UpdateSelectedEffectEditor()
    {
        var entry = EffectList.SelectedItem as EffectListEntry;
        var hasSelection = entry != null && _selectedInspectorItem != null;
        EffectRemoveButton.IsEnabled = hasSelection;
        EffectMoveUpButton.IsEnabled = hasSelection && EffectList.SelectedIndex > 0;
        EffectMoveDownButton.IsEnabled = hasSelection && EffectList.SelectedIndex >= 0 && EffectList.SelectedIndex < EffectList.Items.Count - 1;
        EffectToggleButton.IsEnabled = hasSelection;
        EffectDuplicateButton.IsEnabled = hasSelection;
        EffectResetButton.IsEnabled = hasSelection;
        EffectApplyParametersButton.IsEnabled = hasSelection;
        EffectToggleButton.Content = entry?.Effect.Enabled == true ? "Disable" : "Enable";

        _effectParameterEditors.Clear();
        EffectParameterPanel.Children.Clear();
        if (entry == null)
        {
            EffectParameterPanel.Children.Add(new TextBlock
            {
                Text = "Select an applied effect to edit its parameters.",
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null || definition.Parameters.Count == 0)
        {
            EffectParameterPanel.Children.Add(new TextBlock
            {
                Text = "This effect has no editable parameters.",
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
            });
            return;
        }

        foreach (var parameter in definition.Parameters)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = FormatEffectParameterName(parameter.Name),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = entry.Effect.Parameters.TryGetValue(parameter.Name, out var current)
                ? ConvertEffectValue(current, parameter.DefaultValue)
                : Convert.ToDouble(parameter.DefaultValue, CultureInfo.InvariantCulture);
            var editor = new TextBox
            {
                Text = value.ToString("0.###", CultureInfo.InvariantCulture),
                ToolTip = $"Range: {parameter.Min:0.###} to {parameter.Max:0.###}",
                MinHeight = 28,
            };
            Grid.SetColumn(editor, 1);
            grid.Children.Add(label);
            grid.Children.Add(editor);
            EffectParameterPanel.Children.Add(grid);
            _effectParameterEditors[parameter.Name] = editor;
        }
    }

    private void RemoveSelectedEffect()
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new RemoveEffectCommand { ItemId = _selectedInspectorItem.Id, EffectInstanceId = entry.Effect.Id });
    }

    private void MoveSelectedEffect(int offset)
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var newIndex = Math.Clamp(EffectList.SelectedIndex + offset, 0, EffectList.Items.Count - 1);
        Execute(new ReorderEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            NewIndex = newIndex,
        });
        EffectList.SelectedIndex = newIndex;
    }

    private void ToggleSelectedEffect()
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = !entry.Effect.Enabled,
            Parameters = new Dictionary<string, object>(entry.Effect.Parameters),
        });
    }

    private void DuplicateSelectedEffect()
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        Execute(new AddEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectTypeId = entry.Effect.EffectTypeId,
            Parameters = new Dictionary<string, object>(entry.Effect.Parameters),
        });
        EffectList.SelectedIndex = EffectList.Items.Count - 1;
    }

    private void ResetSelectedEffect()
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null) return;
        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = true,
            Parameters = definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue),
        });
    }

    private void ApplySelectedEffectParameters()
    {
        if (_selectedInspectorItem == null || EffectList.SelectedItem is not EffectListEntry entry) return;
        var definition = _effectRegistry.Get(entry.Effect.EffectTypeId);
        if (definition == null) return;

        var parameters = new Dictionary<string, object>();
        foreach (var parameter in definition.Parameters)
        {
            if (!_effectParameterEditors.TryGetValue(parameter.Name, out var editor)
                || !double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                StatusText.Text = $"Invalid value for {FormatEffectParameterName(parameter.Name)}";
                editor?.Focus();
                return;
            }

            var clamped = Math.Clamp(parsed, parameter.Min, parameter.Max);
            parameters[parameter.Name] = parameter.Type.Equals("int", StringComparison.OrdinalIgnoreCase)
                ? (object)(int)Math.Round(clamped)
                : clamped;
        }

        Execute(new UpdateEffectCommand
        {
            ItemId = _selectedInspectorItem.Id,
            EffectInstanceId = entry.Effect.Id,
            Enabled = entry.Effect.Enabled,
            Parameters = parameters,
        });
    }

    private static double ConvertEffectValue(object value, object fallback)
    {
        try
        {
            if (value is JsonElement json && json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var number))
                return number;
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return Convert.ToDouble(fallback, CultureInfo.InvariantCulture);
        }
    }

    private static string FormatEffectParameterName(string name) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' '));

    private async Task AnalyzeSelectedStabilizationAsync()
    {
        var item = _selectedInspectorItem;
        if (item?.MediaAssetId == null) return;
        var asset = _project.MediaLibrary.FirstOrDefault(a => a.Id == item.MediaAssetId.Value);
        if (asset == null) return;

        var current = item.Stabilization;
        var settings = new StabilizationSettings
        {
            Enabled = true,
            Strength = current?.Strength ?? 0.5,
            CropZoomCompensation = current?.CropZoomCompensation ?? true,
            AnalysisComplete = current?.AnalysisComplete ?? false,
        };
        AddRenderQueueMessage($"Stabilization: analyzing {Path.GetFileName(asset.OriginalPath)}");
        SetMediaOperationState(true, $"Analyzing motion in {Path.GetFileName(asset.OriginalPath)}…");
        try
        {
            await _stabilizationService.AnalyzeAsync(asset, settings);
            Execute(SetValue(item, nameof(TimelineItem.Stabilization), settings, i => i.Stabilization, (i, v) => i.Stabilization = (StabilizationSettings?)v));
            AddRenderQueueMessage("Stabilization: analysis complete");
        }
        catch (Exception ex)
        {
            AddRenderQueueMessage($"Stabilization failed: {ex.Message}");
        }
        finally
        {
            SetMediaOperationState(false, "Stabilization analysis finished");
        }
    }

    private static SetPropertyCommand SetValue<T>(TimelineItem item, string propertyName, T value, Func<TimelineItem, T> getter, Action<TimelineItem, object?> setter) =>
        new()
        {
            ItemId = item.Id,
            PropertyName = propertyName,
            NewValue = value,
            Getter = i => getter(i),
            Setter = setter,
        };

    private static double ParseDouble(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record TransformSnapshot(double X, double Y, double Scale, double Rotation);

    private sealed record AgentAuditEntry(
        DateTimeOffset TimestampUtc,
        string Action,
        string Summary,
        bool Success,
        string? Error);

    private sealed record AgentEditBuildResult(bool Success, IEditCommand? Command, string Summary, string? Error)
    {
        public static AgentEditBuildResult Ok(IEditCommand command, string summary) =>
            new(true, command, summary, null);

        public static AgentEditBuildResult Fail(string error) =>
            new(false, null, string.Empty, error);
    }

    private static MediaKind GetMediaKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" or ".wav" or ".aac" or ".m4a" or ".flac" => MediaKind.Audio,
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => MediaKind.Image,
        ".srt" or ".vtt" => MediaKind.Subtitle,
        ".ttf" or ".otf" => MediaKind.Font,
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => MediaKind.Video,
        _ => MediaKind.Other,
    };

    private MediaListItem CreateMediaListItem(MediaAsset asset)
    {
        var previewPath = asset.Kind switch
        {
            MediaKind.Image => asset.OriginalPath,
            MediaKind.Video => Path.Combine(_appData, "Cache", "thumbnails", $"{asset.Id}.jpg"),
            MediaKind.Audio => Path.Combine(_appData, "Cache", "waveforms", $"{asset.Id}.png"),
            _ => string.Empty,
        };

        return new MediaListItem(
            asset,
            LoadThumbnail(previewPath),
            asset.Kind switch
            {
                MediaKind.Video => "VID",
                MediaKind.Audio => "AUD",
                MediaKind.Image => "IMG",
                MediaKind.Subtitle => "CC",
                MediaKind.Font => "FONT",
                _ => "FILE",
            },
            FormatDuration(asset.Duration));
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 240;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

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

    private sealed record MediaListItem(MediaAsset Asset, ImageSource? Thumbnail, string FallbackGlyph, string DurationText)
    {
        public string FileName => Path.GetFileName(Asset.OriginalPath);
        public string KindText => Asset.Kind.ToString().ToUpperInvariant();
        public string FolderPath => Path.GetDirectoryName(Asset.OriginalPath) ?? string.Empty;
        public bool HasDuration => !string.IsNullOrEmpty(DurationText);
    }
}
