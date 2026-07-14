using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rushframe.Desktop.Commands;

namespace Rushframe.Desktop.Dialogs;

internal sealed class KeyboardShortcutsDialog
{
    private readonly Window _owner;
    private readonly EditorActionRegistry _registry;
    private readonly IReadOnlyDictionary<string, string> _current;

    public KeyboardShortcutsDialog(
        Window owner,
        EditorActionRegistry registry,
        IReadOnlyDictionary<string, string> current)
    {
        _owner = owner;
        _registry = registry;
        _current = current;
    }

    public Dictionary<string, string>? Show()
    {
        Brush Brush(string key) => (Brush)_owner.FindResource(key);
        Style Style(string key) => (Style)_owner.FindResource(key);

        var dialog = new Window
        {
            Owner = _owner,
            Title = "Keyboard Shortcuts",
            Width = 720,
            Height = 680,
            MinWidth = 620,
            MinHeight = 520,
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

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 19, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Use forms such as Ctrl+S, Ctrl+Shift+Z, Space, Left, or Delete. Leave blank to disable an action.",
            Foreground = Brush("TextMutedBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(header);

        var editors = new Dictionary<string, TextBox>();
        var list = new StackPanel();
        foreach (var group in _registry.Actions.GroupBy(action => action.Category))
        {
            list.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("AccentHoverBrush"),
                Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 16, 0, 6),
            });
            foreach (var action in group)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.Children.Add(new TextBlock
                {
                    Text = action.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("TextSecondaryBrush"),
                });
                var editor = new TextBox
                {
                    Text = _registry.ResolveShortcutText(action, _current),
                    MinHeight = 30,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = action,
                };
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                list.Children.Add(row);
                editors[action.Id] = editor;
            }
        }
        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
            Background = Brush("EditorPanelBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var error = new TextBlock
        {
            Foreground = Brush("AccentHoverBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        footerGrid.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var reset = new Button { Content = "Reset defaults", Style = Style("CommandButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Style = Style("CommandButtonStyle"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save shortcuts", Style = Style("PrimaryButtonStyle"), MinWidth = 118, IsDefault = true };
        reset.Click += (_, _) =>
        {
            foreach (var action in _registry.Actions)
                editors[action.Id].Text = action.DefaultShortcut;
            error.Text = string.Empty;
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) =>
        {
            var gestures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in _registry.Actions)
            {
                var text = editors[action.Id].Text.Trim();
                if (text.Length > 0 && EditorActionRegistry.ParseGesture(text) == null)
                {
                    error.Text = $"Invalid shortcut for {action.Name}: {text}";
                    editors[action.Id].Focus();
                    return;
                }
                if (text.Length > 0 && used.TryGetValue(text, out var other))
                {
                    error.Text = $"{text} is assigned to both {other} and {action.Name}.";
                    editors[action.Id].Focus();
                    return;
                }
                if (text.Length > 0) used[text] = action.Name;
                if (!string.Equals(text, action.DefaultShortcut, StringComparison.OrdinalIgnoreCase))
                    gestures[action.Id] = text;
            }
            dialog.Tag = gestures;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 1);
        footerGrid.Children.Add(buttons);
        Grid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);

        dialog.Content = root;
        return dialog.ShowDialog() == true ? dialog.Tag as Dictionary<string, string> : null;
    }
}
