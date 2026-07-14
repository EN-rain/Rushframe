using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Text.Json;

namespace Rushframe.Desktop.Services;

/// <summary>
/// Lightweight in-process performance telemetry. Metrics are always cheap to record and
/// detailed samples are only retained when RUSHFRAME_PERF=1 is set.
/// </summary>
public sealed class EditorPerformanceTelemetry : IDisposable
{
    private const int MaxSamplesPerMetric = 512;
    private static readonly Meter Meter = new("Rushframe.Editor", "1.0.0");
    private static readonly Histogram<double> PreviewFrameDuration =
        Meter.CreateHistogram<double>("rushframe.preview.frame.duration_ms", "ms");
    private static readonly Counter<long> PreviewFramesRendered =
        Meter.CreateCounter<long>("rushframe.preview.frames.rendered");
    private static readonly Counter<long> PreviewFramesDropped =
        Meter.CreateCounter<long>("rushframe.preview.frames.dropped");
    private static readonly Histogram<double> UiInputDuration =
        Meter.CreateHistogram<double>("rushframe.ui.input.duration_ms", "ms");
    private static readonly Histogram<double> TimelineRenderDuration =
        Meter.CreateHistogram<double>("rushframe.timeline.static_render.duration_ms", "ms");
    private static readonly Counter<long> TimelineRenderCount =
        Meter.CreateCounter<long>("rushframe.timeline.static_render.count");
    private static readonly Histogram<double> ProjectSnapshotDuration =
        Meter.CreateHistogram<double>("rushframe.project.snapshot.duration_ms", "ms");
    private static readonly Histogram<double> ProjectWriteDuration =
        Meter.CreateHistogram<double>("rushframe.project.write.duration_ms", "ms");
    private static readonly Counter<long> ProjectSavesCoalesced =
        Meter.CreateCounter<long>("rushframe.project.save.coalesced_count");
    private static readonly UpDownCounter<long> ActivePreviewLayers =
        Meter.CreateUpDownCounter<long>("rushframe.preview.layers.active");
    private static readonly Counter<long> ThumbnailCacheHits =
        Meter.CreateCounter<long>("rushframe.thumbnail.cache.hit_count");
    private static readonly Counter<long> ThumbnailCacheMisses =
        Meter.CreateCounter<long>("rushframe.thumbnail.cache.miss_count");
    private static readonly Histogram<double> StartupMilestones =
        Meter.CreateHistogram<double>("rushframe.startup.milestone_ms", "ms");

    private readonly ConcurrentDictionary<string, SampleWindow> _samples = new(StringComparer.Ordinal);
    private readonly bool _detailedEnabled =
        string.Equals(Environment.GetEnvironmentVariable("RUSHFRAME_PERF"), "1", StringComparison.Ordinal);
    private long _lastLayerCount;
    private bool _disposed;

    public static EditorPerformanceTelemetry Shared { get; } = new();

    public bool DetailedEnabled => _detailedEnabled;

    public IDisposable MeasureUiInput(string interaction) =>
        new Measurement(elapsed =>
        {
            UiInputDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("interaction", interaction));
            AddSample($"ui.input.{interaction}", elapsed.TotalMilliseconds);
        });

    public IDisposable MeasureTimelineRender(int itemsScanned = 0, int visibleItems = 0) =>
        new Measurement(elapsed =>
        {
            TimelineRenderDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("items.scanned", itemsScanned),
                new KeyValuePair<string, object?>("items.visible", visibleItems));
            TimelineRenderCount.Add(1);
            AddSample("timeline.render", elapsed.TotalMilliseconds);
        });

    public IDisposable MeasureProjectSnapshot(long revision) =>
        new Measurement(elapsed =>
        {
            ProjectSnapshotDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("revision", revision));
            AddSample("project.snapshot", elapsed.TotalMilliseconds);
        });

    public IDisposable MeasureProjectWrite(long revision, string target) =>
        new Measurement(elapsed =>
        {
            ProjectWriteDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("revision", revision),
                new KeyValuePair<string, object?>("target", target));
            AddSample($"project.write.{target}", elapsed.TotalMilliseconds);
        });

    public void RecordPreviewFrame(TimeSpan elapsed, int activeLayers, bool dropped)
    {
        PreviewFrameDuration.Record(elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("layers", activeLayers));
        PreviewFramesRendered.Add(1);
        if (dropped) PreviewFramesDropped.Add(1);
        SetActiveLayerCount(activeLayers);
        AddSample("preview.frame", elapsed.TotalMilliseconds);
    }

    public void RecordCoalescedSave() => ProjectSavesCoalesced.Add(1);

    public void RecordThumbnailCache(bool hit)
    {
        if (hit) ThumbnailCacheHits.Add(1);
        else ThumbnailCacheMisses.Add(1);
    }

    public void RecordStartupMilestone(string milestone, TimeSpan elapsed)
    {
        StartupMilestones.Record(elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("milestone", milestone));
        AddSample($"startup.{milestone}", elapsed.TotalMilliseconds);
    }

    public void WriteSnapshot(string path)
    {
        if (!_detailedEnabled) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(GetSnapshot(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    public PerformanceSnapshot GetSnapshot()
    {
        var values = _samples.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Snapshot(),
            StringComparer.Ordinal);
        return new PerformanceSnapshot(
            values,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalMemory(forceFullCollection: false));
    }

    private void SetActiveLayerCount(int count)
    {
        var previous = Interlocked.Exchange(ref _lastLayerCount, count);
        var delta = count - previous;
        if (delta != 0) ActivePreviewLayers.Add(delta);
    }

    private void AddSample(string name, double milliseconds)
    {
        if (!_detailedEnabled) return;
        _samples.GetOrAdd(name, static _ => new SampleWindow(MaxSamplesPerMetric)).Add(milliseconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetActiveLayerCount(0);
    }

    private sealed class Measurement(Action<TimeSpan> completed) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private Action<TimeSpan>? _completed = completed;

        public void Dispose()
        {
            var callback = Interlocked.Exchange(ref _completed, null);
            if (callback == null) return;
            callback(Stopwatch.GetElapsedTime(_started));
        }
    }

    private sealed class SampleWindow(int capacity)
    {
        private readonly object _gate = new();
        private readonly double[] _values = new double[Math.Max(8, capacity)];
        private int _count;
        private int _next;

        public void Add(double value)
        {
            lock (_gate)
            {
                _values[_next] = value;
                _next = (_next + 1) % _values.Length;
                _count = Math.Min(_count + 1, _values.Length);
            }
        }

        public MetricSnapshot Snapshot()
        {
            double[] copy;
            lock (_gate)
            {
                copy = new double[_count];
                for (var index = 0; index < _count; index++)
                {
                    var source = (_next - _count + index + _values.Length) % _values.Length;
                    copy[index] = _values[source];
                }
            }

            if (copy.Length == 0) return new MetricSnapshot(0, 0, 0, 0, 0);
            Array.Sort(copy);
            return new MetricSnapshot(
                copy.Length,
                copy.Average(),
                Percentile(copy, 0.50),
                Percentile(copy, 0.95),
                Percentile(copy, 0.99));
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 0) return 0;
            var position = Math.Clamp(percentile, 0, 1) * (sorted.Length - 1);
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper) return sorted[lower];
            var fraction = position - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
        }
    }
}

public sealed record PerformanceSnapshot(
    IReadOnlyDictionary<string, MetricSnapshot> Metrics,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long ManagedBytes);
public sealed record MetricSnapshot(int Count, double AverageMs, double P50Ms, double P95Ms, double P99Ms);
