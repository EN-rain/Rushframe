using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Rushframe.Domain;
using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Dialogs;

internal sealed class CreativeAssetsDialog
{
    private readonly Window _owner;
    private readonly List<CreativeAssetProviderManifest> _providers;
    private readonly List<ExtensionManifest> _extensions;
    private readonly CreativeAssetPackService _assetPackService;
    private readonly ExtensionManifestService _extensionService;
    private readonly string _assetPackDirectory;
    private readonly string _extensionDirectory;

    public CreativeAssetsDialog(
        Window owner,
        List<CreativeAssetProviderManifest> providers,
        List<ExtensionManifest> extensions,
        CreativeAssetPackService assetPackService,
        ExtensionManifestService extensionService,
        string assetPackDirectory,
        string extensionDirectory)
    {
        _owner = owner;
        _providers = providers;
        _extensions = extensions;
        _assetPackService = assetPackService;
        _extensionService = extensionService;
        _assetPackDirectory = assetPackDirectory;
        _extensionDirectory = extensionDirectory;
    }

    public CreativeAssetDescriptor? Show()
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style Style(string key) => (Style)_owner.FindResource(key);

        var dialog = new Window
        {
            Owner = _owner,
            Title = "Creative Assets & Extensions",
            Width = 920,
            Height = 680,
            MinWidth = 760,
            MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };
        DialogTheme.Apply(dialog, _owner);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Creative Assets & Extensions",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Rushframe uses local, licensed asset packs. Network-enabled packs and remote extension entry points are rejected.",
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(header);

        var tabs = new TabControl { Background = Brushes.Transparent };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var assetTabRoot = new Grid { Margin = new Thickness(10) };
        assetTabRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        assetTabRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        assetTabRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var assetToolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        assetToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        assetToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchBox = new TextBox
        {
            MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search assets by name, kind, tag, provider, or license",
        };
        assetToolbar.Children.Add(searchBox);
        var assetButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        var importPack = new Button { Content = "Import Pack", Style = Style("CommandButtonStyle"), Margin = new Thickness(0, 0, 7, 0) };
        var packTemplate = new Button { Content = "Create Pack Template", Style = Style("CommandButtonStyle") };
        assetButtons.Children.Add(importPack);
        assetButtons.Children.Add(packTemplate);
        Grid.SetColumn(assetButtons, 1);
        assetToolbar.Children.Add(assetButtons);
        assetTabRoot.Children.Add(assetToolbar);

        var assetList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        assetList.ItemTemplate = CreateAssetTemplate(Brush);
        Grid.SetRow(assetList, 1);
        assetTabRoot.Children.Add(assetList);

        var assetInfo = new TextBlock
        {
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        };
        Grid.SetRow(assetInfo, 2);
        assetTabRoot.Children.Add(assetInfo);
        tabs.Items.Add(new TabItem { Header = "Assets", Content = assetTabRoot });

        var extensionRoot = new Grid { Margin = new Thickness(10) };
        extensionRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        extensionRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        extensionRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var extensionToolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 10) };
        var importExtension = new Button { Content = "Import Manifest", Style = Style("CommandButtonStyle"), Margin = new Thickness(0, 0, 7, 0) };
        var extensionTemplate = new Button { Content = "Create Manifest Template", Style = Style("CommandButtonStyle") };
        extensionToolbar.Children.Add(importExtension);
        extensionToolbar.Children.Add(extensionTemplate);
        extensionRoot.Children.Add(extensionToolbar);
        var extensionList = new ListBox
        {
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            DisplayMemberPath = nameof(ExtensionDisplay.Label),
        };
        Grid.SetRow(extensionList, 1);
        extensionRoot.Children.Add(extensionList);
        var extensionInfo = new TextBlock
        {
            Text = "Manifests are discovery and permission metadata only. Rushframe does not execute arbitrary extension code. High-risk permissions remain disabled.",
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        };
        Grid.SetRow(extensionInfo, 2);
        extensionRoot.Children.Add(extensionInfo);
        tabs.Items.Add(new TabItem { Header = "Extensions", Content = extensionRoot });

        var status = new TextBlock
        {
            Foreground = Brush("AccentHoverBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(status);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var close = new Button { Content = "Close", Style = Style("CommandButtonStyle"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        var insert = new Button { Content = "Add to Timeline", Style = Style("PrimaryButtonStyle"), MinWidth = 122, IsDefault = true, IsEnabled = false };
        close.Click += (_, _) => dialog.DialogResult = false;
        insert.Click += (_, _) =>
        {
            if (assetList.SelectedItem is not AssetDisplay display) return;
            dialog.Tag = display.Asset;
            dialog.DialogResult = true;
        };
        footerButtons.Children.Add(close);
        footerButtons.Children.Add(insert);
        Grid.SetColumn(footerButtons, 1);
        footer.Children.Add(footerButtons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        List<AssetDisplay> BuildAssetList()
        {
            var query = searchBox.Text.Trim();
            return _providers
                .SelectMany(provider => provider.Assets.Select(asset => new AssetDisplay(provider, asset)))
                .Where(display => query.Length == 0
                                  || display.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(display => display.Asset.Kind)
                .ThenBy(display => display.Asset.Name)
                .ToList();
        }

        void RefreshAssets()
        {
            assetList.ItemsSource = BuildAssetList();
            assetInfo.Text = $"{assetList.Items.Count} local assets from {_providers.Count} providers.";
            insert.IsEnabled = assetList.SelectedItem != null;
        }

        void RefreshExtensions()
        {
            extensionList.ItemsSource = _extensions
                .OrderBy(extension => extension.Name)
                .Select(extension => new ExtensionDisplay(extension))
                .ToArray();
        }

        searchBox.TextChanged += (_, _) => RefreshAssets();
        assetList.SelectionChanged += (_, _) =>
        {
            insert.IsEnabled = assetList.SelectedItem != null;
            if (assetList.SelectedItem is AssetDisplay selected)
            {
                assetInfo.Text = $"{selected.Provider.Name} · {selected.Asset.Kind} · {selected.Asset.LicenseName ?? "License not specified"}" +
                                 (string.IsNullOrWhiteSpace(selected.Asset.Attribution) ? string.Empty : $" · Credit: {selected.Asset.Attribution}");
            }
        };
        importPack.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Rushframe Asset Pack (*.rushframe-assets.json)|*.rushframe-assets.json|JSON (*.json)|*.json" };
            if (picker.ShowDialog(dialog) != true) return;
            try
            {
                var inspected = _assetPackService.LoadPack(picker.FileName);
                var installedManifest = InstallManifestDirectory(
                    picker.FileName,
                    _assetPackDirectory,
                    inspected.Id,
                    inspected.Id + ".rushframe-assets.json");
                var provider = _assetPackService.LoadPack(installedManifest);
                _providers.RemoveAll(existing => string.Equals(existing.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                _providers.Add(provider);
                status.Text = $"Imported asset pack: {provider.Name}";
                RefreshAssets();
            }
            catch (Exception exception)
            {
                status.Text = $"Asset pack rejected: {exception.Message}";
            }
        };
        packTemplate.Click += (_, _) =>
        {
            var save = new SaveFileDialog
            {
                Filter = "Rushframe Asset Pack (*.rushframe-assets.json)|*.rushframe-assets.json",
                FileName = "my-pack.rushframe-assets.json",
            };
            if (save.ShowDialog(dialog) != true) return;
            _assetPackService.WriteTemplate(save.FileName);
            status.Text = "Asset-pack template created.";
        };
        importExtension.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Filter = "Rushframe Extension (*.rushframe-extension.json)|*.rushframe-extension.json|JSON (*.json)|*.json" };
            if (picker.ShowDialog(dialog) != true) return;
            try
            {
                var inspected = _extensionService.Load(picker.FileName);
                var installedManifest = InstallManifestDirectory(
                    picker.FileName,
                    _extensionDirectory,
                    inspected.Id,
                    inspected.Id + ".rushframe-extension.json");
                var extension = _extensionService.Load(installedManifest);
                _extensions.RemoveAll(existing => string.Equals(existing.Id, extension.Id, StringComparison.OrdinalIgnoreCase));
                _extensions.Add(extension);
                status.Text = extension.Enabled
                    ? $"Imported reviewed extension manifest: {extension.Name}"
                    : $"Imported {extension.Name}, but it remains disabled because it requests high-risk permissions.";
                RefreshExtensions();
            }
            catch (Exception exception)
            {
                status.Text = $"Extension manifest rejected: {exception.Message}";
            }
        };
        extensionTemplate.Click += (_, _) =>
        {
            var save = new SaveFileDialog
            {
                Filter = "Rushframe Extension (*.rushframe-extension.json)|*.rushframe-extension.json",
                FileName = "my-extension.rushframe-extension.json",
            };
            if (save.ShowDialog(dialog) != true) return;
            _extensionService.WriteTemplate(save.FileName);
            status.Text = "Extension-manifest template created.";
        };
        extensionList.SelectionChanged += (_, _) =>
        {
            if (extensionList.SelectedItem is not ExtensionDisplay display) return;
            extensionInfo.Text = $"{display.Extension.Name} {display.Extension.Version}\n" +
                                 $"Permissions: {(display.Extension.Permissions.Count == 0 ? "none" : string.Join(", ", display.Extension.Permissions))}\n" +
                                 $"Status: {(display.Extension.Enabled ? "reviewed metadata enabled" : "disabled; high-risk permission or manual review required")}. " +
                                 "Rushframe does not execute its entry point.";
        };

        dialog.Content = root;
        RefreshAssets();
        RefreshExtensions();
        return dialog.ShowDialog() == true ? dialog.Tag as CreativeAssetDescriptor : null;
    }

    private static DataTemplate CreateAssetTemplate(Func<string, Brush> brush)
    {
        var template = new DataTemplate(typeof(AssetDisplay));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(10));
        border.SetValue(Border.BorderBrushProperty, brush("BorderBrush"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        var glyph = TextBlockFactory(nameof(AssetDisplay.Glyph), 24, FontWeights.Normal, brush("AccentHoverBrush"), new Thickness(0, 0, 12, 0));
        glyph.SetValue(DockPanel.DockProperty, Dock.Left);
        grid.AppendChild(glyph);
        var details = new FrameworkElementFactory(typeof(StackPanel));
        details.AppendChild(TextBlockFactory(nameof(AssetDisplay.Title), 12, FontWeights.SemiBold, brush("TextBrush"), new Thickness()));
        details.AppendChild(TextBlockFactory(nameof(AssetDisplay.Subtitle), 10.5, FontWeights.Normal, brush("TextMutedBrush"), new Thickness(0, 3, 0, 0)));
        grid.AppendChild(details);
        border.AppendChild(grid);
        template.VisualTree = border;
        return template;
    }

    private static FrameworkElementFactory TextBlockFactory(
        string path,
        double size,
        FontWeight weight,
        Brush foreground,
        Thickness margin)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
        text.SetValue(TextBlock.FontSizeProperty, size);
        text.SetValue(TextBlock.FontWeightProperty, weight);
        text.SetValue(TextBlock.ForegroundProperty, foreground);
        text.SetValue(TextBlock.MarginProperty, margin);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        return text;
    }

    private static string InstallManifestDirectory(
        string sourceManifest,
        string installationRoot,
        string folderName,
        string manifestFileName)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceManifest))!;
        var safeFolderName = string.Concat(folderName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var destinationDirectory = Path.Combine(installationRoot, safeFolderName);
        if (Directory.Exists(destinationDirectory)) Directory.Delete(destinationDirectory, recursive: true);
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destination = Path.GetFullPath(Path.Combine(destinationDirectory, relative));
            if (!destination.StartsWith(destinationDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Manifest installation attempted to escape its destination.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
        }

        var installedManifest = Path.Combine(destinationDirectory, manifestFileName);
        var copiedOriginal = Path.Combine(destinationDirectory, Path.GetFileName(sourceManifest));
        if (!string.Equals(copiedOriginal, installedManifest, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(copiedOriginal, installedManifest, overwrite: true);
            File.Delete(copiedOriginal);
        }
        return installedManifest;
    }

    private sealed record AssetDisplay(CreativeAssetProviderManifest Provider, CreativeAssetDescriptor Asset)
    {
        public string Glyph => Asset.Kind switch
        {
            CreativeAssetKind.Font => "Aa",
            CreativeAssetKind.Sound => "♪",
            CreativeAssetKind.Music => "♫",
            CreativeAssetKind.Shape => BuiltInGlyph(Asset.Id),
            CreativeAssetKind.Sticker => "★",
            _ => "◆",
        };
        public string Title => Asset.Name;
        public string Subtitle => $"{Provider.Name} · {Asset.Kind} · {Asset.LicenseName ?? "No license metadata"}";
        public string SearchText => string.Join(" ", new[]
        {
            Asset.Name, Asset.Id, Asset.Kind.ToString(), Provider.Name, Asset.LicenseName ?? string.Empty,
            Asset.Attribution ?? string.Empty, string.Join(" ", Asset.Tags),
        });
    }

    private sealed record ExtensionDisplay(ExtensionManifest Extension)
    {
        public string Label => $"{(Extension.Enabled ? "✓" : "⚠")} {Extension.Name} {Extension.Version} — " +
                               (Extension.Permissions.Count == 0 ? "no permissions" : string.Join(", ", Extension.Permissions));
    }

    private static string BuiltInGlyph(string id) => id switch
    {
        "builtin.shape.star" => "★",
        "builtin.shape.circle" => "●",
        "builtin.shape.triangle" => "▲",
        "builtin.shape.diamond" => "◆",
        "builtin.shape.arrow" => "➜",
        "builtin.shape.heart" => "♥",
        "builtin.shape.speech" => "▰",
        _ => "◆",
    };
}
