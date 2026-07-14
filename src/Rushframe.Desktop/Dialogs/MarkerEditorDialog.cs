using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rushframe.Domain;

namespace Rushframe.Desktop.Dialogs;

internal sealed record MarkerEditorResult(
    string Label,
    string Note,
    MediaTime Time,
    MediaTime Duration,
    string Color);

internal sealed class MarkerEditorDialog
{
    private readonly Window _owner;
    private readonly Marker _marker;
    private readonly bool _isNew;

    public MarkerEditorDialog(Window owner, Marker marker, bool isNew)
    {
        _owner = owner;
        _marker = marker;
        _isNew = isNew;
    }

    public MarkerEditorResult? Show()
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style Style(string key) => (Style)_owner.FindResource(key);
        var dialog = new Window
        {
            Owner = _owner,
            Title = _isNew ? "Add Marker" : "Edit Marker",
            Width = 520,
            Height = 540,
            MinHeight = 520,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };
        DialogTheme.Apply(dialog, _owner);
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = _isNew ? "Add Timeline Marker" : "Edit Timeline Marker",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });
        var labelBox = Box(_marker.Label);
        var noteBox = new TextBox
        {
            Text = _marker.Note ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var timeBox = Box(_marker.Time.Seconds.ToString("0.###", CultureInfo.InvariantCulture));
        var durationBox = Box(_marker.Duration.Seconds.ToString("0.###", CultureInfo.InvariantCulture));
        var colorBox = Box(string.IsNullOrWhiteSpace(_marker.Color) ? "#ffcc00" : _marker.Color!);
        panel.Children.Add(Label("Label"));
        panel.Children.Add(labelBox);
        panel.Children.Add(Label("Note", new Thickness(0, 12, 0, 6)));
        panel.Children.Add(noteBox);
        var timeGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var timePanel = new StackPanel();
        timePanel.Children.Add(Label("Time (seconds)"));
        timePanel.Children.Add(timeBox);
        timeGrid.Children.Add(timePanel);
        var durationPanel = new StackPanel();
        durationPanel.Children.Add(Label("Duration (seconds)"));
        durationPanel.Children.Add(durationBox);
        Grid.SetColumn(durationPanel, 2);
        timeGrid.Children.Add(durationPanel);
        panel.Children.Add(timeGrid);
        panel.Children.Add(Label("Color (#RRGGBB)", new Thickness(0, 12, 0, 6)));
        panel.Children.Add(colorBox);
        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var color in new[] { "#ffcc00", "#ff5c7a", "#8b5cf6", "#38bdf8", "#34d399", "#f97316", "#ffffff" })
        {
            var button = new Button
            {
                Width = 32,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                BorderBrush = Brush("BorderStrongBrush"),
                ToolTip = color,
            };
            button.Click += (_, _) => colorBox.Text = color;
            swatches.Children.Add(button);
        }
        var customColorButton = new Button
        {
            Content = "More colors...",
            Style = Style("CommandButtonStyle"),
            MinWidth = 112,
            Height = 28,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Choose a custom marker color",
        };
        customColorButton.Click += (_, _) =>
        {
            if (ShowColorPicker(colorBox.Text) is { } picked)
                colorBox.Text = picked;
        };
        swatches.Children.Add(customColorButton);
        panel.Children.Add(swatches);
        root.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var error = new TextBlock { Foreground = Brush("AccentHoverBrush"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Cancel", Style = Style("CommandButtonStyle"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = _isNew ? "Add Marker" : "Save Marker", Style = Style("PrimaryButtonStyle"), MinWidth = 104, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(labelBox.Text))
            {
                error.Text = "Marker label is required.";
                return;
            }
            if (!double.TryParse(timeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || !double.TryParse(durationBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
                || seconds < 0 || duration < 0)
            {
                error.Text = "Time and duration must be non-negative numbers.";
                return;
            }
            var color = colorBox.Text.Trim();
            if (color.Length != 7 || color[0] != '#'
                || !int.TryParse(color[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                error.Text = "Marker color must use #RRGGBB.";
                return;
            }
            dialog.Tag = new MarkerEditorResult(
                labelBox.Text.Trim(),
                noteBox.Text.Trim(),
                MediaTime.FromSeconds(seconds),
                MediaTime.FromSeconds(duration),
                color);
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        dialog.Content = root;
        dialog.Loaded += (_, _) => { labelBox.Focus(); labelBox.SelectAll(); };
        return dialog.ShowDialog() == true ? dialog.Tag as MarkerEditorResult : null;
    }

    private static TextBlock Label(string text, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(172, 174, 187)),
        Margin = margin ?? new Thickness(0, 0, 0, 6),
    };

    private static TextBox Box(string value) => new()
    {
        Text = value,
        MinHeight = 32,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static bool TryParseDrawingColor(string value, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }

        color = Colors.Transparent;
        return false;
    }

    private string? ShowColorPicker(string currentValue)
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style Style(string key) => (Style)_owner.FindResource(key);
        var dialog = new Window
        {
            Owner = _owner,
            Title = "Choose marker color",
            Width = 360,
            Height = 300,
            MinWidth = 340,
            MinHeight = 280,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBrush"),
            Foreground = Brush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };
        DialogTheme.Apply(dialog, _owner);

        TryParseDrawingColor(currentValue, out var initial);
        var (hue, saturation, brightness) = RgbToHsb(initial == Colors.Transparent ? Color.FromRgb(255, 204, 0) : initial);
        var result = (string?)null;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var preview = new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brush("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(preview);

        var hueSlider = PickerSlider("Hue", 0, 360, hue);
        Grid.SetRow(hueSlider.Panel, 1);
        root.Children.Add(hueSlider.Panel);
        var saturationSlider = PickerSlider("Saturation", 0, 100, saturation * 100);
        Grid.SetRow(saturationSlider.Panel, 2);
        root.Children.Add(saturationSlider.Panel);
        var brightnessSlider = PickerSlider("Brightness", 0, 100, brightness * 100);
        Grid.SetRow(brightnessSlider.Panel, 3);
        root.Children.Add(brightnessSlider.Panel);

        var hexText = new TextBlock
        {
            Foreground = Brush("TextSecondaryBrush"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(hexText, 4);
        root.Children.Add(hexText);

        void UpdatePreview()
        {
            var color = HsbToRgb(hueSlider.Slider.Value, saturationSlider.Slider.Value / 100, brightnessSlider.Slider.Value / 100);
            preview.Background = new SolidColorBrush(color);
            hexText.Text = ToHex(color);
        }

        hueSlider.Slider.ValueChanged += (_, _) => UpdatePreview();
        saturationSlider.Slider.ValueChanged += (_, _) => UpdatePreview();
        brightnessSlider.Slider.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Style = Style("CommandButtonStyle"), MinWidth = 86, Margin = new Thickness(0, 0, 8, 0) };
        var choose = new Button { Content = "Use Color", Style = Style("PrimaryButtonStyle"), MinWidth = 104, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        choose.Click += (_, _) => { result = hexText.Text; dialog.DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(choose);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        dialog.Content = root;
        return dialog.ShowDialog() == true ? result : null;
    }

    private (StackPanel Panel, Slider Slider) PickerSlider(string label, double minimum, double maximum, double value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(Label(label));
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value };
        panel.Children.Add(slider);
        return (panel, slider);
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}".ToLowerInvariant();

    private static Color HsbToRgb(double hue, double saturation, double brightness)
    {
        hue = ((hue % 360) + 360) % 360;
        var c = brightness * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = brightness - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static (double Hue, double Saturation, double Brightness) RgbToHsb(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * (((b - r) / delta) + 2)
            : 60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;
        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
