namespace Rushframe.Domain.Editing;

public sealed class UpdateAnimationChannelsCommand : IAtomicEditCommand
{
    public required TimelineItemId ItemId { get; init; }
    public required IReadOnlyList<AnimationChannel> NewChannels { get; init; }

    private List<AnimationChannel>? _oldChannels;

    public string Description => $"Update animation channels on {ItemId}";

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate =>
            candidate.Items.Any(item => item.Id == ItemId));
        var item = track?.Items.FirstOrDefault(candidate => candidate.Id == ItemId);
        if (item == null) return EditResult.Fail(new ItemNotFoundError(ItemId));
        if (track!.Locked) return EditResult.Fail("Track is locked");
        if (item.Locked) return EditResult.Fail("Item is locked");

        var replacementChannels = Clone(NewChannels);
        _oldChannels = Clone(item.AnimationChannels);
        item.AnimationChannels.Clear();
        item.AnimationChannels.AddRange(replacementChannels);
        item.InvalidateAnimationChannelCache();
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (_oldChannels == null) return EditResult.Fail("No animation snapshot is available.");
        var item = sequence.Tracks.SelectMany(track => track.Items)
            .FirstOrDefault(candidate => candidate.Id == ItemId);
        if (item == null) return EditResult.Fail(new ItemNotFoundError(ItemId));

        item.AnimationChannels.Clear();
        item.AnimationChannels.AddRange(Clone(_oldChannels));
        item.InvalidateAnimationChannelCache();
        return EditResult.Ok();
    }

    public static List<AnimationChannel> Clone(IEnumerable<AnimationChannel> channels) =>
        channels.Select(channel => new AnimationChannel
        {
            PropertyName = channel.PropertyName,
            DefaultValue = channel.DefaultValue,
            Keyframes = channel.Keyframes.Select(keyframe => new Keyframe
            {
                Id = keyframe.Id,
                Time = keyframe.Time,
                Value = keyframe.Value,
                Interpolation = keyframe.Interpolation,
                InTangentX = keyframe.InTangentX,
                InTangentY = keyframe.InTangentY,
                OutTangentX = keyframe.OutTangentX,
                OutTangentY = keyframe.OutTangentY,
            }).ToList(),
        }).ToList();
}
