using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Rushframe.Desktop.Panels;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Workspace;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private const string AllProjectFoldersLabel = "All folders";
    private static readonly PanelId UtilityWindowPanelId = new("utilityWindow");

    private readonly Dictionary<TabItem, string> _inspectorTabTitles = [];
    private string? _mediaFolderFilter;
    private bool _previewWindowFree;
    private bool _utilityWindowSeparate;
    private PanelGridArea _utilityWindowArea = PanelGridArea.Empty;
    private (int Column, int Row)? _utilityWindowPreferredCell;
    private TabControl? _utilityTabHost;
    private IReadOnlyDictionary<PanelId, PanelGridArea> _effectivePrimaryAreas =
        new Dictionary<PanelId, PanelGridArea>();

    private void InitializeInspectorUtilityTabs()
    {
        RightPanelHost.Children.Remove(TasksBorder);
        UtilityWindowContentHost.Content = TasksBorder;
        _utilityTabHost = UtilityTabs;
        TasksBorder.Visibility = Visibility.Collapsed;
        UtilityWindowHost.Visibility = Visibility.Collapsed;
        RightTasksRow.Height = new GridLength(0);

        ConfigureClosableInspectorTabs();
        InspectorTabs.SelectionChanged += (_, _) => UpdateInspectorFooterVisibility();
        UtilityTabs.SelectionChanged += (_, _) => UpdateUtilityWindowTitle();
        InspectorFieldColumnsCombo.SelectionChanged += (_, _) => UpdateInspectorFieldColumns();
        ProjectFolderFilterCombo.SelectionChanged += (_, _) =>
        {
            _mediaFolderFilter = ProjectFolderFilterCombo.SelectedItem is ProjectFolderOption option
                ? option.Path
                : null;
            RefreshMediaView();
        };
        PreviewFreeLayoutButton.Click += (_, _) => TogglePreviewFreeLayout();
        MediaCloseWindowButton.Click += (_, _) => TogglePanel(PanelId.Media);
        UtilityCloseWindowButton.Click += (_, _) => CloseAllUtilityPanels();
        MediaWindowTitleBar.MouseRightButtonUp += (_, args) =>
        {
            ShowPanelTitleMenu(MediaWindowTitleBar, PanelId.Media, "Project Files");
            args.Handled = true;
        };
        InspectorWindowTitleBar.MouseRightButtonUp += (_, args) =>
        {
            ShowInspectorTitleMenu();
            args.Handled = true;
        };
        UtilityWindowTitleBar.MouseRightButtonUp += (_, args) =>
        {
            ShowUtilityTitleMenu();
            args.Handled = true;
        };
        ProjectNameText.MouseLeftButtonDown += (_, args) =>
        {
            BeginProjectNameEdit();
            args.Handled = true;
        };
        ProjectNameEditBox.KeyDown += ProjectNameEditBox_KeyDown;
        ProjectNameEditBox.LostKeyboardFocus += (_, _) =>
        {
            if (ProjectNameEditBox.Visibility == Visibility.Visible)
                CommitProjectNameEdit();
        };

        UpdateInspectorFieldColumns();
        UpdateInspectorFooterVisibility();
        RefreshProjectFolderFilters();
    }

    private void ConfigureClosableInspectorTabs()
    {
        ConfigureClosableTab(PropertiesInspectorTab, "Properties");
        ConfigureClosableTab(EffectsInspectorTab, "Effects");
        ConfigureClosableTab(AudioInspectorTab, "Audio");
        foreach (var entry in GetUtilityPanelEntries())
            ConfigureClosableTab(entry.Tab, GetUtilityDefaultTitle(entry.Id));
    }

    private void ConfigureClosableTab(TabItem tab, string title)
    {
        _inspectorTabTitles[tab] = title;
        AutomationProperties.SetName(tab, title);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        tab.Header = header;

        var closeItem = new MenuItem { Header = "Close" };
        closeItem.Click += (_, args) =>
        {
            CloseInspectorTab(tab);
            args.Handled = true;
        };
        tab.ContextMenu = new ContextMenu();
        tab.ContextMenu.Items.Add(closeItem);
    }

    private void CloseInspectorTab(TabItem tab)
    {
        var utility = GetUtilityPanelEntries().FirstOrDefault(entry => ReferenceEquals(entry.Tab, tab));
        if (utility.Tab != null)
        {
            if (_layout.IsPanelOpen(utility.Id))
                TogglePanel(utility.Id);
            return;
        }

        tab.Visibility = Visibility.Collapsed;
        SelectFirstVisibleInspectorTab();
        UpdateInspectorFooterVisibility();
    }

    private void ShowInspectorTabMenu()
    {
        var hiddenCoreTab = new[] { PropertiesInspectorTab, EffectsInspectorTab, AudioInspectorTab }
            .FirstOrDefault(tab => tab.Visibility != Visibility.Visible);
        if (hiddenCoreTab != null)
        {
            hiddenCoreTab.Visibility = Visibility.Visible;
            hiddenCoreTab.IsSelected = true;
            StatusText.Text = $"Added {GetTabTitle(hiddenCoreTab)} Inspector tab";
            return;
        }

        var closedUtility = GetUtilityPanelEntries().FirstOrDefault(entry => !_layout.IsPanelOpen(entry.Id));
        if (closedUtility.Tab != null)
        {
            TogglePanel(closedUtility.Id);
            StatusText.Text = $"Added {GetTabTitle(closedUtility.Tab)} Inspector tab";
            return;
        }

        StatusText.Text = "All Inspector tabs are already open";
    }

    private void ShowPanelTitleMenu(FrameworkElement target, PanelId panelId, string title)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
        };
        var remove = new MenuItem { Header = $"Remove {title} window" };
        remove.Click += (_, _) => TogglePanel(panelId);
        menu.Items.Add(remove);

        AddClosedPrimaryPanelMenuItem(menu, PanelId.Media, "Project Files");
        AddClosedPrimaryPanelMenuItem(menu, PanelId.Inspector, "Inspector");
        foreach (var hiddenTab in new[] { PropertiesInspectorTab, EffectsInspectorTab, AudioInspectorTab }
                     .Where(tab => tab.Visibility != Visibility.Visible))
        {
            var capturedTab = hiddenTab;
            var addTab = new MenuItem { Header = $"Add {GetTabTitle(capturedTab)} tab" };
            addTab.Click += (_, _) =>
            {
                capturedTab.Visibility = Visibility.Visible;
                capturedTab.IsSelected = true;
            };
            menu.Items.Add(addTab);
        }
        foreach (var entry in GetUtilityPanelEntries())
        {
            if (_layout.IsPanelOpen(entry.Id)) continue;
            var captured = entry;
            var item = new MenuItem { Header = $"Add {GetTabTitle(entry.Tab)}" };
            item.Click += (_, _) => TogglePanel(captured.Id);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddClosedPrimaryPanelMenuItem(ContextMenu menu, PanelId panelId, string title)
    {
        if (_layout.IsPanelOpen(panelId)) return;
        var item = new MenuItem { Header = $"Add {title}" };
        item.Click += (_, _) => TogglePanel(panelId);
        menu.Items.Add(item);
    }

    private void ShowInspectorTitleMenu()
    {
        ShowPanelTitleMenu(InspectorWindowTitleBar, PanelId.Inspector, "Inspector");
    }

    private void ShowUtilityTitleMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = UtilityWindowTitleBar,
            Placement = PlacementMode.Bottom,
        };
        var remove = new MenuItem { Header = "Remove utility window" };
        remove.Click += (_, _) => CloseAllUtilityPanels();
        menu.Items.Add(remove);
        AddClosedPrimaryPanelMenuItem(menu, PanelId.Media, "Project Files");
        AddClosedPrimaryPanelMenuItem(menu, PanelId.Inspector, "Inspector");
        foreach (var entry in GetUtilityPanelEntries())
        {
            if (_layout.IsPanelOpen(entry.Id)) continue;
            var captured = entry;
            var item = new MenuItem { Header = $"Add {GetTabTitle(entry.Tab)}" };
            item.Click += (_, _) => TogglePanel(captured.Id);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void CloseAllUtilityPanels()
    {
        var open = GetUtilityPanelEntries()
            .Where(entry => _layout.IsPanelOpen(entry.Id))
            .Select(entry => entry.Id)
            .ToArray();
        if (open.Length == 0) return;

        foreach (var panelId in open)
            _layout = _layout.WithPanelToggled(panelId, false);
        SynchronizePanelMenuChecks();
        ApplyLayout();
        SaveLayout();
        StatusText.Text = "Utility window closed";
    }

    private void SynchronizePanelMenuChecks()
    {
        foreach (var item in PanelsMenu.Items.OfType<MenuItem>())
            if (item.Tag is PanelId id)
                item.IsChecked = PanelRegistry.Find(id)?.CanClose == false || _layout.IsPanelOpen(id);
    }

    private void BeginProjectNameEdit()
    {
        ProjectNameEditBox.Text = _project.Name;
        ProjectNameText.Visibility = Visibility.Collapsed;
        ProjectNameEditBox.Visibility = Visibility.Visible;
        ProjectNameEditBox.Focus();
        ProjectNameEditBox.SelectAll();
    }

    private void ProjectNameEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitProjectNameEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelProjectNameEdit();
            e.Handled = true;
        }
    }

    private void CommitProjectNameEdit()
    {
        var name = ProjectNameEditBox.Text.Trim();
        if (name.Length is 0 or > 120 || name.Any(char.IsControl))
        {
            StatusText.Text = "Project name must contain 1 to 120 visible characters";
            ProjectNameEditBox.Focus();
            ProjectNameEditBox.SelectAll();
            return;
        }

        if (!string.Equals(name, _project.Name, StringComparison.Ordinal))
        {
            using var mutation = _saveCoordinator.BeginMutation();
            _project.Name = name;
            _project.IncrementRevision();
            if (_timeline != null) _timeline.ProjectRevision = _project.Revision;
            MarkProjectDirty("Project renamed");
        }
        ProjectNameText.Text = name;
        ProjectNameEditBox.Visibility = Visibility.Collapsed;
        ProjectNameText.Visibility = Visibility.Visible;
    }

    private void CancelProjectNameEdit()
    {
        ProjectNameEditBox.Visibility = Visibility.Collapsed;
        ProjectNameText.Visibility = Visibility.Visible;
        ProjectNameEditBox.Text = _project.Name;
    }

    private void UpdateInspectorFooterVisibility()
    {
        var showFooter = InspectorTabs.SelectedItem is TabItem selected
            && (ReferenceEquals(selected, PropertiesInspectorTab)
                || ReferenceEquals(selected, EffectsInspectorTab)
                || ReferenceEquals(selected, AudioInspectorTab));
        InspectorFooterBorder.Visibility = showFooter ? Visibility.Visible : Visibility.Collapsed;
        InspectorFooterRow.Height = showFooter ? new GridLength(52) : new GridLength(0);
    }

    private void UpdateInspectorFieldColumns()
    {
        var columns = InspectorFieldColumnsCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && int.TryParse(tag, out var parsed)
                ? parsed
                : 2;
        TransformFieldsGrid.Columns = Math.Clamp(columns, 1, 3);
    }

    private void RefreshProjectFolderFilters()
    {
        var previous = _mediaFolderFilter;
        var folders = _project.MediaLibrary
            .Select(asset => Path.GetDirectoryName(asset.OriginalPath) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProjectFolderOption(path))
            .ToArray();

        ProjectFolderFilterCombo.Items.Clear();
        ProjectFolderFilterCombo.Items.Add(new ProjectFolderOption(null));
        foreach (var folder in folders)
            ProjectFolderFilterCombo.Items.Add(folder);

        ProjectFolderFilterCombo.SelectedItem = ProjectFolderFilterCombo.Items
            .OfType<ProjectFolderOption>()
            .FirstOrDefault(option => string.Equals(option.Path, previous, StringComparison.OrdinalIgnoreCase))
            ?? ProjectFolderFilterCombo.Items[0];
    }

    private void TogglePreviewFreeLayout()
    {
        _previewWindowFree = !_previewWindowFree;
        if (_previewWindowFree)
            _previewWindowPortrait = false;

        UpdatePreviewOrientationButton();
        ApplyLayout();
        StatusText.Text = _previewWindowFree
            ? "Preview uses free adaptive grid sizing"
            : "Preview returned to landscape grid sizing";
    }

    private bool IsUtilityPanel(PanelId panelId) =>
        panelId == PanelId.RenderQueue
        || panelId == PanelId.MediaIntelligence
        || panelId == PanelId.ProductionWorkflow
        || panelId == PanelId.TranscriptEditor
        || panelId == PanelId.OutputVariants
        || panelId == PanelId.GeneratedCompositions;

    private TabItem? GetUtilityInspectorTab(PanelId panelId) =>
        panelId == PanelId.RenderQueue ? RenderQueueTab
        : panelId == PanelId.MediaIntelligence ? MediaIntelligenceTab
        : panelId == PanelId.ProductionWorkflow ? ProductionWorkflowTab
        : panelId == PanelId.TranscriptEditor ? TranscriptEditorTab
        : panelId == PanelId.OutputVariants ? OutputVariantsTab
        : panelId == PanelId.GeneratedCompositions ? GeneratedCompositionsTab
        : null;

    private (PanelId Id, TabItem Tab)[] GetUtilityPanelEntries() =>
    [
        (PanelId.RenderQueue, RenderQueueTab),
        (PanelId.MediaIntelligence, MediaIntelligenceTab),
        (PanelId.ProductionWorkflow, ProductionWorkflowTab),
        (PanelId.TranscriptEditor, TranscriptEditorTab),
        (PanelId.OutputVariants, OutputVariantsTab),
        (PanelId.GeneratedCompositions, GeneratedCompositionsTab),
    ];

    private static string GetUtilityDefaultTitle(PanelId panelId) =>
        panelId == PanelId.RenderQueue ? "Queue"
        : panelId == PanelId.MediaIntelligence ? "AI"
        : panelId == PanelId.ProductionWorkflow ? "Workflow"
        : panelId == PanelId.TranscriptEditor ? "Transcript"
        : panelId == PanelId.OutputVariants ? "Variants + QA"
        : panelId == PanelId.GeneratedCompositions ? "Compositions"
        : "Utility";

    private string GetTabTitle(TabItem tab) =>
        _inspectorTabTitles.TryGetValue(tab, out var title) ? title : "Tab";

    private void UpdateUtilityPanelHosting(
        bool portrait,
        bool mediaOpen,
        bool previewOpen,
        bool inspectorOpen,
        bool activityOpen)
    {
        var visiblePrimaryPanels = GetVisiblePrimaryPanels(mediaOpen, previewOpen, inspectorOpen);

        var useSeparateWindow = TryResolveSeparateUtilityArea(
            portrait,
            visiblePrimaryPanels,
            activityOpen,
            out var utilityArea);

        if (useSeparateWindow)
        {
            EnsureUtilityTabsHostedBy(UtilityTabs);
            _utilityWindowSeparate = true;
            _utilityWindowArea = utilityArea;
            PlaceUtilityWindowInGrid(utilityArea);
            UtilityWindowHost.Visibility = Visibility.Visible;
            TasksBorder.Visibility = Visibility.Visible;
            SelectFirstVisibleUtilityTab();
            UpdateUtilityWindowTitle();
            return;
        }

        EnsureUtilityTabsHostedBy(InspectorTabs);
        _utilityWindowSeparate = false;
        _utilityWindowArea = PanelGridArea.Empty;
        UtilityWindowHost.Visibility = Visibility.Collapsed;
        TasksBorder.Visibility = Visibility.Collapsed;
    }

    private bool CanClosePrimaryPanelWithoutEmptyCells(PanelId panelId)
    {
        if (panelId != PanelId.Media && panelId != PanelId.Preview && panelId != PanelId.Inspector)
            return true;

        var mediaOpen = panelId != PanelId.Media && _layout.IsPanelOpen(PanelId.Media);
        var previewOpen = panelId != PanelId.Preview && _layout.IsPanelOpen(PanelId.Preview);
        var inspectorOpen = panelId != PanelId.Inspector && _layout.IsPanelOpen(PanelId.Inspector);
        var visible = GetVisiblePrimaryPanels(mediaOpen, previewOpen, inspectorOpen);
        var activityOpen = GetUtilityPanelEntries().Any(entry => _layout.IsPanelOpen(entry.Id));
        var reserved = TryResolveSeparateUtilityArea(
            _previewWindowPortrait,
            visible,
            activityOpen,
            out var utilityArea)
                ? utilityArea
                : PanelGridArea.Empty;
        if (panelId == PanelId.Inspector && activityOpen && !reserved.IsWithinGrid)
            return false;

        var resolved = WorkspaceVisibleLayoutService.Resolve(
            _layout.GetGridAreas(_previewWindowPortrait),
            visible,
            _previewWindowPortrait,
            reserved);
        if (WorkspaceVisibleLayoutService.HasOverlaps(resolved, reserved))
            return false;

        var occupied = resolved.Values.SelectMany(area => area.Cells()).ToHashSet();
        foreach (var cell in reserved.IsWithinGrid ? reserved.Cells() : [])
            occupied.Add(cell);
        return occupied.Count == WorkspaceLayout.GridColumns * WorkspaceLayout.GridRows;
    }

    private bool TryResolveSeparateUtilityArea(
        bool portrait,
        IReadOnlySet<PanelId> visiblePrimaryPanels,
        bool activityOpen,
        out PanelGridArea utilityArea,
        (int Column, int Row)? preferredCell = null)
    {
        utilityArea = PanelGridArea.Empty;
        if (!activityOpen) return false;

        var storedAreas = _layout.GetGridAreas(portrait);
        if (!WorkspaceUtilityPlacementService.TryFindArea(
                storedAreas,
                visiblePrimaryPanels,
                portrait,
                preferredCell ?? _utilityWindowPreferredCell,
                out var candidate))
            return false;

        var resolvedPrimaryAreas = WorkspaceVisibleLayoutService.Resolve(
            storedAreas,
            visiblePrimaryPanels,
            portrait,
            candidate);
        if (WorkspaceVisibleLayoutService.HasOverlaps(resolvedPrimaryAreas, candidate))
            return false;

        var viewportWidth = MainGrid.ActualWidth > 0
            ? MainGrid.ActualWidth
            : ActualWidth / Math.Max(_settings.UiScale, 0.01);
        var viewportHeight = MainGrid.ActualHeight > 0
            ? MainGrid.ActualHeight
            : Math.Max(0, ActualHeight / Math.Max(_settings.UiScale, 0.01) - 86);
        if (!AdaptiveWindowService.CanHostSeparateUtility(
                viewportWidth,
                viewportHeight,
                resolvedPrimaryAreas,
                visiblePrimaryPanels,
                candidate,
                portrait))
            return false;

        utilityArea = candidate;
        return true;
    }

    private void ResolveEffectivePrimaryAreas(
        bool portrait,
        IReadOnlySet<PanelId> visiblePrimaryPanels)
    {
        var reserved = _utilityWindowSeparate ? _utilityWindowArea : PanelGridArea.Empty;
        _effectivePrimaryAreas = WorkspaceVisibleLayoutService.Resolve(
            _layout.GetGridAreas(portrait),
            visiblePrimaryPanels,
            portrait,
            reserved);
    }

    private PanelGridArea GetEffectivePrimaryArea(PanelId panelId, bool portrait) =>
        _effectivePrimaryAreas.TryGetValue(panelId, out var area)
            ? area
            : _layout.GetGridArea(panelId, portrait);

    private static HashSet<PanelId> GetVisiblePrimaryPanels(
        bool mediaOpen,
        bool previewOpen,
        bool inspectorOpen)
    {
        var visible = new HashSet<PanelId> { PanelId.Timeline };
        if (mediaOpen) visible.Add(PanelId.Media);
        if (previewOpen) visible.Add(PanelId.Preview);
        if (inspectorOpen) visible.Add(PanelId.Inspector);
        return visible;
    }

    private void EnsureUtilityTabsHostedBy(TabControl target)
    {
        if (ReferenceEquals(_utilityTabHost, target)) return;

        var entries = GetUtilityPanelEntries();
        var selected = entries.FirstOrDefault(entry => entry.Tab.IsSelected).Tab;
        foreach (var entry in entries)
        {
            InspectorTabs.Items.Remove(entry.Tab);
            UtilityTabs.Items.Remove(entry.Tab);
        }
        foreach (var entry in entries)
            target.Items.Add(entry.Tab);

        _utilityTabHost = target;
        if (selected?.Visibility == Visibility.Visible)
            selected.IsSelected = true;
    }

    private void EnsureInspectorCoreTabVisible(int tabIndex)
    {
        var tabs = new[] { PropertiesInspectorTab, EffectsInspectorTab, AudioInspectorTab };
        var preferred = tabs[Math.Clamp(tabIndex, 0, tabs.Length - 1)];
        if (preferred.Visibility == Visibility.Visible)
            preferred.IsSelected = true;
        else
            SelectFirstVisibleInspectorTab();
    }

    private void SelectFirstVisibleInspectorTab()
    {
        if (InspectorTabs.SelectedItem is TabItem selected && selected.Visibility == Visibility.Visible)
            return;
        var firstVisible = InspectorTabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
        if (firstVisible != null)
            firstVisible.IsSelected = true;
    }

    private void SelectFirstVisibleUtilityTab()
    {
        if (UtilityTabs.SelectedItem is TabItem selected && selected.Visibility == Visibility.Visible)
            return;

        var firstVisible = UtilityTabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
        if (firstVisible != null)
            firstVisible.IsSelected = true;
    }

    private void UpdateUtilityWindowTitle()
    {
        if (!_utilityWindowSeparate)
            return;

        var openTabs = GetUtilityPanelEntries()
            .Where(entry => entry.Tab.Visibility == Visibility.Visible)
            .Select(entry => entry.Tab)
            .ToArray();
        UtilityWindowTitleText.Text = openTabs.Length == 1
            ? GetTabTitle(openTabs[0])
            : UtilityTabs.SelectedItem is TabItem selected
                ? GetTabTitle(selected)
                : "Utilities";
    }

    private sealed record ProjectFolderOption(string? Path)
    {
        public string DisplayName => Path == null
            ? AllProjectFoldersLabel
            : string.IsNullOrWhiteSpace(System.IO.Path.GetFileName(Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)))
                ? Path
                : System.IO.Path.GetFileName(Path.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar));

        public override string ToString() => DisplayName;
    }
}
