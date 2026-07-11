namespace Rushframe.Domain.Editing;

public sealed class AddEffectCommand : IEditCommand
{
    public string Description => $"Add effect {EffectTypeId} to {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public required string EffectTypeId { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = [];

    private EffectInstance? _added;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            _added = new EffectInstance
            {
                EffectTypeId = EffectTypeId,
                Parameters = new Dictionary<string, object>(Parameters),
            };
            item.Effects.Add(_added);
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

            item.Effects.Remove(_added);
            _added = null;
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

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            var idx = item.Effects.FindIndex(e => e.Id == EffectInstanceId);
            if (idx < 0) return EditResult.Fail("Effect not found");

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

            item.Effects.Add(_removed);
            _removed = null;
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
