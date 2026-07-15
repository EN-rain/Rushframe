using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rushframe.Desktop.Commands;
using Rushframe.Desktop.Panels;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private sealed record FunctionSearchItem(
        string Title,
        string Category,
        string Keywords,
        Action Execute,
        Func<bool>? CanExecute = null,
        string? Shortcut = null)
    {
        public bool IsAvailable
        {
            get
            {
                try
                {
                    return CanExecute?.Invoke() ?? true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    private readonly List<FunctionSearchItem> _functionSearchItems = [];

    private void InitializeGlobalFunctionSearch()
    {
        AddRoutedFunction("New Project", "Project", "create blank new project", EditorCommands.NewProject, "Ctrl+N");
        AddRoutedFunction("Open Project", "Project", "load project file", EditorCommands.OpenProject, "Ctrl+O");
        AddRoutedFunction("Save Project", "Project", "save current project", EditorCommands.SaveProject, "Ctrl+S");
        AddRoutedFunction("Import Media", "Media", "add video image audio files", EditorCommands.ImportMedia, "Ctrl+I");
        AddRoutedFunction("Relink Selected Media", "Media", "offline missing replace source", EditorCommands.RelinkMedia);
        AddRoutedFunction("Generate Media Cache", "Media", "thumbnail proxy waveform cache", EditorCommands.GenerateMediaCache);
        AddRoutedFunction("Extract Audio", "Media", "separate audio from video", EditorCommands.ExtractAudio);
        AddRoutedFunction("Import AI Analysis", "AI", "media intelligence analysis json captions transcript scenes", EditorCommands.ImportMediaIntelligence);
        AddRoutedFunction("Export Project", "Export", "render output video portrait landscape custom", EditorCommands.Render, "Ctrl+R");
        AddRoutedFunction("Add Text", "Timeline", "title caption text layer", EditorCommands.AddText, "Ctrl+T");
        AddRoutedFunction("Add Marker", "Timeline", "timeline marker note", EditorCommands.AddMarker);
        AddRoutedFunction("Split Clip", "Timeline", "cut clip at playhead", EditorCommands.SplitClip, "Ctrl+B");
        AddRoutedFunction("Cut", "Edit", "cut selected clip", EditorCommands.Cut, "Ctrl+X");
        AddRoutedFunction("Copy", "Edit", "copy selected clip", EditorCommands.Copy, "Ctrl+C");
        AddRoutedFunction("Paste", "Edit", "paste clip at playhead", EditorCommands.Paste, "Ctrl+V");
        AddRoutedFunction("Duplicate", "Edit", "duplicate selected clip", EditorCommands.Duplicate, "Ctrl+D");
        AddRoutedFunction("Delete Clip", "Edit", "remove selected item", EditorCommands.DeleteClip, "Delete");
        AddRoutedFunction("Ripple Delete", "Edit", "remove clip and close gap", EditorCommands.RippleDelete);
        AddRoutedFunction("Undo", "Edit", "undo last change", EditorCommands.Undo, "Ctrl+Z");
        AddRoutedFunction("Redo", "Edit", "redo last change", EditorCommands.Redo, "Ctrl+Y");
        AddRoutedFunction("Settings", "Application", "preferences editor configuration", EditorCommands.Settings, "Ctrl+,");
        AddFunction("Keyboard Shortcuts", "Application", "configure remap keybindings hotkeys", ShowKeyboardShortcutsDialog);
        AddRoutedFunction("Zoom In UI", "View", "increase interface scale", EditorCommands.ZoomIn, "Ctrl++");
        AddRoutedFunction("Zoom Out UI", "View", "decrease interface scale", EditorCommands.ZoomOut, "Ctrl+-");
        AddRoutedFunction("Reset UI Zoom", "View", "restore interface scale", EditorCommands.ResetZoom);

        AddFunction("Play Preview", "Preview", "play resume video audio", () => _ = PlayPreviewAsync(), () => PreviewPlayButton.IsEnabled, "Space");
        AddFunction("Pause Preview", "Preview", "pause video audio", PausePreview, () => PreviewPauseButton.IsEnabled, "Space");
        AddFunction("Stop Preview", "Preview", "stop reset video audio", StopPreview, () => PreviewStopButton.IsEnabled);
        AddFunction("Previous Frame", "Preview", "step backward one frame", () => StepPreviewFrame(-1), () => PreviewPreviousFrameButton.IsEnabled);
        AddFunction("Next Frame", "Preview", "step forward one frame", () => StepPreviewFrame(1), () => PreviewNextFrameButton.IsEnabled);
        AddFunction("Set Mark In", "Preview", "source range start in point", () => SetPreviewMark(true), () => PreviewMarkInButton.IsEnabled);
        AddFunction("Set Mark Out", "Preview", "source range end out point", () => SetPreviewMark(false), () => PreviewMarkOutButton.IsEnabled);
        AddFunction("Clear Preview Marks", "Preview", "remove in out source range", () => ClearPreviewMarks(), () => PreviewClearMarksButton.IsEnabled);
        AddFunction("Insert Preview Range", "Preview", "insert marked source into timeline", () => AddPreviewRangeToTimeline(false), () => PreviewInsertButton.IsEnabled);
        AddFunction("Overwrite Preview Range", "Preview", "overwrite timeline with marked source", () => AddPreviewRangeToTimeline(true), () => PreviewOverwriteButton.IsEnabled);
        AddFunction("Save Preview Snapshot", "Preview", "capture current frame png image", async () => await SavePreviewSnapshotAsync(), () => PreviewSnapshotButton.IsEnabled);
        AddFunction("Toggle Preview Fullscreen", "Preview", "fullscreen monitor", TogglePreviewFullscreen, () => PreviewFullscreenButton.IsEnabled);
        AddFunction("Canvas & Layout Guides", "Preview", "resolution aspect frame rate background tiktok reels shorts grid safe area", OpenCanvasSettings);

        AddFunction("Add Selected Media to Timeline", "Media", "place selected asset at playhead", async () => await AddSelectedMediaToTimelineAsync(), () => AddToTimelineButton.IsEnabled);
        AddFunction("Preview Selected Media", "Media", "open selected asset in source monitor", PreviewSelectedMedia, () => PreviewSelectedMediaButton.IsEnabled);
        AddFunction("Creative Asset Library", "Media", "licensed local stickers shapes fonts sounds music extensions packs", async () => await ShowCreativeAssetsAsync());
        AddFunction("Run AI Analysis", "AI", "media intelligence analyze selected media scenes transcript hooks", async () => await RunMediaIntelligenceAsync(), () => RunMediaIntelligenceButton.IsEnabled);
        AddFunction("Apply AI Analysis", "AI", "media intelligence apply analysis to timeline", async () => await ApplyCurrentMediaIntelligenceToTimelineAsync(), () => ApplyMediaIntelligenceButton.IsEnabled);
        AddFunction("Search AI Context", "AI", "media intelligence semantic context search", () => SearchMediaContext(false), () => SearchMediaContextButton.IsEnabled);
        AddFunction("Find Hooks", "AI", "media intelligence find engaging moments", () => SearchMediaContext(true), () => FindHooksButton.IsEnabled);
        AddFunction("Open Local Agent Setup", "Agent / MCP", "agent bridge mcp connection status", () => McpStatusButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

        foreach (var panel in PanelRegistry.All)
        {
            var capturedPanel = panel;
            if (!capturedPanel.CanClose) continue;
            AddFunction(
                $"Toggle {capturedPanel.Title} Panel",
                "Panels",
                $"show hide open close {capturedPanel.Title}",
                () => TogglePanel(capturedPanel.Id));
        }

        GlobalFunctionSearchButton.Click += (_, _) => FocusGlobalFunctionSearch();
        GlobalFunctionSearchBackdrop.Click += (_, _) => CloseGlobalFunctionSearch(clearQuery: true);
        GlobalFunctionSearchBox.TextChanged += (_, _) => RefreshGlobalFunctionSearch();
        GlobalFunctionSearchBox.PreviewKeyDown += GlobalFunctionSearchBox_OnPreviewKeyDown;
        GlobalFunctionSearchResults.MouseLeftButtonUp += (_, _) => ExecuteSelectedGlobalFunction();
        GlobalFunctionSearchResults.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            ExecuteSelectedGlobalFunction();
            args.Handled = true;
        };
        PreviewKeyDown += MainWindow_OnGlobalFunctionSearchKeyDown;
        Deactivated += (_, _) => CloseGlobalFunctionSearch(clearQuery: true);
    }

    private void AddRoutedFunction(
        string title,
        string category,
        string keywords,
        RoutedCommand command,
        string? shortcut = null)
    {
        AddFunction(
            title,
            category,
            keywords,
            () => command.Execute(null, this),
            () => command.CanExecute(null, this),
            shortcut);
    }

    private void AddFunction(
        string title,
        string category,
        string keywords,
        Action execute,
        Func<bool>? canExecute = null,
        string? shortcut = null)
    {
        _functionSearchItems.Add(new FunctionSearchItem(title, category, keywords, execute, canExecute, shortcut));
    }

    private void MainWindow_OnGlobalFunctionSearchKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.K || Keyboard.Modifiers != ModifierKeys.Control) return;
        FocusGlobalFunctionSearch();
        args.Handled = true;
    }

    private void FocusGlobalFunctionSearch()
    {
        GlobalFunctionSearchOverlay.Visibility = Visibility.Visible;
        GlobalFunctionSearchBox.Focus();
        GlobalFunctionSearchBox.SelectAll();
        RefreshGlobalFunctionSearch(showAllWhenEmpty: true);
    }

    private void RefreshGlobalFunctionSearch(bool showAllWhenEmpty = false)
    {
        if (GlobalFunctionSearchOverlay.Visibility != Visibility.Visible)
            return;

        try
        {
            var query = GlobalFunctionSearchBox.Text.Trim();
            GlobalFunctionSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (query.Length == 0 && !showAllWhenEmpty)
                showAllWhenEmpty = true;

            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var matches = _functionSearchItems
                .Select(item => new
                {
                    Item = item,
                    Score = ScoreFunctionSearchItem(item, terms),
                    Available = item.IsAvailable,
                })
                .Where(result => query.Length == 0 || result.Score > 0)
                .OrderByDescending(result => result.Available)
                .ThenByDescending(result => result.Score)
                .ThenBy(result => result.Item.Category)
                .ThenBy(result => result.Item.Title)
                .Take(12)
                .Select(result => result.Item)
                .ToList();

            GlobalFunctionSearchResults.ItemsSource = matches;
            GlobalFunctionSearchResults.SelectedIndex = -1;
            GlobalFunctionSearchEmptyText.Text = "No matching commands";
            GlobalFunctionSearchEmptyText.Visibility = matches.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            GlobalFunctionSearchResults.Visibility = matches.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        catch
        {
            GlobalFunctionSearchResults.ItemsSource = Array.Empty<FunctionSearchItem>();
            GlobalFunctionSearchResults.Visibility = Visibility.Collapsed;
            GlobalFunctionSearchEmptyText.Text = "Search unavailable";
            GlobalFunctionSearchEmptyText.Visibility = Visibility.Visible;
        }
    }

    private static int ScoreFunctionSearchItem(FunctionSearchItem item, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return 1;

        var title = item.Title.ToLowerInvariant();
        var category = item.Category.ToLowerInvariant();
        var keywords = item.Keywords.ToLowerInvariant();
        var score = 0;

        foreach (var rawTerm in terms)
        {
            var term = rawTerm.ToLowerInvariant();
            if (title.StartsWith(term, StringComparison.Ordinal)) score += 10;
            else if (title.Contains(term, StringComparison.Ordinal)) score += 6;
            else if (category.Contains(term, StringComparison.Ordinal)) score += 3;
            else if (keywords.Contains(term, StringComparison.Ordinal)) score += 2;
            else return 0;
        }

        return score;
    }

    private void GlobalFunctionSearchBox_OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Down:
                if (GlobalFunctionSearchResults.Items.Count > 0)
                {
                    GlobalFunctionSearchResults.SelectedIndex = Math.Min(
                        GlobalFunctionSearchResults.Items.Count - 1,
                        GlobalFunctionSearchResults.SelectedIndex + 1);
                    GlobalFunctionSearchResults.ScrollIntoView(GlobalFunctionSearchResults.SelectedItem);
                }
                args.Handled = true;
                break;

            case Key.Up:
                if (GlobalFunctionSearchResults.Items.Count > 0)
                {
                    GlobalFunctionSearchResults.SelectedIndex = Math.Max(0, GlobalFunctionSearchResults.SelectedIndex - 1);
                    GlobalFunctionSearchResults.ScrollIntoView(GlobalFunctionSearchResults.SelectedItem);
                }
                args.Handled = true;
                break;

            case Key.Enter:
                if (GlobalFunctionSearchResults.SelectedItem == null && GlobalFunctionSearchResults.Items.Count > 0)
                    GlobalFunctionSearchResults.SelectedIndex = 0;
                ExecuteSelectedGlobalFunction();
                args.Handled = true;
                break;

            case Key.Escape:
                CloseGlobalFunctionSearch(clearQuery: true);
                Keyboard.ClearFocus();
                args.Handled = true;
                break;
        }
    }

    private void ExecuteSelectedGlobalFunction()
    {
        if (GlobalFunctionSearchResults.SelectedItem is not FunctionSearchItem item || !item.IsAvailable)
            return;

        CloseGlobalFunctionSearch(clearQuery: true);
        try
        {
            item.Execute();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Command failed: {ex.Message}";
        }
    }

    private void CloseGlobalFunctionSearch(bool clearQuery = false)
    {
        GlobalFunctionSearchOverlay.Visibility = Visibility.Collapsed;
        GlobalFunctionSearchResults.SelectedIndex = -1;
        GlobalFunctionSearchEmptyText.Visibility = Visibility.Collapsed;
        if (clearQuery && GlobalFunctionSearchBox.Text.Length > 0)
            GlobalFunctionSearchBox.Clear();
        Focus();
    }
}
