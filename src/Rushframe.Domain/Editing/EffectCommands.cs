namespace Rushframe.Domain.Editing;

public sealed class AddEffectCommand : IEditCommand
{
    public string Description => $"Add effect {EffectTypeId} to {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public required string EffectTypeId { get; init; }
    public bool Enabled { get; init; } = true;
    public Dictionary<string, object> Parameters { get; init; } = [];

    private EffectInstance? _added;
    private int _insertIndex = -1;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            _added ??= new EffectInstance
            {
                EffectTypeId = EffectTypeId,
                Enabled = Enabled,
                Parameters = new Dictionary<string, object>(Parameters),
            };
            if (item.Effects.Any(effect => effect.Id == _added.Id))
                return EditResult.Fail("Effect is already present");

            _insertIndex = _insertIndex < 0 ? item.Effects.Count : Math.Clamp(_insertIndex, 0, item.Effects.Count);
            item.Effects.Insert(_insertIndex, _added);
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_added == null) return EditResult.Fail("Nothing to undo");

        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            var index = item.Effects.FindIndex(effect => effect.Id == _added.Id);
            if (index < 0) return EditResult.Fail("Effect not found");
            _insertIndex = index;
            item.Effects.RemoveAt(index);
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}

public sealed class RemoveEffectCommand : IEditCommand
{
    public string Description => $"Remove effect {EffectInstanceId}";

    public required TimelineItemId ItemId { get; init; }
    public required EffectInstanceId EffectInstanceId { get; init; }

    private EffectInstance? _removed;
    private int _removedIndex = -1;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            var idx = item.Effects.FindIndex(e => e.Id == EffectInstanceId);
            if (idx < 0) return EditResult.Fail("Effect not found");

            _removedIndex = idx;
            _removed = item.Effects[idx];
            item.Effects.RemoveAt(idx);
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_removed == null) return EditResult.Fail("Nothing to undo");

        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            item.Effects.Insert(Math.Clamp(_removedIndex, 0, item.Effects.Count), _removed);
            _removed = null;
            _removedIndex = -1;
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}

public sealed class UpdateEffectCommand : IEditCommand
{
    public string Description => $"Update effect {EffectInstanceId}";

    public required TimelineItemId ItemId { get; init; }
    public required EffectInstanceId EffectInstanceId { get; init; }
    public required bool Enabled { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = [];

    private bool _oldEnabled;
    private Dictionary<string, object>? _oldParameters;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            var effect = item.Effects.FirstOrDefault(e => e.Id == EffectInstanceId);
            if (effect == null) return EditResult.Fail("Effect not found");

            _oldEnabled = effect.Enabled;
            _oldParameters = new Dictionary<string, object>(effect.Parameters);
            effect.Enabled = Enabled;
            effect.Parameters.Clear();
            foreach (var pair in Parameters) effect.Parameters[pair.Key] = pair.Value;
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldParameters == null) return EditResult.Fail("Nothing to undo");

        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            var effect = item.Effects.FirstOrDefault(e => e.Id == EffectInstanceId);
            if (effect == null) return EditResult.Fail("Effect not found");

            effect.Enabled = _oldEnabled;
            effect.Parameters.Clear();
            foreach (var pair in _oldParameters) effect.Parameters[pair.Key] = pair.Value;
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}

public sealed class ReorderEffectCommand : IEditCommand
{
    public string Description => $"Reorder effect {EffectInstanceId}";

    public required TimelineItemId ItemId { get; init; }
    public required EffectInstanceId EffectInstanceId { get; init; }
    public required int NewIndex { get; init; }

    private int _oldIndex;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            var idx = item.Effects.FindIndex(e => e.Id == EffectInstanceId);
            if (idx < 0) return EditResult.Fail("Effect not found");

            _oldIndex = idx;
            var effect = item.Effects[idx];
            item.Effects.RemoveAt(idx);
            item.Effects.Insert(Math.Clamp(NewIndex, 0, item.Effects.Count), effect);
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            var idx = item.Effects.FindIndex(e => e.Id == EffectInstanceId);
            if (idx < 0) continue;

            var effect = item.Effects[idx];
            item.Effects.RemoveAt(idx);
            item.Effects.Insert(Math.Min(_oldIndex, item.Effects.Count), effect);
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}
