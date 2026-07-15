using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rushframe.Domain;

namespace Rushframe.Desktop.Dialogs;

internal sealed class EditingBriefDialog : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.Ordinal);
    private readonly ComboBox _styleCombo;

    public EditingBriefDialog(Window owner, EditingBrief brief)
    {
        Owner = owner;
        Title = "Editing Brief";
        Width = 760;
        Height = 760;
        MinWidth = 620;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.FindResource("PanelBrush") as System.Windows.Media.Brush;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Define measurable creative constraints before an agent proposes timeline edits.",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        AddField(form, ref row, "Purpose", "purpose", brief.Purpose);
        AddField(form, ref row, "Target audience", "audience", brief.TargetAudience);
        AddField(form, ref row, "Platform", "platform", brief.Platform);
        AddField(form, ref row, "Aspect ratio", "aspect", brief.AspectRatio);
        AddField(form, ref row, "Target duration (s)", "duration", brief.TargetDurationSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty);
        AddField(form, ref row, "Tone", "tone", brief.Tone);

        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var styleLabel = Label("Editing style");
        Grid.SetRow(styleLabel, row);
        form.Children.Add(styleLabel);
        _styleCombo = new ComboBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 7), DisplayMemberPath = nameof(EditingStyleProfile.Name), SelectedValuePath = nameof(EditingStyleProfile.Id), ItemsSource = EditingStyleProfile.BuiltIns };
        _styleCombo.SelectedValue = brief.EditingStyle;
        _styleCombo.SelectedItem ??= EditingStyleProfile.BuiltIns.FirstOrDefault();
        Grid.SetRow(_styleCombo, row);
        Grid.SetColumn(_styleCombo, 1);
        form.Children.Add(_styleCombo);
        row++;

        AddField(form, ref row, "Pacing", "pacing", brief.Pacing);
        AddField(form, ref row, "Hook deadline (s)", "hook", brief.HookDeadlineSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty);
        AddField(form, ref row, "Required messages", "messages", string.Join(Environment.NewLine, brief.RequiredMessages), multiline: true);
        AddField(form, ref row, "Caption policy", "captions", brief.CaptionPolicy, multiline: true);
        AddField(form, ref row, "Music policy", "music", brief.MusicPolicy, multiline: true);
        AddField(form, ref row, "SFX policy", "sfx", brief.SoundEffectsPolicy, multiline: true);
        AddField(form, ref row, "Transition policy", "transitions", brief.TransitionPolicy, multiline: true);
        AddField(form, ref row, "Call to action", "cta", brief.CallToAction);
        AddField(form, ref row, "Brand colors", "colors", string.Join(", ", brief.BrandColors));
        AddField(form, ref row, "Brand fonts", "fonts", string.Join(", ", brief.BrandFonts));
        AddField(form, ref row, "Logo policy", "logo", brief.LogoPolicy, multiline: true);
        AddField(form, ref row, "Reference notes", "references", brief.ReferenceNotes, multiline: true);

        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) });
        var save = new Button { Content = "Save brief", IsDefault = true, MinWidth = 120, Style = owner.FindResource("PrimaryButtonStyle") as Style };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    public EditingBrief Result { get; private set; } = new();

    private void Save()
    {
        if (!TryOptionalPositive(_boxes["duration"].Text, out var duration) || !TryOptionalPositive(_boxes["hook"].Text, out var hook))
        {
            MessageBox.Show(this, "Target duration and hook deadline must be positive numbers when supplied.", "Invalid editing brief", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = new EditingBrief
        {
            Purpose = Text("purpose"), TargetAudience = Text("audience"), Platform = Text("platform"), AspectRatio = Text("aspect"),
            TargetDurationSeconds = duration, Tone = Text("tone"), EditingStyle = _styleCombo.SelectedValue?.ToString() ?? "custom",
            Pacing = Text("pacing"), HookDeadlineSeconds = hook, CaptionPolicy = Text("captions"), MusicPolicy = Text("music"),
            SoundEffectsPolicy = Text("sfx"), TransitionPolicy = Text("transitions"), CallToAction = Text("cta"), LogoPolicy = Text("logo"), ReferenceNotes = Text("references"),
        };
        Result.RequiredMessages.AddRange(Lines("messages"));
        Result.BrandColors.AddRange(Csv("colors"));
        Result.BrandFonts.AddRange(Csv("fonts"));
        Result.Normalize();
        DialogResult = true;
    }

    private string Text(string key) => _boxes[key].Text.Trim();
    private IEnumerable<string> Lines(string key) => _boxes[key].Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private IEnumerable<string> Csv(string key) => _boxes[key].Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void AddField(Grid form, ref int row, string label, string key, string value, bool multiline = false)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelControl = Label(label);
        Grid.SetRow(labelControl, row);
        form.Children.Add(labelControl);
        var box = new TextBox
        {
            Text = value,
            MinHeight = multiline ? 54 : 29,
            MaxHeight = multiline ? 90 : double.PositiveInfinity,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            Margin = new Thickness(0, 0, 0, 7),
        };
        _boxes[key] = box;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        form.Children.Add(box);
        row++;
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 6, 12, 7), VerticalAlignment = VerticalAlignment.Top, Opacity = 0.82 };

    private static bool TryOptionalPositive(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) return false;
        value = parsed;
        return true;
    }
}
