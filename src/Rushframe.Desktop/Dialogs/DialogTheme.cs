using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Rushframe.Desktop.Dialogs;

internal static class DialogTheme
{
    public static void Apply(Window dialog, Window owner)
    {
        dialog.WindowStyle = WindowStyle.None;
        dialog.AllowsTransparency = true;
        dialog.Background = Brushes.Transparent;
        dialog.ShowInTaskbar = false;

        dialog.SourceInitialized += (_, _) => WrapContent(dialog, owner);
    }

    private static void WrapContent(Window dialog, Window owner)
    {
        if (dialog.Content is not UIElement originalContent || originalContent is Border { Tag: "RushframeDialogFrame" })
            return;

        var panelBrush = ResolveBrush(owner, "PanelBrush", Color.FromRgb(20, 21, 28));
        var chromeBrush = ResolveBrush(owner, "ChromeBrush", Color.FromRgb(14, 15, 21));
        var borderBrush = ResolveBrush(owner, "BorderBrush", Color.FromRgb(55, 48, 70));
        var textBrush = ResolveBrush(owner, "TextBrush", Colors.White);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Grid
        {
            Background = chromeBrush,
            Cursor = Cursors.Arrow,
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                dialog.DragMove();
        };

        var title = new TextBlock
        {
            Text = dialog.Title,
            Foreground = textBrush,
            FontFamily = owner.FontFamily,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 8, 0),
        };

        var close = new Button
        {
            Content = "×",
            Width = 42,
            Height = 42,
            FontSize = 18,
            Foreground = textBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Close",
        };
        close.Click += (_, _) => dialog.Close();
        close.MouseEnter += (_, _) => close.Background = new SolidColorBrush(Color.FromArgb(45, 192, 132, 252));
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;

        Grid.SetColumn(close, 1);
        titleBar.Children.Add(title);
        titleBar.Children.Add(close);

        // Detach the original logical child from Window before reparenting it into the frame.
        // Without this, WPF throws during SourceInitialized and terminates the process.
        dialog.Content = null;
        Grid.SetRow(originalContent, 1);
        root.Children.Add(titleBar);
        root.Children.Add(originalContent);

        dialog.Content = new Border
        {
            Tag = "RushframeDialogFrame",
            Background = panelBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = root,
        };
    }

    private static Brush ResolveBrush(FrameworkElement owner, string key, Color fallback)
    {
        try
        {
            return owner.FindResource(key) as Brush ?? new SolidColorBrush(fallback);
        }
        catch (ResourceReferenceKeyNotFoundException)
        {
            return new SolidColorBrush(fallback);
        }
    }
}
