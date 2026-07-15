namespace Rushframe.Domain.Editing;

public sealed class UndoRedoStack
{
    private readonly LinkedList<IEditCommand> _undoStack = [];
    private readonly LinkedList<IEditCommand> _redoStack = [];
    private const int MaxUndoDepth = 100;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public EditResult Execute(Sequence sequence, IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(command);
        var snapshot = command is IAtomicEditCommand ? null : SequenceStateSnapshot.Capture(sequence);
        var result = InvokeSafely(() => command.Execute(sequence), snapshot, sequence, "execute");
        if (!result.Success) return result;

        _undoStack.AddLast(command);
        if (_undoStack.Count > MaxUndoDepth)
            _undoStack.RemoveFirst();
        _redoStack.Clear();
        return result;
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_undoStack.Count == 0)
            return EditResult.Fail("Nothing to undo");

        var command = _undoStack.Last!.Value;
        var snapshot = command is IAtomicEditCommand ? null : SequenceStateSnapshot.Capture(sequence);
        var result = InvokeSafely(() => command.Undo(sequence), snapshot, sequence, "undo");
        if (!result.Success) return result;

        _undoStack.RemoveLast();
        _redoStack.AddLast(command);
        return result;
    }

    public EditResult Redo(Sequence sequence)
    {
        if (_redoStack.Count == 0)
            return EditResult.Fail("Nothing to redo");

        var command = _redoStack.Last!.Value;
        var snapshot = command is IAtomicEditCommand ? null : SequenceStateSnapshot.Capture(sequence);
        var result = InvokeSafely(() => command.Execute(sequence), snapshot, sequence, "redo");
        if (!result.Success) return result;

        _redoStack.RemoveLast();
        _undoStack.AddLast(command);
        if (_undoStack.Count > MaxUndoDepth)
            _undoStack.RemoveFirst();
        return result;
    }

    private static EditResult InvokeSafely(
        Func<EditResult> operation,
        SequenceStateSnapshot? snapshot,
        Sequence sequence,
        string operationName)
    {
        try
        {
            var result = operation();
            if (!result.Success && snapshot != null && !snapshot.Matches(sequence))
                snapshot.Restore(sequence);
            return result;
        }
        catch (Exception ex)
        {
            snapshot?.Restore(sequence);
            return EditResult.Fail($"Command {operationName} failed: {ex.Message}");
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
