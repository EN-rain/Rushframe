using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop.Dialogs;

internal sealed record ExportSettings(int Width, int Height, TimelineExportOptions Options);

internal sealed class ExportSettingsDialog
{
    private readonly Window _owner;
    private readonly Sequence _sequence;
    private readonly MediaAsset? _previewAsset;

    public ExportSettingsDialog(Window owner, Sequence sequence, MediaAsset? previewAsset = null)
    {
        _owner = owner;
        _sequence = sequence;
        _previewAsset = previewAsset;
    }

    public ExportSettings? Show()
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style ButtonStyle(string key) => (Style)_owner.FindResource(key);

        var dialog = new Window
        {
            Owner = _owner,
            Title = "Export Settings",
            Width = 860,
            Height = 540,
            MinWidth = 820,
            MinHeight = 520,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            AllowsTransparency = true,
            Foreground = Brush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };

        var widthBox = new TextBox { Text = _sequence.Width.ToString(CultureInfo.InvariantCulture), Width = 120 };
        var heightBox = new TextBox { Text = _sequence.Height.ToString(CultureInfo.InvariantCulture), Width = 120 };
        var dimensionsText = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var errorText = new TextBlock
        {
            Foreground = Brush("AccentHoverBrush"),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 18,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var formatCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<TimelineExportFormat>(),
            SelectedItem = TimelineExportFormat.Mp4,
            MinWidth = 138,
            MinHeight = 32,
        };
        var qualityCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<TimelineExportQuality>(),
            SelectedItem = TimelineExportQuality.High,
            MinWidth = 138,
            MinHeight = 32,
        };
        var includeAudioToggle = new CheckBox
        {
            Content = "Include mixed audio",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var previewFrame = new Border
        {
            Width = 300,
            Height = 300,
            BorderThickness = new Thickness(2),
            BorderBrush = Brush("AccentBrush"),
            Background = Brush("EditorMonitorBrush"),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipToBounds = true,
        };
        var previewHost = new Grid { ClipToBounds = true };
        var mediaPlayer = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Stop,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
        };
        var imagePreview = new Image
        {
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
        };
        var previewPlaceholder = new TextBlock
        {
            Text = "No timeline preview",
            Foreground = Brush("TextMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        previewHost.Children.Add(mediaPlayer);
        previewHost.Children.Add(imagePreview);
        previewHost.Children.Add(previewPlaceholder);
        previewFrame.Child = previewHost;

        var previewButton = new Button
        {
            Content = "Play Preview",
            Style = ButtonStyle("ChipButtonStyle"),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var previewPlaying = false;
        previewButton.Click += (_, _) =>
        {
            if (previewPlaying)
            {
                mediaPlayer.Pause();
                previewButton.Content = "Play Preview";
                previewPlaying = false;
                return;
            }

            mediaPlayer.Play();
            previewButton.Content = "Pause Preview";
            previewPlaying = true;
        };
        mediaPlayer.MediaEnded += (_, _) =>
        {
            mediaPlayer.Position = TimeSpan.Zero;
            previewButton.Content = "Play Preview";
            previewPlaying = false;
        };

        var mode = "current";
        var resolution = Math.Min(_sequence.Width, _sequence.Height);

        void UpdatePreview()
        {
            if (!int.TryParse(widthBox.Text, out var width) || !int.TryParse(heightBox.Text, out var height)
                || width < 2 || height < 2)
            {
                dimensionsText.Text = "Enter valid dimensions";
                errorText.Text = "Width and height must be valid numbers.";
                return;
            }

            errorText.Text = string.Empty;
            const double maxWidth = 330;
            const double maxHeight = 330;
            var scale = Math.Min(maxWidth / width, maxHeight / height);
            previewFrame.Width = Math.Max(120, width * scale);
            previewFrame.Height = Math.Max(120, height * scale);
            dimensionsText.Text = $"{width} x {height}  -  {(width >= height ? "Landscape" : "Portrait")}";
        }

        void ApplyPreset()
        {
            if (mode == "current")
            {
                widthBox.Text = _sequence.Width.ToString(CultureInfo.InvariantCulture);
                heightBox.Text = _sequence.Height.ToString(CultureInfo.InvariantCulture);
            }
            else if (mode == "portrait")
            {
                widthBox.Text = resolution.ToString(CultureInfo.InvariantCulture);
                heightBox.Text = resolution switch { 480 => "854", 720 => "1280", _ => "1920" };
            }
            else if (mode == "landscape")
            {
                widthBox.Text = resolution switch { 480 => "854", 720 => "1280", _ => "1920" };
                heightBox.Text = resolution.ToString(CultureInfo.InvariantCulture);
            }

            var custom = mode == "custom";
            widthBox.IsEnabled = custom;
            heightBox.IsEnabled = custom;
            UpdatePreview();
        }

        Button ModeButton(string label, string value)
        {
            var button = new Button
            {
                Content = label,
                Style = ButtonStyle("ChipButtonStyle"),
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 128,
                MinHeight = 34,
            };
            button.Click += (_, _) => { mode = value; ApplyPreset(); };
            return button;
        }

        Button ResolutionButton(string label, int value)
        {
            var button = new Button
            {
                Content = label,
                Style = ButtonStyle("ChipButtonStyle"),
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 88,
                MinHeight = 34,
            };
            button.Click += (_, _) =>
            {
                resolution = value;
                if (mode == "current") mode = _sequence.Width >= _sequence.Height ? "landscape" : "portrait";
                ApplyPreset();
            };
            return button;
        }

        widthBox.TextChanged += (_, _) => { if (mode == "custom") UpdatePreview(); };
        heightBox.TextChanged += (_, _) => { if (mode == "custom") UpdatePreview(); };
        LoadPreviewAsset();

        var shell = new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new Grid
        {
            Background = Brush("ChromeBrush"),
            Height = 48,
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) dialog.DragMove();
        };
        titleBar.Children.Add(new TextBlock
        {
            Text = "Export Settings",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        });
        var close = new Button
        {
            Content = "X",
            Style = ButtonStyle("IconButtonStyle"),
            Width = 42,
            Height = 36,
            Margin = new Thickness(0, 6, 10, 6),
        };
        close.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        root.Children.Add(titleBar);

        var body = new Grid { Margin = new Thickness(22, 20, 22, 14) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);

        var settingsCard = new Border
        {
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
        };
        var settingsPanel = new StackPanel();
        settingsPanel.Children.Add(new TextBlock
        {
            Text = "Format",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var modePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        modePanel.Children.Add(ModeButton("Current Size", "current"));
        modePanel.Children.Add(ModeButton("Portrait", "portrait"));
        modePanel.Children.Add(ModeButton("Landscape", "landscape"));
        modePanel.Children.Add(ModeButton("Custom", "custom"));
        settingsPanel.Children.Add(modePanel);

        settingsPanel.Children.Add(new TextBlock
        {
            Text = "Resolution",
            Foreground = Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        var resolutionPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        resolutionPanel.Children.Add(ResolutionButton("480p", 480));
        resolutionPanel.Children.Add(ResolutionButton("720p", 720));
        resolutionPanel.Children.Add(ResolutionButton("1080p", 1080));
        settingsPanel.Children.Add(resolutionPanel);

        settingsPanel.Children.Add(new TextBlock
        {
            Text = "Size",
            Foreground = Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        customPanel.Children.Add(new TextBlock { Text = "W", Foreground = Brush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        customPanel.Children.Add(widthBox);
        customPanel.Children.Add(new TextBlock { Text = "x", Foreground = Brush("TextSecondaryBrush"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) });
        customPanel.Children.Add(new TextBlock { Text = "H", Foreground = Brush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        customPanel.Children.Add(heightBox);
        settingsPanel.Children.Add(customPanel);
        var encodingPanel = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        encodingPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        encodingPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        encodingPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var formatStack = new StackPanel();
        formatStack.Children.Add(new TextBlock { Text = "Container", Foreground = Brush("TextSecondaryBrush"), Margin = new Thickness(0, 0, 0, 7) });
        formatStack.Children.Add(formatCombo);
        encodingPanel.Children.Add(formatStack);
        var qualityStack = new StackPanel();
        qualityStack.Children.Add(new TextBlock { Text = "Quality", Foreground = Brush("TextSecondaryBrush"), Margin = new Thickness(0, 0, 0, 7) });
        qualityStack.Children.Add(qualityCombo);
        Grid.SetColumn(qualityStack, 2);
        encodingPanel.Children.Add(qualityStack);
        settingsPanel.Children.Add(encodingPanel);
        settingsPanel.Children.Add(includeAudioToggle);
        settingsPanel.Children.Add(errorText);
        settingsCard.Child = settingsPanel;
        body.Children.Add(settingsCard);

        var previewCard = new Border
        {
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
        };
        Grid.SetColumn(previewCard, 2);
        var previewPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        previewPanel.Children.Add(new TextBlock
        {
            Text = _previewAsset != null ? Path.GetFileName(_previewAsset.OriginalPath) : "Timeline preview",
            Foreground = Brush("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 360,
        });
        previewPanel.Children.Add(previewFrame);
        previewPanel.Children.Add(previewButton);
        previewPanel.Children.Add(dimensionsText);
        previewCard.Child = previewPanel;
        body.Children.Add(previewCard);
        root.Children.Add(body);

        var actions = new Border
        {
            Background = Brush("ChromeBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 14, 22, 14),
        };
        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "Cancel",
            Style = ButtonStyle("ChipButtonStyle"),
            MinWidth = 112,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 10, 0),
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var export = new Button
        {
            Content = "Continue",
            Style = ButtonStyle("PrimaryButtonStyle"),
            MinWidth = 124,
            MinHeight = 34,
            IsDefault = true,
        };
        export.Click += (_, _) =>
        {
            if (!int.TryParse(widthBox.Text, out var width) || !int.TryParse(heightBox.Text, out var height)
                || width < 2 || height < 2 || width > 7680 || height > 7680)
            {
                errorText.Text = "Enter dimensions between 2 and 7680 pixels.";
                return;
            }

            var format = formatCombo.SelectedItem is TimelineExportFormat selectedFormat
                ? selectedFormat
                : TimelineExportFormat.Mp4;
            var quality = qualityCombo.SelectedItem is TimelineExportQuality selectedQuality
                ? selectedQuality
                : TimelineExportQuality.High;
            dialog.Tag = new ExportSettings(
                width % 2 == 0 ? width : width + 1,
                height % 2 == 0 ? height : height + 1,
                new TimelineExportOptions(format, quality, includeAudioToggle.IsChecked == true));
            dialog.DialogResult = true;
        };
        actionButtons.Children.Add(cancel);
        actionButtons.Children.Add(export);
        actions.Child = actionButtons;
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        ApplyPreset();
        shell.Child = root;
        dialog.Content = shell;
        var result = dialog.ShowDialog() == true ? dialog.Tag as ExportSettings : null;
        mediaPlayer.Stop();
        return result;

        void LoadPreviewAsset()
        {
            if (_previewAsset == null || !File.Exists(_previewAsset.OriginalPath)) return;

            try
            {
                if (_previewAsset.Kind == MediaKind.Video)
                {
                    mediaPlayer.Source = new Uri(_previewAsset.OriginalPath);
                    mediaPlayer.Visibility = Visibility.Visible;
                    previewButton.Visibility = Visibility.Visible;
                    previewPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }

                if (_previewAsset.Kind == MediaKind.Image)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(_previewAsset.OriginalPath);
                    image.EndInit();
                    image.Freeze();
                    imagePreview.Source = image;
                    imagePreview.Visibility = Visibility.Visible;
                    previewPlaceholder.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                previewPlaceholder.Text = "PREVIEW UNAVAILABLE";
            }
        }
    }
}
