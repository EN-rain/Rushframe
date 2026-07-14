namespace Rushframe.Domain;

public enum InterpolationType { Hold, Linear, EaseIn, EaseOut, EaseInOut, Bezier }

public static class AnimationPropertyNames
{
    public const string PositionX = "positionX";
    public const string PositionY = "positionY";
    public const string ScaleX = "scaleX";
    public const string ScaleY = "scaleY";
    public const string Rotation = "rotation";
    public const string Opacity = "opacity";
    public const string Volume = "volume";
    public const string Pan = "pan";
}

public sealed class Keyframe
{
    private static long _globalMutationVersion;
    private long _mutationVersion;
    private MediaTime _time;
    private double _value;
    private InterpolationType _interpolation = InterpolationType.Linear;
    private double _inTangentX = 0.75;
    private double _inTangentY = 0.75;
    private double _outTangentX = 0.25;
    private double _outTangentY = 0.25;

    public KeyframeId Id { get; init; } = KeyframeId.New();
    public required MediaTime Time
    {
        get => _time;
        set { if (_time == value) return; _time = value; MarkMutated(); }
    }
    public required double Value
    {
        get => _value;
        set { if (_value.Equals(value)) return; _value = value; MarkMutated(); }
    }
    public InterpolationType Interpolation
    {
        get => _interpolation;
        set { if (_interpolation == value) return; _interpolation = value; MarkMutated(); }
    }

    // Tangents are normalized cubic-bezier control points for the segment.
    public double InTangentX
    {
        get => _inTangentX;
        set { if (_inTangentX.Equals(value)) return; _inTangentX = value; MarkMutated(); }
    }
    public double InTangentY
    {
        get => _inTangentY;
        set { if (_inTangentY.Equals(value)) return; _inTangentY = value; MarkMutated(); }
    }
    public double OutTangentX
    {
        get => _outTangentX;
        set { if (_outTangentX.Equals(value)) return; _outTangentX = value; MarkMutated(); }
    }
    public double OutTangentY
    {
        get => _outTangentY;
        set { if (_outTangentY.Equals(value)) return; _outTangentY = value; MarkMutated(); }
    }

    internal static long GlobalMutationVersion => Interlocked.Read(ref _globalMutationVersion);
    internal long MutationVersion => Interlocked.Read(ref _mutationVersion);

    private void MarkMutated()
    {
        Interlocked.Increment(ref _mutationVersion);
        Interlocked.Increment(ref _globalMutationVersion);
    }
}

public class AnimationChannel
{
    private Keyframe[] _orderedKeyframes = [];
    private KeyframeCacheEntry[] _cachedKeyframes = [];
    private int _cachedCount = -1;
    private long _observedMutationVersion = long.MinValue;
    private int _lastSegmentIndex;
    private long _lastQueryTicks = long.MinValue;

    public required string PropertyName { get; init; }
    public double DefaultValue { get; set; }
    public List<Keyframe> Keyframes { get; init; } = [];

    public void NormalizeKeyframes()
    {
        Keyframes.Sort(static (left, right) => left.Time.Ticks.CompareTo(right.Time.Ticks));
        InvalidateLookupCache();
    }

    public void InvalidateLookupCache()
    {
        _cachedCount = -1;
        _observedMutationVersion = long.MinValue;
        _lastSegmentIndex = 0;
        _lastQueryTicks = long.MinValue;
    }

    public double GetValueAt(MediaTime time)
    {
        EnsureLookupCache();
        var keyframes = _orderedKeyframes;
        if (keyframes.Length == 0) return DefaultValue;
        if (time <= keyframes[0].Time) return keyframes[0].Value;
        if (time >= keyframes[^1].Time) return keyframes[^1].Value;

        var index = FindSegmentIndex(time.Ticks, keyframes);
        var left = keyframes[index];
        var right = keyframes[index + 1];
        _lastSegmentIndex = index;
        _lastQueryTicks = time.Ticks;

        var span = right.Time.Ticks - left.Time.Ticks;
        if (span <= 0) return right.Value;
        var fraction = Math.Clamp((double)(time.Ticks - left.Time.Ticks) / span, 0, 1);
        var progress = left.Interpolation switch
        {
            InterpolationType.Hold => 0,
            InterpolationType.Linear => fraction,
            InterpolationType.EaseIn => fraction * fraction,
            InterpolationType.EaseOut => 1 - ((1 - fraction) * (1 - fraction)),
            InterpolationType.EaseInOut => fraction * fraction * (3 - (2 * fraction)),
            InterpolationType.Bezier => EvaluateBezierProgress(fraction, left, right),
            _ => fraction,
        };
        return left.Value + ((right.Value - left.Value) * progress);
    }

    private void EnsureLookupCache()
    {
        var mutationVersion = Keyframe.GlobalMutationVersion;
        if (_cachedCount == Keyframes.Count)
        {
            if (_observedMutationVersion == mutationVersion) return;
            if (CachedKeyframesAreCurrent())
            {
                _observedMutationVersion = mutationVersion;
                return;
            }
        }

        RebuildLookupCache(mutationVersion);
    }

    private bool CachedKeyframesAreCurrent()
    {
        if (_cachedKeyframes.Length != Keyframes.Count) return false;
        for (var index = 0; index < _cachedKeyframes.Length; index++)
        {
            var current = Keyframes[index];
            var cached = _cachedKeyframes[index];
            if (!ReferenceEquals(current, cached.Keyframe)
                || current.MutationVersion != cached.MutationVersion)
                return false;
        }
        return true;
    }

    private void RebuildLookupCache(long mutationVersion)
    {
        var count = Keyframes.Count;
        _orderedKeyframes = new Keyframe[count];
        _cachedKeyframes = new KeyframeCacheEntry[count];
        for (var index = 0; index < count; index++)
        {
            var keyframe = Keyframes[index];
            _orderedKeyframes[index] = keyframe;
            _cachedKeyframes[index] = new KeyframeCacheEntry(keyframe, keyframe.MutationVersion);
        }
        Array.Sort(_orderedKeyframes, static (left, right) => left.Time.Ticks.CompareTo(right.Time.Ticks));
        _cachedCount = count;
        _observedMutationVersion = mutationVersion;
        _lastSegmentIndex = 0;
        _lastQueryTicks = long.MinValue;
    }

    private readonly record struct KeyframeCacheEntry(Keyframe Keyframe, long MutationVersion);

    private int FindSegmentIndex(long ticks, Keyframe[] keyframes)
    {
        if (_lastQueryTicks != long.MinValue && ticks >= _lastQueryTicks)
        {
            var index = Math.Clamp(_lastSegmentIndex, 0, keyframes.Length - 2);
            while (index < keyframes.Length - 2 && ticks > keyframes[index + 1].Time.Ticks) index++;
            if (ticks >= keyframes[index].Time.Ticks && ticks <= keyframes[index + 1].Time.Ticks)
                return index;
        }

        var low = 0;
        var high = keyframes.Length - 2;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (ticks < keyframes[middle].Time.Ticks)
            {
                high = middle - 1;
                continue;
            }
            if (ticks > keyframes[middle + 1].Time.Ticks)
            {
                low = middle + 1;
                continue;
            }
            return middle;
        }
        return Math.Clamp(low, 0, keyframes.Length - 2);
    }

    private static double EvaluateBezierProgress(double x, Keyframe left, Keyframe right)
    {
        var x1 = Math.Clamp(left.OutTangentX, 0, 1);
        var y1 = left.OutTangentY;
        var x2 = Math.Clamp(right.InTangentX, 0, 1);
        var y2 = right.InTangentY;

        // Older project files used reversed defaults. Normalize them to a monotonic curve.
        if (x1 > x2)
        {
            x1 = 0.25;
            x2 = 0.75;
        }

        var parameter = x;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var currentX = Cubic(parameter, 0, x1, x2, 1);
            var derivative = CubicDerivative(parameter, 0, x1, x2, 1);
            if (Math.Abs(derivative) < 0.000001) break;
            var next = parameter - ((currentX - x) / derivative);
            if (next is < 0 or > 1) break;
            parameter = next;
        }

        var low = 0.0;
        var high = 1.0;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var currentX = Cubic(parameter, 0, x1, x2, 1);
            if (Math.Abs(currentX - x) < 0.000001) break;
            if (currentX < x) low = parameter; else high = parameter;
            parameter = (low + high) / 2;
        }

        return Cubic(parameter, 0, y1, y2, 1);
    }

    private static double Cubic(double t, double p0, double p1, double p2, double p3)
    {
        var inverse = 1 - t;
        return (inverse * inverse * inverse * p0)
               + (3 * inverse * inverse * t * p1)
               + (3 * inverse * t * t * p2)
               + (t * t * t * p3);
    }

    private static double CubicDerivative(double t, double p0, double p1, double p2, double p3)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * (p1 - p0))
               + (6 * inverse * t * (p2 - p1))
               + (3 * t * t * (p3 - p2));
    }
}

/// <summary>
/// Backward-compatible name for projects created before multi-channel animation support.
/// </summary>
public sealed class AnimatedProperty : AnimationChannel
{
}
