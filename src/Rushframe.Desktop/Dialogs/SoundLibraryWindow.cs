using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Rushframe.Desktop.Services;
using Rushframe.Desktop.Timeline;
using Rushframe.Domain;

namespace Rushframe.Desktop.Dialogs;

internal sealed record SoundLibraryImportResult(
    IReadOnlyList<MediaAsset> Imported,
    IReadOnlyList<string> Errors);

internal sealed class SoundLibraryWindow : Window
{
    public const string AudioFileFilter =
        "Audio files|*.wav;*.mp3;*.aac;*.m4a;*.flac;*.ogg;*.oga;*.opus;*.wma;*.aif;*.aiff;*.ac3;*.amr;*.caf|All files|*.*";

    private readonly Window _owner;
    private readonly Func<IReadOnlyList<MediaAsset>> _assetProvider;
    private readonly Func<SoundLibraryCatalogQuery, CancellationToken, Task<SoundLibraryCatalogSearchResponse>> _searchAsync;
    private readonly Func<CancellationToken, Task<SoundLibraryCatalogStatus>> _statusAsync;
    private readonly Func<string> _projectIdProvider;
    private readonly Func<string?, CancellationToken, Task<List<SoundLibraryCollection>>> _listCollectionsAsync;
    private readonly Func<string, string?, CancellationToken, Task<string>> _createCollectionAsync;
    private readonly Func<string, string, CancellationToken, Task> _addToCollectionAsync;
    private readonly Func<string, string, CancellationToken, Task> _removeFromCollectionAsync;
    private readonly Func<IReadOnlyList<string>, Task<SoundLibraryImportResult>> _importAsync;
    private readonly Func<string, CancellationToken, Task<SoundLibraryIndexResult>> _addFolderAsync;
    private readonly Func<CancellationToken, Task<SoundLibraryIndexResult>> _reindexAsync;
    private readonly Func<SoundLibraryCatalogEntry, CancellationToken, Task<MediaAsset?>> _registerAsync;
    private readonly Func<SoundLibraryCatalogEntry, bool, CancellationToken, Task> _setFavoriteAsync;
    private readonly Func<SoundLibraryCatalogEntry, string, string, bool, CancellationToken, Task> _updateLicenseAsync;
    private readonly Action<MediaAsset> _addAtPlayhead;
    private readonly Action<MediaAsset> _preview;
    private readonly Action<SoundLibraryCatalogEntry> _previewCatalog;
    private readonly ListBox _soundList;
    private readonly TextBox _searchBox;
    private readonly CheckBox _semanticToggle;
    private readonly CheckBox _favoritesToggle;
    private readonly CheckBox _includeOfflineToggle;
    private readonly ComboBox _viewFilter;
    private readonly ComboBox _collectionFilter;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _moodFilter;
    private readonly TextBox _durationFilter;
    private readonly TextBox _minLufsFilter;
    private readonly TextBox _maxLufsFilter;
    private readonly TextBox _minTempoFilter;
    private readonly TextBox _maxTempoFilter;
    private readonly TextBox _licenseFilter;
    private readonly TextBlock _status;
    private readonly TextBlock _indexStatus;
    private readonly Image _waveformPreview;
    private readonly Button _addAtPlayheadButton;
    private readonly Button _registerButton;
    private readonly Button _favoriteButton;
    private readonly Button _licenseButton;
    private readonly Button _addToCollectionButton;
    private readonly Button _removeFromCollectionButton;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private CancellationTokenSource? _refreshCancellation;
    private Point _dragStart;
    private string? _similarToSoundId;
    private bool _suppressCollectionRefresh;

    public SoundLibraryWindow(
        Window owner,
        Func<IReadOnlyList<MediaAsset>> assetProvider,
        Func<SoundLibraryCatalogQuery, CancellationToken, Task<SoundLibraryCatalogSearchResponse>> searchAsync,
        Func<CancellationToken, Task<SoundLibraryCatalogStatus>> statusAsync,
        Func<string> projectIdProvider,
        Func<string?, CancellationToken, Task<List<SoundLibraryCollection>>> listCollectionsAsync,
        Func<string, string?, CancellationToken, Task<string>> createCollectionAsync,
        Func<string, string, CancellationToken, Task> addToCollectionAsync,
        Func<string, string, CancellationToken, Task> removeFromCollectionAsync,
        Func<IReadOnlyList<string>, Task<SoundLibraryImportResult>> importAsync,
        Func<string, CancellationToken, Task<SoundLibraryIndexResult>> addFolderAsync,
        Func<CancellationToken, Task<SoundLibraryIndexResult>> reindexAsync,
        Func<SoundLibraryCatalogEntry, CancellationToken, Task<MediaAsset?>> registerAsync,
        Func<SoundLibraryCatalogEntry, bool, CancellationToken, Task> setFavoriteAsync,
        Func<SoundLibraryCatalogEntry, string, string, bool, CancellationToken, Task> updateLicenseAsync,
        Action<MediaAsset> addAtPlayhead,
        Action<MediaAsset> preview,
        Action<SoundLibraryCatalogEntry> previewCatalog)
    {
        Owner = owner;
        _owner = owner;
        _assetProvider = assetProvider;
        _searchAsync = searchAsync;
        _statusAsync = statusAsync;
        _projectIdProvider = projectIdProvider;
        _listCollectionsAsync = listCollectionsAsync;
        _createCollectionAsync = createCollectionAsync;
        _addToCollectionAsync = addToCollectionAsync;
        _removeFromCollectionAsync = removeFromCollectionAsync;
        _importAsync = importAsync;
        _addFolderAsync = addFolderAsync;
        _reindexAsync = reindexAsync;
        _registerAsync = registerAsync;
        _setFavoriteAsync = setFavoriteAsync;
        _updateLicenseAsync = updateLicenseAsync;
        _addAtPlayhead = addAtPlayhead;
        _preview = preview;
        _previewCatalog = previewCatalog;

        Title = "Sound Library";
        Width = 940;
        Height = 650;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brush("PanelBrush");
        Foreground = Brush("TextBrush");
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        DialogTheme.Apply(this, owner);
        AutomationProperties.SetName(this, "Sound Library");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headingText = new StackPanel();
        headingText.Children.Add(new TextBlock
        {
            Text = "Sound Library",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        headingText.Children.Add(new TextBlock
        {
            Text = "Search indexed local audio by meaning or metadata. Catalog results must be explicitly registered in the open project before timeline use. Rushframe never downloads or scrapes media.",
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 14, 0),
        });
        heading.Children.Add(headingText);
        _indexStatus = new TextBlock
        {
            Text = "Index unavailable",
            Foreground = Brush("TextMutedBrush"),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_indexStatus, 1);
        heading.Children.Add(_indexStatus);
        root.Children.Add(heading);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchBox = new TextBox
        {
            MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search by natural language, file name, category, mood, tags, or license",
        };
        AutomationProperties.SetName(_searchBox, "Search sound library");
        _searchBox.TextChanged += (_, _) => QueueRefresh(clearSimilarity: true);
        _searchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            _searchDebounce.Stop();
            _ = RefreshAssetsAsync();
            args.Handled = true;
        };
        toolbar.Children.Add(_searchBox);

        var importButton = CreateButton("Add Sounds", "PrimaryButtonStyle", 110, "Add local sounds");
        importButton.Margin = new Thickness(10, 0, 0, 0);
        importButton.Click += async (_, _) => await ImportSoundsAsync();
        Grid.SetColumn(importButton, 1);
        toolbar.Children.Add(importButton);

        var folderButton = CreateButton("Add Folder", "CommandButtonStyle", 105, "Add watched sound folder");
        folderButton.Margin = new Thickness(8, 0, 0, 0);
        folderButton.Click += async (_, _) => await AddFolderAsync();
        Grid.SetColumn(folderButton, 2);
        toolbar.Children.Add(folderButton);

        var browseLibrariesButton = CreateButton("Browse Libraries", "CommandButtonStyle", 126, "Browse recommended audio libraries");
        browseLibrariesButton.Margin = new Thickness(8, 0, 0, 0);
        browseLibrariesButton.ToolTip = "Open curated music and SFX websites for manual download and local import.";
        var libraryMenu = new ContextMenu();
        foreach (var recommendation in AudioAssetLibraryCatalog.All)
        {
            var menuItem = new MenuItem
            {
                Header = $"{recommendation.Name} — {recommendation.Category}",
                ToolTip = $"Best for: {recommendation.BestFor}\n{recommendation.Guidance}",
            };
            menuItem.Click += (_, _) => OpenLibrary(recommendation);
            libraryMenu.Items.Add(menuItem);
        }
        browseLibrariesButton.ContextMenu = libraryMenu;
        browseLibrariesButton.Click += (_, _) =>
        {
            libraryMenu.PlacementTarget = browseLibrariesButton;
            libraryMenu.IsOpen = true;
        };
        Grid.SetColumn(browseLibrariesButton, 3);
        toolbar.Children.Add(browseLibrariesButton);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 9), VerticalAlignment = VerticalAlignment.Center };
        _semanticToggle = new CheckBox
        {
            Content = "Semantic search",
            IsChecked = true,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _semanticToggle.Checked += (_, _) => QueueRefresh();
        _semanticToggle.Unchecked += (_, _) => QueueRefresh();
        filters.Children.Add(_semanticToggle);
        _favoritesToggle = new CheckBox
        {
            Content = "Favorites only",
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _favoritesToggle.Checked += (_, _) => QueueRefresh();
        _favoritesToggle.Unchecked += (_, _) => QueueRefresh();
        filters.Children.Add(_favoritesToggle);
        _includeOfflineToggle = new CheckBox
        {
            Content = "Include offline",
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _includeOfflineToggle.Checked += (_, _) => QueueRefresh();
        _includeOfflineToggle.Unchecked += (_, _) => QueueRefresh();
        filters.Children.Add(_includeOfflineToggle);
        filters.Children.Add(FilterLabel("View"));
        _viewFilter = CreateFilterCombo("All", "Project used", "Recently used");
        filters.Children.Add(_viewFilter);
        filters.Children.Add(FilterLabel("Collection"));
        _collectionFilter = new ComboBox
        {
            Width = 150,
            MinHeight = 28,
            DisplayMemberPath = nameof(CollectionChoice.Label),
            ItemsSource = new[] { new CollectionChoice(null, "Any collection") },
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 5, 0),
        };
        _collectionFilter.SelectionChanged += (_, _) =>
        {
            if (!_suppressCollectionRefresh) QueueRefresh();
        };
        filters.Children.Add(_collectionFilter);
        var newCollectionButton = CreateButton("New Collection", "CommandButtonStyle", 108, "Create sound collection");
        newCollectionButton.Margin = new Thickness(0, 0, 10, 0);
        newCollectionButton.Click += async (_, _) => await CreateCollectionAsync();
        filters.Children.Add(newCollectionButton);
        filters.Children.Add(FilterLabel("Category"));
        _categoryFilter = CreateFilterCombo(
            "Any", "transition", "impact", "ambience", "music", "voice", "ui", "animal", "nature", "mechanical", "other");
        filters.Children.Add(_categoryFilter);
        filters.Children.Add(FilterLabel("Mood"));
        _moodFilter = CreateFilterCombo("Any", "tense", "energetic", "calm", "happy", "sad", "dramatic", "neutral");
        filters.Children.Add(_moodFilter);
        filters.Children.Add(FilterLabel("Max seconds"));
        _durationFilter = new TextBox { Width = 66, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        _durationFilter.TextChanged += (_, _) => QueueRefresh();
        filters.Children.Add(_durationFilter);
        filters.Children.Add(FilterLabel("LUFS min/max"));
        _minLufsFilter = CreateNumericFilterBox(58);
        _maxLufsFilter = CreateNumericFilterBox(58);
        filters.Children.Add(_minLufsFilter);
        filters.Children.Add(_maxLufsFilter);
        filters.Children.Add(FilterLabel("BPM min/max"));
        _minTempoFilter = CreateNumericFilterBox(58);
        _maxTempoFilter = CreateNumericFilterBox(58);
        filters.Children.Add(_minTempoFilter);
        filters.Children.Add(_maxTempoFilter);
        filters.Children.Add(FilterLabel("License"));
        _licenseFilter = new TextBox { Width = 105, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        _licenseFilter.TextChanged += (_, _) => QueueRefresh();
        filters.Children.Add(_licenseFilter);
        var reindexButton = CreateButton("Reindex", "CommandButtonStyle", 86, "Reindex watched sound folders");
        reindexButton.Margin = new Thickness(12, 0, 0, 0);
        reindexButton.Click += async (_, _) => await ReindexAsync();
        filters.Children.Add(reindexButton);
        Grid.SetRow(filters, 2);
        root.Children.Add(filters);

        _soundList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            DisplayMemberPath = nameof(SoundDisplay.Label),
        };
        AutomationProperties.SetName(_soundList, "Local sounds");
        _soundList.MouseDoubleClick += (_, _) => PreviewSelected();
        _soundList.PreviewMouseLeftButtonDown += (_, args) => _dragStart = args.GetPosition(_soundList);
        _soundList.PreviewMouseMove += SoundList_PreviewMouseMove;
        _soundList.KeyDown += SoundList_KeyDown;
        _soundList.SelectionChanged += (_, _) => UpdateSelectionState();
        Grid.SetRow(_soundList, 3);
        root.Children.Add(_soundList);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var infoPanel = new StackPanel { MaxWidth = 520 };
        _waveformPreview = new Image
        {
            Height = 54,
            Stretch = Stretch.Fill,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 6),
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(_waveformPreview, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(_waveformPreview, "Selected sound waveform");
        infoPanel.Children.Add(new Border
        {
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3),
            Child = _waveformPreview,
        });
        _status = new TextBlock
        {
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520,
        };
        infoPanel.Children.Add(_status);
        footer.Children.Add(infoPanel);

        var footerButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 590 };
        var previewButton = CreateButton("Preview", "CommandButtonStyle", 82, "Preview selected sound");
        previewButton.Margin = new Thickness(0, 0, 7, 0);
        previewButton.Click += (_, _) => PreviewSelected();
        _favoriteButton = CreateButton("Favorite", "CommandButtonStyle", 86, "Toggle selected sound favorite");
        _favoriteButton.Margin = new Thickness(0, 0, 7, 0);
        _favoriteButton.Click += async (_, _) => await ToggleFavoriteAsync();
        _licenseButton = CreateButton("License", "CommandButtonStyle", 78, "Edit selected sound license");
        _licenseButton.Margin = new Thickness(0, 0, 7, 0);
        _licenseButton.Click += async (_, _) => await EditLicenseAsync();
        _addToCollectionButton = CreateButton("Collect", "CommandButtonStyle", 76, "Add selected sound to a collection");
        _addToCollectionButton.Margin = new Thickness(0, 0, 7, 0);
        _addToCollectionButton.Click += async (_, _) => await AddSelectedToCollectionAsync();
        _removeFromCollectionButton = CreateButton("Uncollect", "CommandButtonStyle", 82, "Remove selected sound from the active collection");
        _removeFromCollectionButton.Margin = new Thickness(0, 0, 7, 0);
        _removeFromCollectionButton.Click += async (_, _) => await RemoveSelectedFromCollectionAsync();
        var similarButton = CreateButton("Similar", "CommandButtonStyle", 78, "Find sounds similar to the selection");
        similarButton.Margin = new Thickness(0, 0, 7, 0);
        similarButton.Click += (_, _) => FindSimilar();
        _registerButton = CreateButton("Register", "CommandButtonStyle", 88, "Register selected sound in the open project");
        _registerButton.Margin = new Thickness(0, 0, 7, 0);
        _registerButton.Click += async (_, _) => await RegisterSelectedAsync();
        _addAtPlayheadButton = CreateButton("Add at Playhead", "PrimaryButtonStyle", 124, "Add selected sound at playhead");
        _addAtPlayheadButton.Click += (_, _) => AddSelectedAtPlayhead();
        footerButtons.Children.Add(previewButton);
        footerButtons.Children.Add(_favoriteButton);
        footerButtons.Children.Add(_licenseButton);
        footerButtons.Children.Add(_addToCollectionButton);
        footerButtons.Children.Add(_removeFromCollectionButton);
        footerButtons.Children.Add(similarButton);
        footerButtons.Children.Add(_registerButton);
        footerButtons.Children.Add(_addAtPlayheadButton);
        Grid.SetColumn(footerButtons, 1);
        footer.Children.Add(footerButtons);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        Content = root;
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = RefreshAssetsAsync();
        };
        Activated += (_, _) => _ = RefreshAssetsAsync();
        Closed += (_, _) =>
        {
            _searchDebounce.Stop();
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
        };
        _ = RefreshAssetsAsync();
    }

    public void RefreshAssets() => _ = RefreshAssetsAsync();

    public async Task RefreshAssetsAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        var selectedSoundId = (_soundList.SelectedItem as SoundDisplay)?.Entry.SoundId;
        var selectedCollectionId = (_collectionFilter.SelectedItem as CollectionChoice)?.Collection?.CollectionId;
        _status.Text = "Searching local sound index…";
        try
        {
            await RefreshCollectionsAsync(selectedCollectionId, cancellation.Token);
            var projectAssets = _assetProvider().Where(asset => asset.Kind == MediaKind.Audio).ToArray();
            var query = BuildQuery();
            var response = await _searchAsync(query, cancellation.Token);
            var displays = response.Results
                .Select(entry => new SoundDisplay(entry, FindRegisteredAsset(entry, projectAssets)))
                .ToList();
            foreach (var asset in projectAssets)
            {
                if (displays.Any(display => display.RegisteredAsset?.Id == asset.Id)) continue;
                if (!query.MatchesProjectFallback(asset)) continue;
                displays.Add(new SoundDisplay(CreateProjectFallbackEntry(asset), asset));
            }
            if (cancellation.IsCancellationRequested) return;
            _soundList.ItemsSource = displays;
            if (!string.IsNullOrWhiteSpace(selectedSoundId))
                _soundList.SelectedItem = displays.FirstOrDefault(display => display.Entry.SoundId == selectedSoundId);

            SoundLibraryCatalogStatus? index = null;
            try
            {
                index = await _statusAsync(cancellation.Token);
            }
            catch
            {
                // Project-local fallback remains usable without the Python index.
            }
            if (cancellation.IsCancellationRequested) return;
            _indexStatus.Text = index == null
                ? "Project-only fallback"
                : $"{index.OnlineCount}/{index.SoundCount} online · {index.RootCount} folder(s) · {ShortProvider(index.PreferredEmbeddingProvider)}";
            var mode = response.SemanticAvailable
                ? $"semantic ({ShortProvider(response.EmbeddingProvider)})"
                : "lexical fallback";
            _status.Text = displays.Count == 0
                ? "No sounds found. Add local sounds or an approved folder."
                : $"{displays.Count} sound(s) · {mode}. Catalog-only results must be registered before use.";
            if (!string.IsNullOrWhiteSpace(response.Warning))
                _status.Text += $" {response.Warning}";
            UpdateSelectionState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var fallback = _assetProvider()
                .Where(asset => asset.Kind == MediaKind.Audio)
                .Select(asset => new SoundDisplay(CreateProjectFallbackEntry(asset), asset))
                .ToArray();
            _soundList.ItemsSource = fallback;
            _indexStatus.Text = "Project-only fallback";
            _status.Text = fallback.Length == 0
                ? $"Sound index unavailable: {ex.Message}"
                : $"Sound index unavailable; showing {fallback.Length} project sound(s). {ex.Message}";
            UpdateSelectionState();
        }
    }

    private SoundLibraryCatalogQuery BuildQuery()
    {
        var maxDuration = ParseOptionalNumber(_durationFilter.Text, positiveOnly: true);
        var view = _viewFilter.SelectedItem as string ?? "All";
        var collectionId = (_collectionFilter.SelectedItem as CollectionChoice)?.Collection?.CollectionId;
        return new SoundLibraryCatalogQuery(
            Query: _searchBox.Text.Trim(),
            Limit: 50,
            MaxDuration: maxDuration,
            MinLufs: ParseOptionalNumber(_minLufsFilter.Text),
            MaxLufs: ParseOptionalNumber(_maxLufsFilter.Text),
            MinTempo: ParseOptionalNumber(_minTempoFilter.Text, positiveOnly: true),
            MaxTempo: ParseOptionalNumber(_maxTempoFilter.Text, positiveOnly: true),
            Category: SelectedFilter(_categoryFilter),
            Mood: SelectedFilter(_moodFilter),
            License: string.IsNullOrWhiteSpace(_licenseFilter.Text) ? null : _licenseFilter.Text.Trim(),
            FavoritesOnly: _favoritesToggle.IsChecked == true,
            IncludeOffline: _includeOfflineToggle.IsChecked == true,
            LexicalOnly: _semanticToggle.IsChecked != true,
            SimilarToSoundId: _similarToSoundId,
            CollectionId: collectionId,
            ProjectId: view == "Project used" ? _projectIdProvider() : null,
            RecentlyUsed: view == "Recently used");
    }

    private async Task RefreshCollectionsAsync(
        string? selectedCollectionId,
        CancellationToken cancellationToken)
    {
        var collections = await _listCollectionsAsync(_projectIdProvider(), cancellationToken);
        var choices = new List<CollectionChoice> { new(null, "Any collection") };
        choices.AddRange(collections.Select(collection =>
            new CollectionChoice(collection, collection.DisplayName)));
        try
        {
            _suppressCollectionRefresh = true;
            _collectionFilter.ItemsSource = choices;
            _collectionFilter.SelectedItem = choices.FirstOrDefault(choice =>
                choice.Collection?.CollectionId == selectedCollectionId) ?? choices[0];
        }
        finally
        {
            _suppressCollectionRefresh = false;
        }
    }

    private async Task CreateCollectionAsync()
    {
        var name = PromptCollectionName();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var activeCollectionId = (_collectionFilter.SelectedItem as CollectionChoice)?.Collection?.CollectionId;
            await _createCollectionAsync(
                name,
                _projectIdProvider(),
                CancellationToken.None);
            await RefreshCollectionsAsync(activeCollectionId, CancellationToken.None);
            UpdateSelectionState();
            _status.Text = $"Created project sound collection '{name}'. Use Collect to add the selected sound.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Collection creation failed: {ex.Message}";
        }
    }

    private async Task AddSelectedToCollectionAsync()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected
            || selected.Entry.SoundId.StartsWith("project:", StringComparison.Ordinal))
            return;
        var collection = ChooseCollection();
        if (collection == null)
        {
            _status.Text = "Create a collection before adding sounds to it.";
            return;
        }
        try
        {
            await _addToCollectionAsync(
                collection.CollectionId,
                selected.Entry.SoundId,
                CancellationToken.None);
            await RefreshCollectionsAsync(collection.CollectionId, CancellationToken.None);
            QueueRefresh();
            _status.Text = $"Added {selected.Entry.Name} to {collection.Name}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not add sound to collection: {ex.Message}";
        }
    }

    private async Task RemoveSelectedFromCollectionAsync()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected
            || _collectionFilter.SelectedItem is not CollectionChoice { Collection: { } collection })
            return;
        try
        {
            await _removeFromCollectionAsync(
                collection.CollectionId,
                selected.Entry.SoundId,
                CancellationToken.None);
            await RefreshCollectionsAsync(collection.CollectionId, CancellationToken.None);
            QueueRefresh();
            _status.Text = $"Removed {selected.Entry.Name} from {collection.Name}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not remove sound from collection: {ex.Message}";
        }
    }

    private SoundLibraryCollection? ChooseCollection()
    {
        if (_collectionFilter.SelectedItem is CollectionChoice { Collection: { } active })
            return active;
        var choices = _collectionFilter.Items
            .OfType<CollectionChoice>()
            .Where(choice => choice.Collection != null)
            .ToArray();
        if (choices.Length == 0) return null;
        if (choices.Length == 1) return choices[0].Collection;

        var dialog = new Window
        {
            Owner = this,
            Title = "Choose Sound Collection",
            Width = 420,
            Height = 360,
            MinWidth = 360,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        DialogTheme.Apply(dialog, _owner);
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "Add the selected sound to:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var list = new ListBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(CollectionChoice.Label),
            SelectedIndex = 0,
        };
        list.MouseDoubleClick += (_, _) => dialog.DialogResult = true;
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = CreateButton("Cancel", "CommandButtonStyle", 82, "Cancel collection selection");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var choose = CreateButton("Choose", "PrimaryButtonStyle", 82, "Choose collection");
        choose.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(choose);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog.ShowDialog() == true
            ? (list.SelectedItem as CollectionChoice)?.Collection
            : null;
    }

    private string? PromptCollectionName()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "New Sound Collection",
            Width = 430,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        DialogTheme.Apply(dialog, _owner);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Collection name",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7),
        });
        var input = new TextBox { MinHeight = 32 };
        panel.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = CreateButton("Cancel", "CommandButtonStyle", 82, "Cancel collection creation");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var create = CreateButton("Create", "PrimaryButtonStyle", 82, "Create collection");
        create.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private async Task ImportSoundsAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "Add Sounds to Rushframe",
            Filter = AudioFileFilter,
            Multiselect = true,
            CheckFileExists = true,
        };
        if (picker.ShowDialog(this) != true) return;

        _status.Text = $"Indexing and registering {picker.FileNames.Length} sound(s)…";
        try
        {
            var result = await _importAsync(picker.FileNames);
            await RefreshAssetsAsync();
            _status.Text = result.Errors.Count == 0
                ? $"Added {result.Imported.Count} sound(s). They are ready for drag-and-drop."
                : $"Added {result.Imported.Count} sound(s). {result.Errors.Count} file(s) were skipped: {string.Join(" ", result.Errors.Take(2))}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Sound import failed: {ex.Message}";
        }
    }

    private async Task AddFolderAsync()
    {
        var picker = new OpenFolderDialog
        {
            Title = "Add Local Sound Folder",
            Multiselect = false,
        };
        if (picker.ShowDialog(this) != true) return;
        _status.Text = $"Indexing {picker.FolderName}…";
        try
        {
            var result = await _addFolderAsync(picker.FolderName, CancellationToken.None);
            await RefreshAssetsAsync();
            _status.Text = $"Indexed {result.Indexed.Count} sound(s), detected {result.Duplicates.Count} duplicate(s), skipped {result.Skipped.Count}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Folder indexing failed: {ex.Message}";
        }
    }

    private async Task ReindexAsync()
    {
        _status.Text = "Reindexing watched sound folders…";
        try
        {
            var result = await _reindexAsync(CancellationToken.None);
            await RefreshAssetsAsync();
            _status.Text = $"Reindex complete: {result.Indexed.Count} updated, {result.Duplicates.Count} duplicate(s), {result.Skipped.Count} skipped.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Reindex failed: {ex.Message}";
        }
    }

    private async Task RegisterSelectedAsync()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected || selected.RegisteredAsset != null) return;
        _status.Text = $"Registering {selected.Entry.Name} in the open project…";
        try
        {
            var asset = await _registerAsync(selected.Entry, CancellationToken.None);
            await RefreshAssetsAsync();
            _status.Text = asset == null
                ? "Registration was not completed."
                : $"Registered {selected.Entry.Name}. It can now be dragged to the timeline.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Registration failed: {ex.Message}";
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected || selected.Entry.SoundId.StartsWith("project:", StringComparison.Ordinal)) return;
        try
        {
            await _setFavoriteAsync(selected.Entry, !selected.Entry.Favorite, CancellationToken.None);
            await RefreshAssetsAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"Favorite update failed: {ex.Message}";
        }
    }

    private async Task EditLicenseAsync()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected || selected.Entry.SoundId.StartsWith("project:", StringComparison.Ordinal)) return;
        var values = ShowLicenseDialog(selected.Entry);
        if (values == null) return;
        try
        {
            await _updateLicenseAsync(
                selected.Entry,
                values.Value.License,
                values.Value.Attribution,
                values.Value.RequiresAttribution,
                CancellationToken.None);
            await RefreshAssetsAsync();
        }
        catch (Exception ex)
        {
            _status.Text = $"License update failed: {ex.Message}";
        }
    }

    private (string License, string Attribution, bool RequiresAttribution)? ShowLicenseDialog(SoundLibraryCatalogEntry entry)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = $"Sound License — {entry.Name}",
            Width = 520,
            Height = 330,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        DialogTheme.Apply(dialog, _owner);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "License name", Margin = new Thickness(0, 0, 0, 5) });
        var license = new TextBox { Text = entry.LicenseName, MinHeight = 30 };
        panel.Children.Add(license);
        panel.Children.Add(new TextBlock { Text = "Required attribution / credit", Margin = new Thickness(0, 12, 0, 5) });
        var attribution = new TextBox
        {
            Text = entry.Attribution,
            MinHeight = 72,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(attribution);
        var requires = new CheckBox
        {
            Content = "Attribution is required before export",
            IsChecked = entry.RequiresAttribution,
            Margin = new Thickness(0, 12, 0, 0),
        };
        panel.Children.Add(requires);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = CreateButton("Cancel", "CommandButtonStyle", 86, "Cancel license edit");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = CreateButton("Save", "PrimaryButtonStyle", 86, "Save license metadata");
        save.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true
            ? (license.Text.Trim(), attribution.Text.Trim(), requires.IsChecked == true)
            : null;
    }

    private void FindSimilar()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected || selected.Entry.SoundId.StartsWith("project:", StringComparison.Ordinal)) return;
        _similarToSoundId = selected.Entry.SoundId;
        _searchBox.Text = string.Empty;
        _status.Text = $"Finding sounds similar to {selected.Entry.Name}…";
        _ = RefreshAssetsAsync();
    }

    private void SoundList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _soundList.SelectedItem is not SoundDisplay selected) return;
        var current = e.GetPosition(_soundList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        if (selected.RegisteredAsset == null)
        {
            _status.Text = "Register this catalog sound in the open project before dragging it to the timeline.";
            return;
        }
        DragDrop.DoDragDrop(_soundList, TimelineMediaDragData.Create(selected.RegisteredAsset.Id), DragDropEffects.Copy);
    }

    private void SoundList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            PreviewSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _ = RegisterSelectedAsync();
            e.Handled = true;
        }
    }

    private void PreviewSelected()
    {
        if (_soundList.SelectedItem is not SoundDisplay selected) return;
        if (selected.RegisteredAsset != null)
            _preview(selected.RegisteredAsset);
        else
            _previewCatalog(selected.Entry);
    }

    private void AddSelectedAtPlayhead()
    {
        if (_soundList.SelectedItem is SoundDisplay { RegisteredAsset: { } asset })
            _addAtPlayhead(asset);
    }

    private void UpdateSelectionState()
    {
        var selected = _soundList.SelectedItem as SoundDisplay;
        var hasCatalogEntry = selected != null && !selected.Entry.SoundId.StartsWith("project:", StringComparison.Ordinal);
        _registerButton.IsEnabled = selected != null && selected.RegisteredAsset == null && !selected.Entry.Offline;
        _addAtPlayheadButton.IsEnabled = selected?.RegisteredAsset != null && !selected.Entry.Offline;
        _favoriteButton.IsEnabled = hasCatalogEntry;
        _favoriteButton.Content = selected?.Entry.Favorite == true ? "Unfavorite" : "Favorite";
        _licenseButton.IsEnabled = hasCatalogEntry;
        UpdateWaveformPreview(selected?.Entry.WaveformPath);
        _addToCollectionButton.IsEnabled = hasCatalogEntry
                                               && _collectionFilter.Items.OfType<CollectionChoice>().Any(choice => choice.Collection != null);
        _removeFromCollectionButton.IsEnabled = hasCatalogEntry
                                                  && _collectionFilter.SelectedItem is CollectionChoice { Collection: not null };
        if (selected == null) return;
        var registration = selected.RegisteredAsset == null ? "catalog only" : "registered";
        var license = string.IsNullOrWhiteSpace(selected.Entry.LicenseName) ? "license not set" : selected.Entry.LicenseName;
        _status.Text = $"{selected.Entry.Name} · {selected.DurationText} · {selected.Entry.Category}/{selected.Entry.Mood} · {license} · {registration}.";
    }

    private void UpdateWaveformPreview(string? path)
    {
        _waveformPreview.Source = null;
        _waveformPreview.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            _waveformPreview.Source = image;
            _waveformPreview.Visibility = Visibility.Visible;
        }
        catch
        {
            _waveformPreview.Source = null;
            _waveformPreview.Visibility = Visibility.Collapsed;
        }
    }

    private void QueueRefresh(bool clearSimilarity = false)
    {
        if (clearSimilarity) _similarToSoundId = null;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private static MediaAsset? FindRegisteredAsset(
        SoundLibraryCatalogEntry entry,
        IReadOnlyList<MediaAsset> projectAssets) =>
        projectAssets.FirstOrDefault(asset =>
            (!string.IsNullOrWhiteSpace(asset.CatalogSoundId)
             && asset.CatalogSoundId.Equals(entry.SoundId, StringComparison.OrdinalIgnoreCase))
            || PathsReferToSameFile(asset.OriginalPath, entry.Path)
            || (!string.IsNullOrWhiteSpace(asset.FileFingerprint)
                && asset.FileFingerprint.Equals(entry.ContentHash, StringComparison.OrdinalIgnoreCase)));

    private static SoundLibraryCatalogEntry CreateProjectFallbackEntry(MediaAsset asset) => new()
    {
        SoundId = string.IsNullOrWhiteSpace(asset.CatalogSoundId) ? $"project:{asset.Id}" : asset.CatalogSoundId,
        Name = Path.GetFileName(asset.OriginalPath),
        Path = asset.OriginalPath,
        ContentHash = asset.FileFingerprint,
        Duration = asset.Duration.Seconds,
        Category = "project",
        Mood = "neutral",
        LicenseName = asset.LicenseName,
        Attribution = asset.Attribution,
        RequiresAttribution = asset.RequiresAttribution,
        Offline = asset.IsOffline || !File.Exists(asset.OriginalPath),
        Tags = ["project"],
    };

    private static bool PathsReferToSameFile(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? SelectedFilter(ComboBox combo) =>
        combo.SelectedItem is string value && !value.Equals("Any", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;

    private TextBox CreateNumericFilterBox(double width)
    {
        var box = new TextBox
        {
            Width = width,
            MinHeight = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        box.TextChanged += (_, _) => QueueRefresh();
        return box;
    }

    private static double? ParseOptionalNumber(string text, bool positiveOnly = false)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || !double.IsFinite(value)
            || (positiveOnly && value <= 0))
            return null;
        return value;
    }

    private ComboBox CreateFilterCombo(params string[] values)
    {
        var combo = new ComboBox
        {
            Width = 108,
            MinHeight = 28,
            ItemsSource = values,
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 10, 0),
        };
        combo.SelectionChanged += (_, _) => QueueRefresh();
        return combo;
    }

    private TextBlock FilterLabel(string text) => new()
    {
        Text = text,
        Foreground = Brush("TextMutedBrush"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 5, 0),
    };

    private Button CreateButton(string content, string style, double minWidth, string automationName)
    {
        var button = new Button
        {
            Content = content,
            Style = FindStyle(style),
            MinWidth = minWidth,
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private void OpenLibrary(AudioAssetLibraryRecommendation recommendation)
    {
        try
        {
            Process.Start(new ProcessStartInfo(recommendation.Url) { UseShellExecute = true });
            _status.Text = $"Opened {recommendation.Name}. Download manually, review the exact asset license, then use Add Sounds.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open {recommendation.Name}: {ex.Message}";
        }
    }

    private static string ShortProvider(string? provider) => provider switch
    {
        null or "" => "metadata only",
        "builtin-hash-v1" => "hash fallback",
        var value when value.StartsWith("laion-clap", StringComparison.OrdinalIgnoreCase) => "CLAP",
        _ => provider,
    };

    private Brush Brush(string key) => (Brush)_owner.FindResource(key);

    private Style FindStyle(string key) => (Style)_owner.FindResource(key);

    private sealed record CollectionChoice(SoundLibraryCollection? Collection, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SoundDisplay(SoundLibraryCatalogEntry Entry, MediaAsset? RegisteredAsset)
    {
        public string DurationText => Entry.Duration > 0
            ? TimeSpan.FromSeconds(Entry.Duration).ToString(Entry.Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
            : "duration unknown";

        public override string ToString() => Label;

        public string Label
        {
            get
            {
                var favorite = Entry.Favorite ? "★ " : string.Empty;
                var registered = RegisteredAsset == null ? "catalog" : "registered";
                var offline = Entry.Offline ? " · OFFLINE" : string.Empty;
                var loudness = Entry.Lufs.HasValue ? $" · {Entry.Lufs:0.0} LUFS" : string.Empty;
                var tempo = Entry.TempoBpm.HasValue ? $" · {Entry.TempoBpm:0} BPM" : string.Empty;
                var license = string.IsNullOrWhiteSpace(Entry.LicenseName) ? string.Empty : $" · {Entry.LicenseName}";
                return $"{favorite}{Entry.Name}   ·   {DurationText}   ·   {Entry.Category}/{Entry.Mood}   ·   {registered}{loudness}{tempo}{license}{offline}\n{Entry.Path}";
            }
        }
    }
}
