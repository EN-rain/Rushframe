using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Rushframe.Domain;

namespace Rushframe.Desktop.Dialogs;

public sealed class ExternalCompositionDialog : Window
{
    private readonly ExternalCompositionKind _kind;
    private readonly string _projectDirectory;
    private readonly TextBox _nameBox;
    private readonly TextBox _entryPointBox;
    private readonly TextBox _compositionIdBox;
    private readonly TextBox _outputBox;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly TextBox _fpsBox;
    private readonly TextBox _durationBox;
    private readonly CheckBox _transparentToggle;
    private readonly CheckBox _importToggle;
    private readonly TextBlock _validationText;
    private ExternalCompositionSpec? _result;

    public ExternalCompositionDialog(Window owner, ExternalCompositionKind kind, string projectDirectory, Sequence? sequence)
    {
        Owner = owner;
        _kind = kind;
        _projectDirectory = Path.GetFullPath(projectDirectory);
        Title = $"Register Local {kind} Composition";
        Width = 520;
        Height = kind == ExternalCompositionKind.Remotion ? 620 : 550;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = owner.FindResource("PanelBrush") as System.Windows.Media.Brush;

        _nameBox = Input(Path.GetFileName(_projectDirectory));
        _entryPointBox = Input("src/index.ts");
        _compositionIdBox = Input("Main");
        _outputBox = Input(Path.Combine("renders", $"composition.{(kind == ExternalCompositionKind.HyperFrames ? "mp4" : "mp4")}"));
        _widthBox = Input((sequence?.Width ?? 1920).ToString(CultureInfo.InvariantCulture));
        _heightBox = Input((sequence?.Height ?? 1080).ToString(CultureInfo.InvariantCulture));
        _fpsBox = Input((sequence?.FrameRate.Value ?? 30).ToString("0.###", CultureInfo.InvariantCulture));
        _durationBox = Input(Math.Max(1, sequence?.Duration.Seconds ?? 5).ToString("0.###", CultureInfo.InvariantCulture));
        _transparentToggle = new CheckBox { Content = "Render with transparent background when supported", Margin = new Thickness(0, 8, 0, 0) };
        _importToggle = new CheckBox { Content = "Import verified render into the Rushframe media library", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        _validationText = new TextBlock
        {
            Foreground = owner.FindResource("DangerBrush") as System.Windows.Media.Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var form = new StackPanel { Margin = new Thickness(18) };
        form.Children.Add(Heading($"Local {kind} adapter"));
        form.Children.Add(Description(
            "Rushframe will use only the executable already installed in this project's node_modules/.bin directory. " +
            "It will not clone repositories, run npx, or download packages."));
        form.Children.Add(Labelled("Project directory", new TextBox { Text = _projectDirectory, IsReadOnly = true }));
        form.Children.Add(Labelled("Composition name", _nameBox));
        if (kind == ExternalCompositionKind.Remotion)
        {
            form.Children.Add(Labelled("Entry point", _entryPointBox));
            form.Children.Add(Labelled("Composition ID", _compositionIdBox));
        }
        form.Children.Add(Labelled("Output path (relative to the composition project)", _outputBox));

        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        AddCompactField(dimensions, 0, "Width", _widthBox);
        AddCompactField(dimensions, 2, "Height", _heightBox);
        AddCompactField(dimensions, 4, "FPS", _fpsBox);
        AddCompactField(dimensions, 6, "Duration", _durationBox);
        form.Children.Add(dimensions);
        form.Children.Add(_transparentToggle);
        form.Children.Add(_importToggle);
        form.Children.Add(_validationText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 86, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var register = new Button
        {
            Content = "Register",
            MinWidth = 96,
            IsDefault = true,
            Style = owner.FindResource("PrimaryButtonStyle") as Style,
        };
        register.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(register);
        form.Children.Add(buttons);

        Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public ExternalCompositionSpec? ShowCompositionDialog() => ShowDialog() == true ? _result : null;

    private void Accept()
    {
        _validationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _validationText.Text = "Composition name is required.";
            return;
        }
        if (_kind == ExternalCompositionKind.Remotion
            && (string.IsNullOrWhiteSpace(_entryPointBox.Text) || string.IsNullOrWhiteSpace(_compositionIdBox.Text)))
        {
            _validationText.Text = "Remotion entry point and composition ID are required.";
            return;
        }
        if (!TryReadPositiveInt(_widthBox.Text, out var width)
            || !TryReadPositiveInt(_heightBox.Text, out var height)
            || !TryReadPositiveDouble(_fpsBox.Text, out var fps)
            || !TryReadPositiveDouble(_durationBox.Text, out var duration))
        {
            _validationText.Text = "Width, height, FPS, and duration must be positive numbers.";
            return;
        }
        if (width % 2 != 0 || height % 2 != 0)
        {
            _validationText.Text = "Width and height must be even numbers for reliable video encoding.";
            return;
        }
        _result = new ExternalCompositionSpec
        {
            Name = _nameBox.Text.Trim(),
            Kind = _kind,
            ProjectDirectory = _projectDirectory,
            EntryPoint = _kind == ExternalCompositionKind.Remotion ? _entryPointBox.Text.Trim() : null,
            CompositionId = _kind == ExternalCompositionKind.Remotion ? _compositionIdBox.Text.Trim() : null,
            OutputPath = _outputBox.Text.Trim(),
            Width = width,
            Height = height,
            FrameRate = FrameRate.FromDouble(fps),
            DurationSeconds = duration,
            TransparentBackground = _transparentToggle.IsChecked == true,
            ImportAfterRender = _importToggle.IsChecked == true,
            Status = ExternalCompositionStatus.Draft,
        };
        DialogResult = true;
    }

    private static TextBox Input(string value) => new() { Text = value, MinHeight = 32 };

    private static FrameworkElement Labelled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock Description(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.75,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static void AddCompactField(Grid grid, int column, string label, FrameworkElement input)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(input);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private static bool TryReadPositiveInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 2 && result <= 16384;

    private static bool TryReadPositiveDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && result > 0 && result <= 86400;
}
