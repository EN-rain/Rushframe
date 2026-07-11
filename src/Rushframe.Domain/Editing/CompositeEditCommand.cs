namespace Rushframe.Domain.Editing;

public sealed class CompositeEditCommand : IEditCommand
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
        var executed = new List<IEditCommand>(_commands.Count);
        foreach (var command in _commands)
        {
            var result = command.Execute(sequence);
            if (result.Success)
            {
                executed.Add(command);
                continue;
            }

            for (var index = executed.Count - 1; index >= 0; index--)
                executed[index].Undo(sequence);

            return result;
        }

        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        for (var index = _commands.Count - 1; index >= 0; index--)
        {
            var result = _commands[index].Undo(sequence);
            if (!result.Success) return result;
        }

        return EditResult.Ok();
    }
}
