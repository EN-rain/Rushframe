namespace Rushframe.Domain.Editing;

public sealed class SplitClipCommand : IAtomicEditCommand
{
    public string Description => $"Split clip {ItemId} at {SplitTime.Seconds:F2}s";

    public required TrackId TrackId { get; init; }
    public required TimelineItemId ItemId { get; init; }
    public required MediaTime SplitTime { get; init; }

    private MediaTime _originalDuration;
    private MediaTime _originalSourceStart;
    private MediaTime _originalSourceDuration;
    private TimelineItem? _rightItem;
    private List<AnimationChannel>? _originalChannels;
    private List<AnimationChannel>? _leftChannels;
    private List<AnimationChannel>? _rightChannels;
    private AnimatedProperty? _originalLegacyAnimation;
    private AnimatedProperty? _leftLegacyAnimation;
    private AnimatedProperty? _rightLegacyAnimation;
    private readonly List<(int Index, Transition Original, Transition Replacement)> _replacedTransitions = [];
    private readonly Dictionary<Transition, Transition> _transitionReplacements = new();

    public EditResult Execute(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail("Track not found");
        if (track.Locked) return EditResult.Fail("Track is locked");

        var index = track.Items.FindIndex(candidate => candidate.Id == ItemId);
        if (index < 0) return EditResult.Fail("Item not found");
        var item = track.Items[index];
        if (item.Locked) return EditResult.Fail("Item is locked");
        if (item.SpeedCurve?.Segments.Count > 0)
            return EditResult.Fail("Split does not support segmented speed curves until exact ramp rendering is available");
        if (SplitTime <= item.TimelineStart || SplitTime >= item.TimelineStart.Add(item.Duration))
            return EditResult.Fail("Split time is outside item bounds");

        var offset = SplitTime.Subtract(item.TimelineStart);
        var remaining = item.Duration.Subtract(offset);
        var speed = Math.Clamp(item.SpeedCurve?.ConstantSpeed ?? item.Speed, 0.1, 100);
        var leftSourceDuration = MediaTime.FromSeconds(offset.Seconds * speed);
        var rightSourceDuration = MediaTime.FromSeconds(remaining.Seconds * speed);
        if (item.SourceDuration.Seconds > 0
            && leftSourceDuration.Add(rightSourceDuration).Seconds > item.SourceDuration.Seconds + 0.001)
            return EditResult.Fail("Split exceeds the available source duration");

        _originalDuration = item.Duration;
        _originalSourceStart = item.SourceStart;
        _originalSourceDuration = item.SourceDuration;
        CaptureAnimationPartitions(item, offset);

        _rightItem ??= TimelineItemCloner.Clone(item, SplitTime);
        _rightItem.TimelineStart = SplitTime;
        _rightItem.Duration = remaining;
        _rightItem.SourceDuration = rightSourceDuration;
        if (item.Reversed)
        {
            var availableDuration = item.SourceDuration.Seconds > 0
                ? item.SourceDuration
                : leftSourceDuration.Add(rightSourceDuration);
            item.SourceStart = _originalSourceStart.Add(availableDuration.Subtract(leftSourceDuration));
            item.SourceDuration = leftSourceDuration;
            _rightItem.SourceStart = _originalSourceStart;
        }
        else
        {
            item.SourceStart = _originalSourceStart;
            item.SourceDuration = leftSourceDuration;
            _rightItem.SourceStart = _originalSourceStart.Add(leftSourceDuration);
        }
        item.Duration = offset;
        ApplyAnimationState(item, _leftChannels!, _leftLegacyAnimation);
        ApplyAnimationState(_rightItem, _rightChannels!, _rightLegacyAnimation);
        track.Items.Insert(index + 1, _rightItem);

        _replacedTransitions.Clear();
        for (var transitionIndex = 0; transitionIndex < sequence.Transitions.Count; transitionIndex++)
        {
            var original = sequence.Transitions[transitionIndex];
            if (original.LeftItemId != item.Id) continue;
            if (!_transitionReplacements.TryGetValue(original, out var replacement))
            {
                replacement = new Transition
                {
                    LeftItemId = _rightItem.Id,
                    RightItemId = original.RightItemId,
                    Kind = original.Kind,
                    Duration = original.Duration,
                    Alignment = original.Alignment,
                    AudioMode = original.AudioMode,
                };
                _transitionReplacements[original] = replacement;
            }
            sequence.Transitions[transitionIndex] = replacement;
            _replacedTransitions.Add((transitionIndex, original, replacement));
        }

        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Id == TrackId);
        if (track == null) return EditResult.Fail("Track not found");
        var original = track.Items.FirstOrDefault(candidate => candidate.Id == ItemId);
        if (original == null) return EditResult.Fail("Original item not found");
        if (_rightItem == null || !track.Items.Contains(_rightItem))
            return EditResult.Fail("Split item not found");
        if (_originalChannels == null) return EditResult.Fail("Split animation state is unavailable");

        track.Items.Remove(_rightItem);
        original.Duration = _originalDuration;
        original.SourceStart = _originalSourceStart;
        original.SourceDuration = _originalSourceDuration;
        ApplyAnimationState(original, _originalChannels, _originalLegacyAnimation);

        foreach (var (transitionIndex, prior, replacement) in _replacedTransitions.OrderBy(entry => entry.Index))
        {
            var currentIndex = sequence.Transitions.IndexOf(replacement);
            if (currentIndex >= 0) sequence.Transitions.RemoveAt(currentIndex);
            sequence.Transitions.Insert(Math.Min(transitionIndex, sequence.Transitions.Count), prior);
        }
        _replacedTransitions.Clear();
        return EditResult.Ok();
    }

    private void CaptureAnimationPartitions(TimelineItem item, MediaTime offset)
    {
        if (_originalChannels != null) return;
        _originalChannels = UpdateAnimationChannelsCommand.Clone(item.AnimationChannels);
        _leftChannels = _originalChannels.Select(channel => PartitionChannel(channel, offset, right: false)).ToList();
        _rightChannels = _originalChannels.Select(channel => PartitionChannel(channel, offset, right: true)).ToList();
        _originalLegacyAnimation = CloneLegacy(item.AnimatedProperty);
        _leftLegacyAnimation = PartitionLegacy(_originalLegacyAnimation, offset, right: false);
        _rightLegacyAnimation = PartitionLegacy(_originalLegacyAnimation, offset, right: true);
    }

    private static AnimationChannel PartitionChannel(AnimationChannel source, MediaTime offset, bool right)
    {
        var splitValue = source.GetValueAt(offset);
        var channel = new AnimationChannel
        {
            PropertyName = source.PropertyName,
            DefaultValue = right ? splitValue : source.DefaultValue,
        };
        var selected = source.Keyframes
            .Where(keyframe => right ? keyframe.Time >= offset : keyframe.Time <= offset)
            .OrderBy(keyframe => keyframe.Time.Ticks)
            .Select(keyframe => CloneKeyframe(keyframe, right ? keyframe.Time.Subtract(offset) : keyframe.Time))
            .ToList();
        if (right && (selected.Count == 0 || selected[0].Time != MediaTime.Zero))
            selected.Insert(0, BoundaryKeyframe(MediaTime.Zero, splitValue));
        if (!right && (selected.Count == 0 || selected[^1].Time != offset))
            selected.Add(BoundaryKeyframe(offset, splitValue));
        channel.Keyframes.AddRange(selected);
        return channel;
    }

    private static AnimatedProperty? PartitionLegacy(AnimatedProperty? source, MediaTime offset, bool right)
    {
        if (source == null) return null;
        var partition = PartitionChannel(source, offset, right);
        var result = new AnimatedProperty
        {
            PropertyName = partition.PropertyName,
            DefaultValue = partition.DefaultValue,
        };
        result.Keyframes.AddRange(partition.Keyframes);
        return result;
    }

    private static Keyframe CloneKeyframe(Keyframe source, MediaTime time) => new()
    {
        Id = source.Id,
        Time = time,
        Value = source.Value,
        Interpolation = source.Interpolation,
        InTangentX = source.InTangentX,
        InTangentY = source.InTangentY,
        OutTangentX = source.OutTangentX,
        OutTangentY = source.OutTangentY,
    };

    private static Keyframe BoundaryKeyframe(MediaTime time, double value) => new()
    {
        Time = time,
        Value = value,
        Interpolation = InterpolationType.Linear,
    };

    private static AnimatedProperty? CloneLegacy(AnimatedProperty? source)
    {
        if (source == null) return null;
        var clone = new AnimatedProperty
        {
            PropertyName = source.PropertyName,
            DefaultValue = source.DefaultValue,
        };
        clone.Keyframes.AddRange(source.Keyframes.Select(keyframe => CloneKeyframe(keyframe, keyframe.Time)));
        return clone;
    }

    private static void ApplyAnimationState(
        TimelineItem item,
        IEnumerable<AnimationChannel> channels,
        AnimatedProperty? legacy)
    {
        item.AnimationChannels.Clear();
        item.AnimationChannels.AddRange(UpdateAnimationChannelsCommand.Clone(channels));
        item.AnimatedProperty = CloneLegacy(legacy);
        item.InvalidateAnimationChannelCache();
    }
}
