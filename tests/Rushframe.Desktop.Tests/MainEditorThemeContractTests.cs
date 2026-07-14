using System.Globalization;
using System.Xml.Linq;

namespace Rushframe.Desktop.Tests;

public sealed class MainEditorThemeContractTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void shared_theme_uses_violet_brand_accents()
    {
        var appXaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "App.xaml"));

        Assert.Contains("#8B5CF6", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#A78BFA", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#3B82F6", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#60A5FA", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#2563EB", appXaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void main_window_keeps_a_usable_minimum_size()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var window = Assert.IsType<XElement>(document.Root);
        var minWidth = double.Parse(window.Attribute("MinWidth")!.Value, CultureInfo.InvariantCulture);
        var minHeight = double.Parse(window.Attribute("MinHeight")!.Value, CultureInfo.InvariantCulture);

        Assert.True(minWidth >= 1120);
        Assert.True(minHeight >= 620);

        var windowCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        Assert.Contains("PtMinTrackSize.X", windowCode, StringComparison.Ordinal);
        Assert.Contains("PtMinTrackSize.Y", windowCode, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetDpi(this)", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void main_menu_scrolls_on_hover_and_exposes_a_preview_orientation_toggle()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var scroller = document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "HeaderMenuScrollViewer");
        var orientationButton = document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "PreviewOrientationButton");

        Assert.Equal("HeaderMenuScrollViewer_PreviewMouseWheel", (string?)scroller.Attribute("PreviewMouseWheel"));
        Assert.Equal("HorizontalOnly", (string?)scroller.Attribute("PanningMode"));
        Assert.Equal("Switch preview window to portrait", (string?)orientationButton.Attribute("ToolTip"));
        Assert.Equal("Switch preview window to portrait", (string?)orientationButton.Attribute("AutomationProperties.Name"));
        Assert.Contains(
            orientationButton.Ancestors(),
            element => (string?)element.Attribute(Xaml + "Name") == "PreviewHeader");
    }

    [Fact]
    public void editor_uses_an_adaptive_three_by_two_grid_for_both_orientations()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var windowCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var canvasCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Canvas.cs"));
        var dockingCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Docking.cs"));
        var layoutCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Layout.cs"));
        var previewTransportPanel = document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "PreviewTransportPanel");
        var previewTransportScroller = document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "PreviewTransportScrollViewer");
        var splitterRow = document.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "WorkspaceSplitterRow");

        Assert.Contains("TimelineWindow", xaml, StringComparison.Ordinal);
        Assert.Equal("4", (string?)splitterRow.Attribute("Height"));
        Assert.Equal(Presentation + "StackPanel", previewTransportPanel.Name);
        Assert.Equal("Horizontal", (string?)previewTransportPanel.Attribute("Orientation"));
        Assert.Equal("Hidden", (string?)previewTransportScroller.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("PreviewTransportScrollViewer_PreviewMouseWheel", (string?)previewTransportScroller.Attribute("PreviewMouseWheel"));
        Assert.Contains("ApplyAdaptiveGridPlacements", windowCode, StringComparison.Ordinal);
        Assert.Contains("ConfigureAdaptiveGridSplitters", windowCode, StringComparison.Ordinal);
        Assert.Contains("PlacePanelInGrid", layoutCode, StringComparison.Ordinal);
        Assert.Contains("WorkspaceGridLayoutPlanner.TryMovePanel", dockingCode, StringComparison.Ordinal);
        Assert.Contains("TryGetAdaptiveGridCell", dockingCode, StringComparison.Ordinal);
        Assert.Contains("GetAdaptiveGridAreaBounds", dockingCode, StringComparison.Ordinal);

        var toggleStart = canvasCode.IndexOf("private void TogglePreviewWindowOrientation", StringComparison.Ordinal);
        var toggleEnd = canvasCode.IndexOf("private void UpdatePreviewOrientationButton", toggleStart, StringComparison.Ordinal);
        Assert.True(toggleStart >= 0 && toggleEnd > toggleStart);
        var toggleBody = canvasCode[toggleStart..toggleEnd];
        Assert.Contains("_previewWindowPortrait", toggleBody, StringComparison.Ordinal);
        Assert.Contains("UpdateResponsiveLayout", toggleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSequenceSettingsCommand", toggleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsurePreviewDockedForLandscape", toggleBody, StringComparison.Ordinal);
    }

    [Fact]
    public void project_files_preview_and_inspector_use_the_new_shell_contract()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var shellCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.UiShell.cs"));
        var windowCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("SideMediaButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtraToolsButton", xaml, StringComparison.Ordinal);
        Assert.Contains("Project Files", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectFolderFilterCombo", xaml, StringComparison.Ordinal);
        Assert.Contains("GridViewColumn Header=\"Name\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GridViewColumn Header=\"Folder\"", xaml, StringComparison.Ordinal);

        Assert.Contains("PreviewFreeLayoutButton", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewFullscreenButton", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewOrientationIcon", xaml, StringComparison.Ordinal);
        Assert.Contains("TogglePreviewFreeLayout", shellCode, StringComparison.Ordinal);
        Assert.Contains("ApplyLayout();", File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Preview.cs")), StringComparison.Ordinal);

        Assert.Contains("InspectorFieldColumnsCombo", xaml, StringComparison.Ordinal);
        Assert.Contains("TransformFieldsGrid", xaml, StringComparison.Ordinal);
        Assert.Contains("1 column", xaml, StringComparison.Ordinal);
        Assert.Contains("2 columns", xaml, StringComparison.Ordinal);
        Assert.Contains("3 columns", xaml, StringComparison.Ordinal);
        Assert.Contains("UtilityWindowHost", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceUtilityPlacementService.TryFindArea", shellCode, StringComparison.Ordinal);
        Assert.Contains("EnsureUtilityTabsHostedBy", shellCode, StringComparison.Ordinal);
        Assert.Contains("RightPanelHost.Children.Remove(TasksBorder)", shellCode, StringComparison.Ordinal);
        Assert.Contains("activityOpen && !_utilityWindowSeparate", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void project_name_close_buttons_and_closable_inspector_tabs_follow_shell_guardrails()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var shellCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.UiShell.cs"));
        var projectCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Project.cs"));
        var layoutCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Layout.cs"));
        var windowCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml.cs"));
        var dialogTheme = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Dialogs", "DialogTheme.cs"));

        Assert.Contains("ProjectNameText", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectNameEditBox", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewWindowTitleText", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Preview\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommitProjectNameEdit", shellCode, StringComparison.Ordinal);
        Assert.Contains("_project.IncrementRevision()", shellCode, StringComparison.Ordinal);
        Assert.Contains("_project.Name", projectCode, StringComparison.Ordinal);

        Assert.Contains("MediaCloseWindowButton", xaml, StringComparison.Ordinal);
        Assert.Contains("InspectorCloseWindowButton", xaml, StringComparison.Ordinal);
        Assert.Contains("UtilityCloseWindowButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewCloseWindowButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TimelineCloseWindowButton", xaml, StringComparison.Ordinal);
        Assert.Contains("InspectorAddTabButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ConfigureClosableInspectorTabs", shellCode, StringComparison.Ordinal);
        Assert.Contains("CloseInspectorTab", shellCode, StringComparison.Ordinal);
        Assert.Contains("ShowInspectorTabMenu", shellCode, StringComparison.Ordinal);

        Assert.Contains("WorkspaceVisibleLayoutService.Resolve", shellCode, StringComparison.Ordinal);
        Assert.Contains("CanClosePrimaryPanelWithoutEmptyCells", shellCode, StringComparison.Ordinal);
        Assert.Contains("_effectivePrimaryAreas", layoutCode, StringComparison.Ordinal);
        Assert.Contains("dialog.Content = null", dialogTheme, StringComparison.Ordinal);
        Assert.Contains("FindTrackMenuItem", windowCode, StringComparison.Ordinal);
        Assert.Contains("QueueAllowedClose", projectCode, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", projectCode, StringComparison.Ordinal);
        Assert.DoesNotContain("(MenuItem)menu.Items[", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void combo_boxes_and_check_boxes_expose_complete_visual_states()
    {
        var appXaml = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "App.xaml"));

        Assert.Contains("PART_EditableTextBox", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsEditable\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CheckMark\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"CheckMark\" Property=\"Visibility\" Value=\"Visible\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void editor_windows_move_only_from_six_dot_drag_handles()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var dockingCode = File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.Docking.cs"));
        var handles = new[]
        {
            "MediaWindowDragHandle",
            "PreviewWindowDragHandle",
            "TimelineWindowDragHandle",
            "InspectorWindowDragHandle",
            "UtilityWindowDragHandle",
        };
        var titleBars = new[]
        {
            "MediaWindowTitleBar",
            "PreviewHeader",
            "TimelineWindowTitleBar",
            "InspectorWindowTitleBar",
            "UtilityWindowTitleBar",
        };
        var titleTexts = new[]
        {
            "MediaWindowTitleText",
            "PreviewWindowTitleText",
            "TimelineWindowTitleText",
            "InspectorWindowTitleText",
            "UtilityWindowTitleText",
        };

        foreach (var name in handles)
        {
            var handle = document.Descendants()
                .Single(element => (string?)element.Attribute(Xaml + "Name") == name);
            Assert.Equal("SizeAll", (string?)handle.Attribute("Cursor"));
            Assert.Contains(
                handle.Descendants(Presentation + "TextBlock"),
                text => (string?)text.Attribute("Text") == "⠿");
            Assert.Contains($"RegisterPanelTitleBar({name}", dockingCode, StringComparison.Ordinal);
        }

        foreach (var name in titleBars)
        {
            var titleBar = document.Descendants()
                .Single(element => (string?)element.Attribute(Xaml + "Name") == name);
            Assert.Null(titleBar.Attribute("Cursor"));
            Assert.DoesNotContain($"RegisterPanelTitleBar({name}", dockingCode, StringComparison.Ordinal);
        }

        foreach (var name in titleTexts)
        {
            var titleText = document.Descendants()
                .Single(element => (string?)element.Attribute(Xaml + "Name") == name);
            Assert.Equal(Presentation + "TextBlock", titleText.Name);
            Assert.Equal("1", (string?)titleText.Attribute("Grid.Column"));
            Assert.Empty(titleText.Parent!.Elements(Presentation + "Path"));
        }

        Assert.Contains(document.Descendants(),
            element => (string?)element.Attribute(Xaml + "Name") == "PanelDockTargetHighlight");
        Assert.Contains("WorkspaceGridLayoutPlanner.TryMovePanel", dockingCode, StringComparison.Ordinal);
        Assert.Contains("_panelDropLayout", dockingCode, StringComparison.Ordinal);
        Assert.Contains("SaveLayout", dockingCode, StringComparison.Ordinal);
        Assert.Contains("CaptureMouse", dockingCode, StringComparison.Ordinal);
    }

    [Fact]
    public void main_editor_action_buttons_are_icon_only_and_keep_hover_labels()
    {
        var document = XDocument.Load(SourcePath("src", "Rushframe.Desktop", "MainWindow.xaml"));
        var actionButtons = document
            .Descendants()
            .Where(element => element.Name == Presentation + "Button" || element.Name == Presentation + "ToggleButton")
            .Where(element => !element.Ancestors().Any(IsTemplateOrStyle))
            .Where(element => (string?)element.Attribute(Xaml + "Name") != "GlobalFunctionSearchBackdrop")
            .ToArray();

        Assert.NotEmpty(actionButtons);
        foreach (var button in actionButtons)
        {
            var content = (string?)button.Attribute("Content");
            if (content != null)
            {
                Assert.True(content.Length <= 3, $"Button content '{content}' is not icon-only.");
                Assert.DoesNotContain(content, char.IsWhiteSpace);
            }

            Assert.Empty(button.Descendants(Presentation + "TextBlock"));
            Assert.NotNull(button.Attribute("ToolTip"));
        }
    }

    [Theory]
    [InlineData("src", "Rushframe.Desktop", "App.xaml")]
    [InlineData("src", "Rushframe.Desktop", "MainWindow.xaml")]
    public void button_hover_templates_change_background_without_drawing_a_border(params string[] pathParts)
    {
        var document = XDocument.Load(SourcePath(pathParts));
        var buttonStyles = document
            .Descendants(Presentation + "Style")
            .Where(style => ((string?)style.Attribute("TargetType"))?.Contains("Button", StringComparison.Ordinal) == true);

        foreach (var style in buttonStyles)
        {
            var hoverTriggers = style
                .Descendants(Presentation + "Trigger")
                .Where(trigger => (string?)trigger.Attribute("Property") == "IsMouseOver")
                .Where(trigger => (string?)trigger.Attribute("Value") == "True");

            foreach (var trigger in hoverTriggers)
            {
                Assert.DoesNotContain(
                    trigger.Elements(Presentation + "Setter"),
                    setter => ((string?)setter.Attribute("Property"))?.EndsWith("BorderBrush", StringComparison.Ordinal) == true);
            }
        }
    }

    [Fact]
    public void custom_editor_interaction_accents_do_not_use_legacy_teal()
    {
        var sources = new[]
        {
            File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Controls", "AnimationGraphControl.cs")),
            File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "MainWindow.PreviewInteraction.cs")),
            File.ReadAllText(SourcePath("src", "Rushframe.Desktop", "Timeline", "TimelineControl.MultiSelection.cs")),
        };
        var combined = string.Join('\n', sources);

        Assert.DoesNotContain("100, 225, 204", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("70, 235, 204", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("76, 220, 196", combined, StringComparison.Ordinal);
        Assert.Contains("196, 181, 253", combined, StringComparison.Ordinal);
    }

    private static bool IsTemplateOrStyle(XElement element) =>
        element.Name == Presentation + "Style"
        || element.Name == Presentation + "ControlTemplate"
        || element.Name == Presentation + "DataTemplate"
        || element.Name == Presentation + "ItemsPanelTemplate";

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
