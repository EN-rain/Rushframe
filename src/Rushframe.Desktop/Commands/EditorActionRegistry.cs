using System.Windows;
using System.Windows.Input;

namespace Rushframe.Desktop.Commands;

public sealed record EditorActionDefinition(
    string Id,
    string Name,
    string Category,
    string DefaultShortcut,
    RoutedUICommand? Command = null);

public sealed class EditorActionRegistry
{
    private readonly IReadOnlyList<EditorActionDefinition> _actions =
    [
        new("project.new", "New Project", "Project", "Ctrl+N", EditorCommands.NewProject),
        new("project.open", "Open Project", "Project", "Ctrl+O", EditorCommands.OpenProject),
        new("project.save", "Save Project", "Project", "Ctrl+S", EditorCommands.SaveProject),
        new("project.import", "Import Media", "Project", "Ctrl+I", EditorCommands.ImportMedia),
        new("project.render", "Render Timeline", "Project", "Ctrl+R", EditorCommands.Render),
        new("edit.cut", "Cut", "Edit", "Ctrl+X", EditorCommands.Cut),
        new("edit.copy", "Copy", "Edit", "Ctrl+C", EditorCommands.Copy),
        new("edit.paste", "Paste", "Edit", "Ctrl+V", EditorCommands.Paste),
        new("edit.split", "Split Clip", "Edit", "Ctrl+B", EditorCommands.SplitClip),
        new("edit.delete", "Delete Selection", "Edit", "Delete", EditorCommands.DeleteClip),
        new("edit.duplicate", "Duplicate Selection", "Edit", "Ctrl+D", EditorCommands.Duplicate),
        new("edit.undo", "Undo", "History", "Ctrl+Z", EditorCommands.Undo),
        new("edit.redo", "Redo", "History", "Ctrl+Y", EditorCommands.Redo),
        new("insert.text", "Add Text", "Insert", "Ctrl+T", EditorCommands.AddText),
        new("application.settings", "Settings", "Application", "Ctrl+,", EditorCommands.Settings),
        new("preview.play-pause", "Play/Pause Preview", "Preview", "Space"),
        new("preview.previous-frame", "Previous Frame", "Preview", "Left"),
        new("preview.next-frame", "Next Frame", "Preview", "Right"),
        new("preview.mark-in", "Set Mark In", "Preview", "I"),
        new("preview.mark-out", "Set Mark Out", "Preview", "O"),
        new("preview.seek-backward", "Seek Backward", "Preview", "J"),
        new("preview.pause", "Pause Preview", "Preview", "K"),
        new("preview.play", "Play Preview", "Preview", "L"),
    ];

    private readonly List<InputBinding> _installedBindings = [];

    public IReadOnlyList<EditorActionDefinition> Actions => _actions;

    public void ApplyInputBindings(Window window, IReadOnlyDictionary<string, string>? overrides)
    {
        foreach (var binding in _installedBindings)
            window.InputBindings.Remove(binding);
        _installedBindings.Clear();

        foreach (var action in _actions.Where(action => action.Command != null))
        {
            var gesture = ResolveGesture(action, overrides);
            if (gesture == null) continue;
            var binding = new KeyBinding(action.Command!, gesture);
            window.InputBindings.Add(binding);
            _installedBindings.Add(binding);
        }
    }

    public bool Matches(string actionId, KeyEventArgs args, IReadOnlyDictionary<string, string>? overrides)
    {
        var action = _actions.FirstOrDefault(candidate => candidate.Id == actionId);
        if (action == null) return false;
        var gesture = ResolveGesture(action, overrides);
        if (gesture == null) return false;
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        return key == gesture.Key && Keyboard.Modifiers == gesture.Modifiers;
    }

    public string ResolveShortcutText(EditorActionDefinition action, IReadOnlyDictionary<string, string>? overrides) =>
        overrides != null && overrides.TryGetValue(action.Id, out var configured)
            ? configured
            : action.DefaultShortcut;

    public static KeyGesture? ParseGesture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return new KeyGestureConverter().ConvertFromInvariantString(value.Trim()) as KeyGesture;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private KeyGesture? ResolveGesture(EditorActionDefinition action, IReadOnlyDictionary<string, string>? overrides) =>
        ParseGesture(ResolveShortcutText(action, overrides));
}
