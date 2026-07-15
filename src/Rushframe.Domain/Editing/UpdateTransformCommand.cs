namespace Rushframe.Domain.Editing;

public sealed class UpdateTransformCommand : IAtomicEditCommand
{
    public required TimelineItemId ItemId { get; init; }
    public required Transform2D NewTransform { get; init; }

    private Transform2D? _oldTransform;

    public string Description => $"Transform clip {ItemId}";

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate =>
            candidate.Items.Any(item => item.Id == ItemId));
        var item = track?.Items.FirstOrDefault(candidate => candidate.Id == ItemId);
        if (item == null) return EditResult.Fail(new ItemNotFoundError(ItemId));
        if (track!.Locked) return EditResult.Fail("Track is locked");
        if (item.Locked) return EditResult.Fail("Item is locked");

        _oldTransform = Clone(item.Transform);
        Apply(item.Transform, NewTransform);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldTransform == null) return EditResult.Fail("No transform snapshot is available.");
        var item = sequence.Tracks.SelectMany(track => track.Items)
            .FirstOrDefault(candidate => candidate.Id == ItemId);
        if (item == null) return EditResult.Fail(new ItemNotFoundError(ItemId));

        Apply(item.Transform, _oldTransform);
        return EditResult.Ok();
    }

    private static Transform2D Clone(Transform2D transform) => new()
    {
        PositionX = transform.PositionX,
        PositionY = transform.PositionY,
        ScaleX = transform.ScaleX,
        ScaleY = transform.ScaleY,
        RotationDegrees = transform.RotationDegrees,
        AnchorX = transform.AnchorX,
        AnchorY = transform.AnchorY,
    };

    private static void Apply(Transform2D target, Transform2D source)
    {
        target.PositionX = source.PositionX;
        target.PositionY = source.PositionY;
        target.ScaleX = source.ScaleX;
        target.ScaleY = source.ScaleY;
        target.RotationDegrees = source.RotationDegrees;
        target.AnchorX = source.AnchorX;
        target.AnchorY = source.AnchorY;
    }
}
