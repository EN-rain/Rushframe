using System.Windows;
using System.Windows.Controls;
using Rushframe.Desktop.Controllers;

namespace Rushframe.Desktop.Dialogs;

internal sealed class AgentEditPlanPreviewDialog : Window
{
    internal AgentEditPlanPreviewDialog(Window owner, AgentEditPlanCompilation plan)
    {
        Owner = owner;
        Title = "Review Agent Edit Plan";
        Width = 720;
        Height = 620;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.FindResource("PanelBrush") as System.Windows.Media.Brush;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = plan.Summary,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Plan {plan.PlanId} • {plan.Operations.Count} operation(s) • applied as one undoable transaction",
            Margin = new Thickness(0, 5, 0, 12),
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(CreateTab("Operations", plan.Operations.Select((operation, index) =>
            $"{index + 1}. {operation.Action}\n{operation.Summary}{(string.IsNullOrWhiteSpace(operation.TargetId) ? string.Empty : $"\nTarget: {operation.TargetId}")}")));
        tabs.Items.Add(CreateTab("Affected ranges", plan.AffectedRanges.Select(range =>
            $"{Format(range.StartSeconds)}–{Format(range.EndSeconds)}  {range.Change}\n"
            + $"Track: {range.TrackId ?? "automatic"}{(string.IsNullOrWhiteSpace(range.ItemId) ? string.Empty : $" • Item: {range.ItemId}")}")));
        tabs.Items.Add(CreateTab(
            $"Warnings ({plan.Warnings.Count})",
            plan.Warnings.Count == 0 ? ["No plan warnings were generated."] : plan.Warnings));
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "Manual edits remain protected by the project revision check. You can undo this entire plan after applying it.",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Reject", MinWidth = 92, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button
        {
            Content = "Approve and apply",
            MinWidth = 142,
            IsDefault = true,
            Style = owner.FindResource("PrimaryButtonStyle") as Style,
        };
        apply.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    private static TabItem CreateTab(string title, IEnumerable<string> values)
    {
        var list = new ListBox { Margin = new Thickness(6) };
        foreach (var value in values)
        {
            list.Items.Add(new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 7),
            });
        }
        return new TabItem { Header = title, Content = list };
    }

    private static string Format(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff");
}
