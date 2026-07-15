using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Rushframe.Domain;

namespace Rushframe.Desktop.Dialogs;

internal sealed record CanvasSettingsResult(
    int Width,
    int Height,
    FrameRate FrameRate,
    CanvasBackground Background,
    IReadOnlyList<LayoutGuide> Guides);

internal sealed class CanvasSettingsDialog
{
    private readonly Window _owner;
    private readonly Sequence _sequence;

    public CanvasSettingsDialog(Window owner, Sequence sequence)
    {
        _owner = owner;
        _sequence = sequence;
    }

    public CanvasSettingsResult? Show()
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style Style(string key) => (Style)_owner.FindResource(key);

        var dialog = new Window
        {
            Owner = _owner,
            Title = "Canvas & Guides",
            Width = 780,
            Height = 680,
            MinWidth = 700,
            MinHeight = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };
        DialogTheme.Apply(dialog, _owner);
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "Canvas, Frame Rate & Layout Guides",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var tabs = new TabControl { Background = Brushes.Transparent };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var widthBox = Numeric(_sequence.Width);
        var heightBox = Numeric(_sequence.Height);
        var frameRateCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                FrameRate.Fps23_976, FrameRate.Fps24, FrameRate.Fps25, FrameRate.Fps29_97,
                FrameRate.Fps30, FrameRate.Fps50, FrameRate.Fps59_94, FrameRate.Fps60,
            },
            SelectedItem = new[]
            {
                FrameRate.Fps23_976, FrameRate.Fps24, FrameRate.Fps25, FrameRate.Fps29_97,
                FrameRate.Fps30, FrameRate.Fps50, FrameRate.Fps59_94, FrameRate.Fps60,
            }.OrderBy(rate => Math.Abs(rate.Value - _sequence.FrameRate.Value)).First(),
            MinHeight = 32,
            MaxDropDownHeight = 220,
        };
        var frameRateText = new FrameworkElementFactory(typeof(TextBlock));
        frameRateText.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(FrameRate.Value)) { StringFormat = "{}{0:0.###}" });
        frameRateCombo.ItemTemplate = new DataTemplate { VisualTree = frameRateText };
        var canvasPanel = new StackPanel { Margin = new Thickness(14) };
        canvasPanel.Children.Add(Label("Canvas preset"));
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var preset in new[]
                 {
                     ("Vertical 1080×1920", 1080, 1920),
                     ("Landscape 1920×1080", 1920, 1080),
                     ("Square 1080×1080", 1080, 1080),
                     ("HD 1280×720", 1280, 720),
                     ("4K 3840×2160", 3840, 2160),
                 })
        {
            var button = new Button
            {
                Content = preset.Item1,
                Style = Style("CommandButtonStyle"),
                Margin = new Thickness(0, 0, 7, 7),
            };
            button.Click += (_, _) =>
            {
                widthBox.Text = preset.Item2.ToString(CultureInfo.InvariantCulture);
                heightBox.Text = preset.Item3.ToString(CultureInfo.InvariantCulture);
            };
            presets.Children.Add(button);
        }
        canvasPanel.Children.Add(presets);
        canvasPanel.Children.Add(CreateTwoColumnField("Width", widthBox, "Height", heightBox));
        canvasPanel.Children.Add(Label("Frame rate", new Thickness(0, 14, 0, 6)));
        canvasPanel.Children.Add(frameRateCombo);
        tabs.Items.Add(new TabItem { Header = "Canvas", Content = canvasPanel });

        var background = Clone(_sequence.Background);
        var backgroundKindCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<CanvasBackgroundKind>(),
            SelectedItem = background.Kind,
            MinHeight = 32,
        };
        var primaryBox = Text(background.PrimaryColor);
        var secondaryBox = Text(background.SecondaryColor);
        var angleBox = Numeric(background.GradientAngleDegrees);
        var blurBox = Numeric(background.BlurStrength);
        var opacityBox = Numeric(background.Opacity * 100);
        var backgroundPanel = new StackPanel { Margin = new Thickness(14) };
        backgroundPanel.Children.Add(Label("Background type"));
        backgroundPanel.Children.Add(backgroundKindCombo);
        backgroundPanel.Children.Add(CreateTwoColumnField("Primary color", primaryBox, "Secondary color", secondaryBox, new Thickness(0, 14, 0, 0)));
        backgroundPanel.Children.Add(CreateTwoColumnField("Gradient angle", angleBox, "Blur strength", blurBox, new Thickness(0, 14, 0, 0)));
        backgroundPanel.Children.Add(Label("Opacity (%)", new Thickness(0, 14, 0, 6)));
        backgroundPanel.Children.Add(opacityBox);
        backgroundPanel.Children.Add(new TextBlock
        {
            Text = "Colors use #RRGGBB. Gradient and transparent backgrounds are rendered in preview and export.",
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        tabs.Items.Add(new TabItem { Header = "Background", Content = backgroundPanel });

        var enabledKinds = _sequence.LayoutGuides.Where(guide => guide.Enabled).Select(guide => guide.Kind).ToHashSet();
        var guideChecks = new Dictionary<LayoutGuideKind, CheckBox>();
        var guidesPanel = new StackPanel { Margin = new Thickness(14) };
        guidesPanel.Children.Add(new TextBlock
        {
            Text = "Guides are editor overlays only and are never burned into exports.",
            Foreground = Brush("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        foreach (var kind in new[]
                 {
                     LayoutGuideKind.Grid, LayoutGuideKind.Center, LayoutGuideKind.TitleSafe,
                     LayoutGuideKind.ActionSafe, LayoutGuideKind.TikTok, LayoutGuideKind.InstagramReels,
                     LayoutGuideKind.YouTubeShorts, LayoutGuideKind.SnapchatSpotlight,
                 })
        {
            var check = new CheckBox
            {
                Content = FormatGuideName(kind),
                IsChecked = enabledKinds.Contains(kind),
                Margin = new Thickness(0, 0, 0, 9),
            };
            guideChecks[kind] = check;
            guidesPanel.Children.Add(check);
        }
        tabs.Items.Add(new TabItem { Header = "Guides", Content = new ScrollViewer { Content = guidesPanel } });

        var errorText = new TextBlock
        {
            Foreground = Brush("AccentHoverBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(errorText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Cancel", Style = Style("CommandButtonStyle"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Apply Canvas Settings", Style = Style("PrimaryButtonStyle"), MinWidth = 148, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(widthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !int.TryParse(heightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                || width is < 2 or > 8192 || height is < 2 or > 8192)
            {
                errorText.Text = "Width and height must be between 2 and 8192 pixels.";
                return;
            }
            if (!TryDouble(angleBox, out var angle) || !TryDouble(blurBox, out var blur) || !TryDouble(opacityBox, out var opacity))
            {
                errorText.Text = "Background values must be valid numbers.";
                return;
            }
            if (!IsHexColor(primaryBox.Text) || !IsHexColor(secondaryBox.Text))
            {
                errorText.Text = "Use #RRGGBB for background colors.";
                return;
            }

            var resultBackground = new CanvasBackground
            {
                Kind = backgroundKindCombo.SelectedItem is CanvasBackgroundKind kind ? kind : CanvasBackgroundKind.Solid,
                PrimaryColor = primaryBox.Text.Trim(),
                SecondaryColor = secondaryBox.Text.Trim(),
                GradientAngleDegrees = angle,
                BlurStrength = Math.Clamp(blur, 0, 100),
                Opacity = Math.Clamp(opacity / 100, 0, 1),
            };
            var guides = guideChecks
                .Where(pair => pair.Value.IsChecked == true)
                .Select(pair => BuildGuide(pair.Key))
                .ToArray();
            dialog.Tag = new CanvasSettingsResult(
                width % 2 == 0 ? width : width + 1,
                height % 2 == 0 ? height : height + 1,
                frameRateCombo.SelectedItem is FrameRate rate ? rate : FrameRate.Fps30,
                resultBackground,
                guides);
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        dialog.Content = root;
        return dialog.ShowDialog() == true ? dialog.Tag as CanvasSettingsResult : null;
    }

    private static LayoutGuide BuildGuide(LayoutGuideKind kind)
    {
        var margins = kind switch
        {
            LayoutGuideKind.TitleSafe => (0.10, 0.10, 0.10, 0.10),
            LayoutGuideKind.ActionSafe => (0.05, 0.05, 0.05, 0.05),
            LayoutGuideKind.TikTok => (0.06, 0.12, 0.12, 0.20),
            LayoutGuideKind.InstagramReels => (0.05, 0.10, 0.08, 0.18),
            LayoutGuideKind.YouTubeShorts => (0.05, 0.08, 0.08, 0.15),
            LayoutGuideKind.SnapchatSpotlight => (0.05, 0.08, 0.08, 0.18),
            _ => (0.0, 0.0, 0.0, 0.0),
        };
        return new LayoutGuide
        {
            Kind = kind,
            Name = FormatGuideName(kind),
            Left = margins.Item1,
            Top = margins.Item2,
            Right = margins.Item3,
            Bottom = margins.Item4,
        };
    }

    private static string FormatGuideName(LayoutGuideKind kind) => kind switch
    {
        LayoutGuideKind.InstagramReels => "Instagram Reels safe area",
        LayoutGuideKind.YouTubeShorts => "YouTube Shorts safe area",
        LayoutGuideKind.SnapchatSpotlight => "Snapchat Spotlight safe area",
        LayoutGuideKind.TikTok => "TikTok safe area",
        LayoutGuideKind.TitleSafe => "Title safe",
        LayoutGuideKind.ActionSafe => "Action safe",
        _ => kind.ToString(),
    };

    private static Grid CreateTwoColumnField(
        string leftLabel,
        Control left,
        string rightLabel,
        Control right,
        Thickness? margin = null)
    {
        var grid = new Grid { Margin = margin ?? new Thickness() };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var leftPanel = new StackPanel();
        leftPanel.Children.Add(Label(leftLabel));
        leftPanel.Children.Add(left);
        grid.Children.Add(leftPanel);
        var rightPanel = new StackPanel();
        rightPanel.Children.Add(Label(rightLabel));
        rightPanel.Children.Add(right);
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);
        return grid;
    }

    private static TextBlock Label(string text, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(170, 172, 184)),
        Margin = margin ?? new Thickness(0, 0, 0, 6),
    };

    private static TextBox Numeric(double value) => new()
    {
        Text = value.ToString("0.###", CultureInfo.InvariantCulture),
        MinHeight = 32,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static TextBox Text(string value) => new()
    {
        Text = value,
        MinHeight = 32,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static bool TryDouble(TextBox box, out double value) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsHexColor(string value)
    {
        var text = value.Trim();
        return text.Length == 7 && text[0] == '#' && int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json)!;
    }
}
