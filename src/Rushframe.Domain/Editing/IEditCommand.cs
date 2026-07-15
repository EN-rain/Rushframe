namespace Rushframe.Domain.Editing;

public interface IEditCommand
{
    string Description { get; }
    EditResult Execute(Sequence sequence);
    EditResult Undo(Sequence sequence);
}

/// <summary>
/// Marks production commands that validate every target before mutation and either
/// succeed completely or return failure without changing sequence state. The undo
/// stack can execute these commands without taking a full defensive sequence snapshot.
/// </summary>
public interface IAtomicEditCommand : IEditCommand;

public sealed class EditResult
{
    public bool Success { get; init; }
    public DomainError? Error { get; init; }
    public string? ErrorMessage => Error?.Message;
    public List<string> Warnings { get; init; } = [];

    public static EditResult Ok() => new() { Success = true };
    public static EditResult Fail(DomainError error) => new() { Error = error };
    public static EditResult Fail(string message) => new() { Error = new InvalidOperationError(message) };
}
