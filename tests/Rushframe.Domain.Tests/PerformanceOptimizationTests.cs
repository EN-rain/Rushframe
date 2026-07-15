using Rushframe.Domain;

namespace Rushframe.Domain.Tests;

public sealed class PerformanceOptimizationTests
{
    [Fact]
    public void AnimationChannel_UnsortedKeyframes_UsesCorrectInterpolationAndInvalidatesOnMutation()
    {
        var channel = new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = -1,
            Keyframes =
            {
                new Keyframe { Time = MediaTime.FromSeconds(2), Value = 20 },
                new Keyframe { Time = MediaTime.Zero, Value = 0 },
                new Keyframe { Time = MediaTime.FromSeconds(1), Value = 10 },
            },
        };

        Assert.Equal(5, channel.GetValueAt(MediaTime.FromSeconds(0.5)), precision: 6);
        Assert.Equal(15, channel.GetValueAt(MediaTime.FromSeconds(1.5)), precision: 6);

        channel.Keyframes[1].Value = 4;

        Assert.Equal(7, channel.GetValueAt(MediaTime.FromSeconds(0.5)), precision: 6);
    }

    [Fact]
    public void AnimationChannel_WarmedSteadyState_HasNegligibleAllocations()
    {
        var channel = new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.Opacity,
            DefaultValue = 1,
        };
        for (var index = 0; index < 100; index++)
        {
            channel.Keyframes.Add(new Keyframe
            {
                Time = MediaTime.FromSeconds(index / 10.0),
                Value = index / 100.0,
            });
        }

        var sampleTimes = new MediaTime[990];
        for (var index = 0; index < sampleTimes.Length; index++)
            sampleTimes[index] = MediaTime.FromSeconds(index / 100.0);
        for (var index = 0; index < 10_000; index++)
            _ = channel.GetValueAt(sampleTimes[index % sampleTimes.Length]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        double sum = 0;
        for (var index = 0; index < 10_000; index++)
            sum += channel.GetValueAt(sampleTimes[index % sampleTimes.Length]);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sum > 0);
        Assert.True(allocated <= 256, $"Expected zero/near-zero steady-state allocation, observed {allocated} bytes.");
    }

    [Fact]
    public void AnimationChannel_UnrelatedKeyframeMutations_DoNotRebuildOrAllocate()
    {
        var channel = new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.Opacity,
            DefaultValue = 1,
        };
        for (var index = 0; index < 100; index++)
        {
            channel.Keyframes.Add(new Keyframe
            {
                Time = MediaTime.FromSeconds(index / 10.0),
                Value = index / 100.0,
            });
        }
        var unrelated = new Keyframe { Time = MediaTime.Zero, Value = 0 };

        _ = channel.GetValueAt(MediaTime.FromSeconds(2.5));
        unrelated.Value = 1;
        _ = channel.GetValueAt(MediaTime.FromSeconds(2.5));

        var before = GC.GetAllocatedBytesForCurrentThread();
        double sum = 0;
        for (var index = 0; index < 100; index++)
        {
            unrelated.Value = index + 2;
            sum += channel.GetValueAt(MediaTime.FromSeconds((index % 90) / 10.0));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sum > 0);
        Assert.True(allocated <= 256, $"Unrelated keyframe mutations rebuilt the cache and allocated {allocated} bytes.");
    }

    [Fact]
    public void SequenceDuration_CacheInvalidatesWhenTimingOrItemCountChanges()
    {
        var sequence = new Sequence();
        var track = new Track { Kind = TrackKind.Video };
        var first = new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.FromSeconds(1),
            Duration = MediaTime.FromSeconds(2),
        };
        track.Items.Add(first);
        sequence.Tracks.Add(track);

        Assert.Equal(3, sequence.Duration.Seconds, precision: 6);
        Assert.Equal(3, sequence.Duration.Seconds, precision: 6);

        first.Duration = MediaTime.FromSeconds(5);
        Assert.Equal(6, sequence.Duration.Seconds, precision: 6);

        track.Items.Add(new TimelineItem
        {
            Kind = ItemKind.Clip,
            TimelineStart = MediaTime.FromSeconds(10),
            Duration = MediaTime.FromSeconds(2),
        });
        Assert.Equal(12, sequence.Duration.Seconds, precision: 6);

        track.Items.RemoveAt(1);
        Assert.Equal(6, sequence.Duration.Seconds, precision: 6);
    }

    [Fact]
    public void TimelineItem_ChannelLookupCache_TracksReplacedChannelSet()
    {
        var item = new TimelineItem { Kind = ItemKind.Clip };
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = 2,
        });
        Assert.Equal(2, item.GetAnimatedValue(AnimationPropertyNames.PositionX, MediaTime.Zero, 0));

        item.AnimationChannels.Clear();
        item.AnimationChannels.Add(new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = 9,
        });
        item.InvalidateAnimationChannelCache();

        Assert.Equal(9, item.GetAnimatedValue(AnimationPropertyNames.PositionX, MediaTime.Zero, 0));
    }
}
