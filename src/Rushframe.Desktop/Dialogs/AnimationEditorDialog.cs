using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rushframe.Desktop.Controls;
using Rushframe.Domain;
using Rushframe.Domain.Editing;

namespace Rushframe.Desktop.Dialogs;

internal sealed class AnimationEditorDialog
{
    private static List<Keyframe> _keyframeClipboard = [];
    private readonly Window _owner;
    private readonly TimelineItem _item;
    private readonly double _playheadLocalSeconds;
    private readonly List<AnimationChannel> _channels;

    public AnimationEditorDialog(Window owner, TimelineItem item, double playheadLocalSeconds)
    {
        _owner = owner;
        _item = item;
        _playheadLocalSeconds = Math.Clamp(playheadLocalSeconds, 0, Math.Max(0, item.Duration.Seconds));
        _channels = UpdateAnimationChannelsCommand.Clone(item.AnimationChannels);
    }

    public IReadOnlyList<AnimationChannel>? Show()
    {
        Brush FindBrush(string key) => (Brush)_owner.FindResource(key);
        Style FindStyle(string key) => (Style)_owner.FindResource(key);

        var dialog = new Window
        {
            Owner = _owner,
            Title = "Animation Graph Editor",
            Width = 980,
            Height = 680,
            MinWidth = 820,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindBrush("PanelBrush"),
            Foreground = FindBrush("TextBrush"),
            FontFamily = _owner.FontFamily,
            FontSize = _owner.FontSize,
        };
        DialogTheme.Apply(dialog, _owner);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Animation Graph Editor",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Add property channels, place keyframes, drag the graph, and edit Bezier handles.",
            Foreground = FindBrush("TextMutedBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        header.Children.Add(heading);
        var currentTime = new TextBlock
        {
            Text = $"Playhead: {_playheadLocalSeconds:0.###}s",
            FontFamily = new FontFamily("Cascadia Mono"),
            Foreground = FindBrush("AccentHoverBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(currentTime, 1);
        header.Children.Add(currentTime);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);

        var sidebar = new Border
        {
            Background = FindBrush("EditorPanelBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
        };
        var sidebarPanel = new StackPanel();
        sidebarPanel.Children.Add(new TextBlock { Text = "Animated property", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) });
        var channelCombo = new ComboBox { MinHeight = 32, DisplayMemberPath = nameof(AnimationChannel.PropertyName) };
        sidebarPanel.Children.Add(channelCombo);

        var propertyCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                AnimationPropertyNames.PositionX, AnimationPropertyNames.PositionY,
                AnimationPropertyNames.ScaleX, AnimationPropertyNames.ScaleY,
                AnimationPropertyNames.Rotation, AnimationPropertyNames.Opacity,
                AnimationPropertyNames.Volume, AnimationPropertyNames.Pan,
            },
            SelectedIndex = 0,
            MinHeight = 32,
            Margin = new Thickness(0, 12, 0, 6),
        };
        sidebarPanel.Children.Add(propertyCombo);
        var channelButtons = new WrapPanel();
        var addChannel = new Button { Content = "Add channel", Style = FindStyle("CommandButtonStyle"), Margin = new Thickness(0, 0, 6, 6) };
        var removeChannel = new Button { Content = "Remove", Style = FindStyle("CommandButtonStyle"), Margin = new Thickness(0, 0, 0, 6) };
        channelButtons.Children.Add(addChannel);
        channelButtons.Children.Add(removeChannel);
        sidebarPanel.Children.Add(channelButtons);

        sidebarPanel.Children.Add(new TextBlock { Text = "Selected keyframe", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 7) });
        var timeBox = new TextBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        var valueBox = new TextBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        var interpolationCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<InterpolationType>(),
            MinHeight = 30,
            Margin = new Thickness(0, 0, 0, 8),
        };
        sidebarPanel.Children.Add(new TextBlock { Text = "Time (seconds)", Foreground = FindBrush("TextMutedBrush") });
        sidebarPanel.Children.Add(timeBox);
        sidebarPanel.Children.Add(new TextBlock { Text = "Value", Foreground = FindBrush("TextMutedBrush") });
        sidebarPanel.Children.Add(valueBox);
        sidebarPanel.Children.Add(new TextBlock { Text = "Interpolation", Foreground = FindBrush("TextMutedBrush") });
        sidebarPanel.Children.Add(interpolationCombo);

        var keyframeButtons = new WrapPanel();
        var addKeyframe = new Button { Content = "Add at playhead", Style = FindStyle("PrimaryButtonStyle"), Margin = new Thickness(0, 0, 6, 6) };
        var removeKeyframe = new Button { Content = "Remove", Style = FindStyle("CommandButtonStyle"), Margin = new Thickness(0, 0, 6, 6) };
        var copyKeyframes = new Button { Content = "Copy all", Style = FindStyle("CommandButtonStyle"), Margin = new Thickness(0, 0, 6, 6) };
        var pasteKeyframes = new Button { Content = "Paste", Style = FindStyle("CommandButtonStyle"), Margin = new Thickness(0, 0, 0, 6) };
        keyframeButtons.Children.Add(addKeyframe);
        keyframeButtons.Children.Add(removeKeyframe);
        keyframeButtons.Children.Add(copyKeyframes);
        keyframeButtons.Children.Add(pasteKeyframes);
        sidebarPanel.Children.Add(keyframeButtons);
        sidebar.Child = sidebarPanel;
        body.Children.Add(sidebar);

        var graphCard = new Border
        {
            Background = FindBrush("EditorPanelBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
        };
        Grid.SetColumn(graphCard, 2);
        var graphPanel = new Grid();
        graphPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        graphPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(115) });
        var graph = new AnimationGraphControl { DurationSeconds = Math.Max(0.001, _item.Duration.Seconds) };
        graphPanel.Children.Add(graph);
        var keyframeList = new ListBox { Margin = new Thickness(0, 10, 0, 0), DisplayMemberPath = nameof(KeyframeDisplay.Label) };
        Grid.SetRow(keyframeList, 1);
        graphPanel.Children.Add(keyframeList);
        graphCard.Child = graphPanel;
        body.Children.Add(graphCard);
        root.Children.Add(body);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", Style = FindStyle("CommandButtonStyle"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Apply animation", Style = FindStyle("PrimaryButtonStyle"), MinWidth = 132, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) => dialog.DialogResult = true;
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        AnimationChannel? CurrentChannel() => channelCombo.SelectedItem as AnimationChannel;
        Keyframe? CurrentKeyframe()
        {
            var channel = CurrentChannel();
            return channel != null && graph.SelectedIndex >= 0 && graph.SelectedIndex < channel.Keyframes.Count
                ? channel.Keyframes[graph.SelectedIndex]
                : null;
        }

        void RefreshChannels(AnimationChannel? select = null)
        {
            channelCombo.ItemsSource = null;
            channelCombo.ItemsSource = _channels;
            if (_channels.Count == 0)
            {
                graph.Channel = null;
                keyframeList.ItemsSource = null;
                return;
            }
            channelCombo.SelectedItem = select != null && _channels.Contains(select) ? select : _channels[0];
        }

        void RefreshKeyframes(int selectedIndex = -1)
        {
            var channel = CurrentChannel();
            graph.Channel = channel;
            if (channel == null)
            {
                keyframeList.ItemsSource = null;
                return;
            }
            graph.DurationSeconds = Math.Max(0.001, _item.Duration.Seconds);
            graph.SelectedIndex = selectedIndex >= 0 ? Math.Min(selectedIndex, channel.Keyframes.Count - 1) : graph.SelectedIndex;
            keyframeList.ItemsSource = channel.Keyframes.Select((keyframe, index) => new KeyframeDisplay(index, keyframe)).ToArray();
            keyframeList.SelectedIndex = graph.SelectedIndex;
            RefreshEditors();
        }

        void RefreshEditors()
        {
            var keyframe = CurrentKeyframe();
            timeBox.IsEnabled = keyframe != null;
            valueBox.IsEnabled = keyframe != null;
            interpolationCombo.IsEnabled = keyframe != null;
            if (keyframe == null)
            {
                timeBox.Text = string.Empty;
                valueBox.Text = string.Empty;
                interpolationCombo.SelectedItem = null;
                return;
            }
            timeBox.Text = keyframe.Time.Seconds.ToString("0.###", CultureInfo.InvariantCulture);
            valueBox.Text = keyframe.Value.ToString("0.###", CultureInfo.InvariantCulture);
            interpolationCombo.SelectedItem = keyframe.Interpolation;
        }

        channelCombo.SelectionChanged += (_, _) => RefreshKeyframes(0);
        addChannel.Click += (_, _) =>
        {
            if (propertyCombo.SelectedItem is not string propertyName) return;
            var existing = _channels.FirstOrDefault(channel => channel.PropertyName == propertyName);
            if (existing != null)
            {
                RefreshChannels(existing);
                return;
            }
            var channel = new AnimationChannel
            {
                PropertyName = propertyName,
                DefaultValue = ResolveDefaultValue(_item, propertyName),
            };
            _channels.Add(channel);
            RefreshChannels(channel);
        };
        removeChannel.Click += (_, _) =>
        {
            var channel = CurrentChannel();
            if (channel == null) return;
            _channels.Remove(channel);
            RefreshChannels();
        };
        addKeyframe.Click += (_, _) =>
        {
            var channel = CurrentChannel();
            if (channel == null) return;
            var existing = channel.Keyframes.FirstOrDefault(keyframe => Math.Abs(keyframe.Time.Seconds - _playheadLocalSeconds) < 0.0005);
            if (existing == null)
            {
                existing = new Keyframe
                {
                    Time = MediaTime.FromSeconds(_playheadLocalSeconds),
                    Value = channel.GetValueAt(MediaTime.FromSeconds(_playheadLocalSeconds)),
                    Interpolation = InterpolationType.Bezier,
                };
                channel.Keyframes.Add(existing);
                channel.Keyframes.Sort((left, right) => left.Time.Ticks.CompareTo(right.Time.Ticks));
            }
            RefreshKeyframes(channel.Keyframes.IndexOf(existing));
        };
        removeKeyframe.Click += (_, _) =>
        {
            var channel = CurrentChannel();
            var keyframe = CurrentKeyframe();
            if (channel == null || keyframe == null) return;
            var oldIndex = channel.Keyframes.IndexOf(keyframe);
            channel.Keyframes.Remove(keyframe);
            RefreshKeyframes(Math.Min(oldIndex, channel.Keyframes.Count - 1));
        };
        copyKeyframes.Click += (_, _) =>
        {
            var channel = CurrentChannel();
            _keyframeClipboard = channel == null ? [] : CloneKeyframes(channel.Keyframes);
        };
        pasteKeyframes.Click += (_, _) =>
        {
            var channel = CurrentChannel();
            if (channel == null || _keyframeClipboard.Count == 0) return;
            channel.Keyframes.Clear();
            channel.Keyframes.AddRange(CloneKeyframes(_keyframeClipboard));
            RefreshKeyframes(0);
        };
        graph.SelectedIndexChanged += (_, index) =>
        {
            keyframeList.SelectedIndex = index;
            RefreshEditors();
        };
        graph.ChannelChanged += (_, _) =>
        {
            var channel = CurrentChannel();
            if (channel == null) return;
            keyframeList.ItemsSource = channel.Keyframes.Select((keyframe, index) => new KeyframeDisplay(index, keyframe)).ToArray();
            keyframeList.SelectedIndex = graph.SelectedIndex;
            RefreshEditors();
        };
        keyframeList.SelectionChanged += (_, _) =>
        {
            if (keyframeList.SelectedItem is not KeyframeDisplay display) return;
            graph.SelectedIndex = display.Index;
            RefreshEditors();
        };
        timeBox.LostKeyboardFocus += (_, _) => ApplyEditorValues();
        valueBox.LostKeyboardFocus += (_, _) => ApplyEditorValues();
        interpolationCombo.SelectionChanged += (_, _) => ApplyEditorValues();

        void ApplyEditorValues()
        {
            var channel = CurrentChannel();
            var keyframe = CurrentKeyframe();
            if (channel == null || keyframe == null) return;
            if (double.TryParse(timeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                keyframe.Time = MediaTime.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0.001, _item.Duration.Seconds)));
            if (double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                keyframe.Value = value;
            if (interpolationCombo.SelectedItem is InterpolationType interpolation)
                keyframe.Interpolation = interpolation;
            channel.Keyframes.Sort((left, right) => left.Time.Ticks.CompareTo(right.Time.Ticks));
            RefreshKeyframes(channel.Keyframes.IndexOf(keyframe));
        }

        dialog.Content = root;
        RefreshChannels();
        return dialog.ShowDialog() == true
            ? UpdateAnimationChannelsCommand.Clone(_channels)
            : null;
    }

    private static double ResolveDefaultValue(TimelineItem item, string propertyName) => propertyName switch
    {
        AnimationPropertyNames.PositionX => item.Transform.PositionX,
        AnimationPropertyNames.PositionY => item.Transform.PositionY,
        AnimationPropertyNames.ScaleX => item.Transform.ScaleX,
        AnimationPropertyNames.ScaleY => item.Transform.ScaleY,
        AnimationPropertyNames.Rotation => item.Transform.RotationDegrees,
        AnimationPropertyNames.Opacity => item.Opacity,
        AnimationPropertyNames.Volume => item.Volume,
        AnimationPropertyNames.Pan => item.Pan,
        _ => 0,
    };

    private static List<Keyframe> CloneKeyframes(IEnumerable<Keyframe> keyframes)
    {
        var json = JsonSerializer.Serialize(keyframes);
        return JsonSerializer.Deserialize<List<Keyframe>>(json) ?? [];
    }

    private sealed record KeyframeDisplay(int Index, Keyframe Keyframe)
    {
        public string Label => $"{Index + 1}. {Keyframe.Time.Seconds:0.###}s  ·  {Keyframe.Value:0.###}  ·  {Keyframe.Interpolation}";
    }
}
