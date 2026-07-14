using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Rushframe.Domain;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

[MemoryDiagnoser]
public class AnimationChannelBenchmarks
{
    private AnimationChannel _channel = null!;
    private MediaTime[] _times = null!;
    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        _channel = new AnimationChannel
        {
            PropertyName = AnimationPropertyNames.PositionX,
            DefaultValue = 0,
        };
        for (var index = 99; index >= 0; index--)
        {
            _channel.Keyframes.Add(new Keyframe
            {
                Time = MediaTime.FromSeconds(index * 0.1),
                Value = Math.Sin(index * 0.1) * 100,
                Interpolation = index % 8 == 0 ? InterpolationType.Bezier : InterpolationType.Linear,
            });
        }
        _times = Enumerable.Range(0, 990)
            .Select(index => MediaTime.FromSeconds(index / 100.0))
            .ToArray();
        _ = _channel.GetValueAt(MediaTime.Zero);
    }

    [Benchmark(Baseline = true)]
    public double SequentialLookup()
    {
        var time = _times[_index++ % _times.Length];
        return _channel.GetValueAt(time);
    }

    [Benchmark]
    public double ReverseLookup()
    {
        var time = _times[_times.Length - 1 - (_index++ % _times.Length)];
        return _channel.GetValueAt(time);
    }
}

[MemoryDiagnoser]
public class SequenceDurationBenchmarks
{
    private Sequence _sequence = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sequence = new Sequence();
        for (var trackIndex = 0; trackIndex < 20; trackIndex++)
        {
            var track = new Track { Kind = TrackKind.Video };
            for (var itemIndex = 0; itemIndex < 60; itemIndex++)
            {
                track.Items.Add(new TimelineItem
                {
                    Kind = ItemKind.Clip,
                    TimelineStart = MediaTime.FromSeconds(itemIndex * 1.75),
                    Duration = MediaTime.FromSeconds(1.5),
                });
            }
            _sequence.Tracks.Add(track);
        }
        _ = _sequence.Duration;
    }

    [Benchmark]
    public MediaTime CachedDuration() => _sequence.Duration;

    [Benchmark]
    public MediaTime DurationAfterMutation()
    {
        var item = _sequence.Tracks[0].Items[0];
        item.Duration = item.Duration == MediaTime.FromSeconds(1.5)
            ? MediaTime.FromSeconds(1.6)
            : MediaTime.FromSeconds(1.5);
        return _sequence.Duration;
    }
}
