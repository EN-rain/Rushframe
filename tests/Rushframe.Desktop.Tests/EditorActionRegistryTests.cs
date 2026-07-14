using System.Windows.Input;
using Rushframe.Desktop.Commands;

namespace Rushframe.Desktop.Tests;

public sealed class EditorActionRegistryTests
{
    [Theory]
    [InlineData("Ctrl+S", Key.S, ModifierKeys.Control)]
    [InlineData("Ctrl+Shift+Z", Key.Z, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Space", Key.Space, ModifierKeys.None)]
    [InlineData("Delete", Key.Delete, ModifierKeys.None)]
    public void ParseGesture_reads_supported_shortcuts(string text, Key key, ModifierKeys modifiers)
    {
        var gesture = EditorActionRegistry.ParseGesture(text);

        Assert.NotNull(gesture);
        Assert.Equal(key, gesture!.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
    }

    [Fact]
    public void Default_shortcuts_are_unique()
    {
        var registry = new EditorActionRegistry();
        var duplicates = registry.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.DefaultShortcut))
            .GroupBy(action => action.DefaultShortcut, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Override_replaces_default_text()
    {
        var registry = new EditorActionRegistry();
        var action = registry.Actions.Single(candidate => candidate.Id == "edit.split");

        var shortcut = registry.ResolveShortcutText(
            action,
            new Dictionary<string, string> { ["edit.split"] = "S" });

        Assert.Equal("S", shortcut);
    }
}
