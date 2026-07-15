namespace Rushframe.Domain.Editing;

public sealed class CompositeEditCommand : IAtomicEditCommand
{
    private readonly IReadOnlyList<IEditCommand> _commands;

    public CompositeEditCommand(string description, IEnumerable<IEditCommand> commands)
    {
        Description = description;
        _commands = commands.ToArray();
    }

    public string Description { get; }

    public EditResult Execute(Sequence sequence)
    {
        var snapshot = CaptureFallbackSnapshot(sequence);
        var completedCommands = new List<IEditCommand>();
        try
        {
            foreach (var command in _commands)
            {
                var result = command.Execute(sequence);
                if (result.Success)
                {
                    completedCommands.Add(command);
                    continue;
                }

                RollBackCompletedCommands(sequence, completedCommands);
                if (snapshot != null && !snapshot.Matches(sequence)) snapshot.Restore(sequence);
                return result;
            }

            return EditResult.Ok();
        }
        catch (Exception ex)
        {
            RollBackCompletedCommands(sequence, completedCommands);
            snapshot?.Restore(sequence);
            return EditResult.Fail($"Composite edit failed: {ex.Message}");
        }
    }

    public EditResult Undo(Sequence sequence)
    {
        var snapshot = CaptureFallbackSnapshot(sequence);
        var undoneCommands = new List<IEditCommand>();
        try
        {
            for (var index = _commands.Count - 1; index >= 0; index--)
            {
                var command = _commands[index];
                var result = command.Undo(sequence);
                if (result.Success)
                {
                    undoneCommands.Add(command);
                    continue;
                }

                RollForwardUndoneCommands(sequence, undoneCommands);
                if (snapshot != null && !snapshot.Matches(sequence)) snapshot.Restore(sequence);
                return result;
            }

            return EditResult.Ok();
        }
        catch (Exception ex)
        {
            RollForwardUndoneCommands(sequence, undoneCommands);
            snapshot?.Restore(sequence);
            return EditResult.Fail($"Composite undo failed: {ex.Message}");
        }
    }

    private SequenceStateSnapshot? CaptureFallbackSnapshot(Sequence sequence) =>
        _commands.All(command => command is IAtomicEditCommand)
            ? null
            : SequenceStateSnapshot.Capture(sequence);

    private static void RollBackCompletedCommands(Sequence sequence, IEnumerable<IEditCommand> commands)
    {
        foreach (var command in commands.Reverse())
        {
            try { command.Undo(sequence); }
            catch { }
        }
    }

    private static void RollForwardUndoneCommands(Sequence sequence, IEnumerable<IEditCommand> commands)
    {
        foreach (var command in commands.Reverse())
        {
            try { command.Execute(sequence); }
            catch { }
        }
    }
}
