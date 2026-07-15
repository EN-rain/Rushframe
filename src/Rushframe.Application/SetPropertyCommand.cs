using Rushframe.Domain;

namespace Rushframe.Application;

public sealed class SetPropertyCommand : Domain.Editing.IAtomicEditCommand
{
    public string Description => $"Set {PropertyName} on {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public required string PropertyName { get; init; }
    public required object? NewValue { get; init; }
    public required Func<TimelineItem, object?> Getter { get; init; }
    public required Action<TimelineItem, object?> Setter { get; init; }

    private object? _oldValue;

    public Domain.Editing.EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;
            if (track.Locked) return Domain.Editing.EditResult.Fail("Track is locked");
            if (item.Locked) return Domain.Editing.EditResult.Fail("Item is locked");

            _oldValue = Getter(item);
            try
            {
                Setter(item, NewValue);
            }
            catch
            {
                try { Setter(item, _oldValue); } catch { }
                throw;
            }
            return Domain.Editing.EditResult.Ok();
        }

        return Domain.Editing.EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public Domain.Editing.EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            Setter(item, _oldValue);
            return Domain.Editing.EditResult.Ok();
        }

        return Domain.Editing.EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}
