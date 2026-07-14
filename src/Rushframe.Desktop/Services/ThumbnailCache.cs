using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace Rushframe.Desktop.Services;

/// <summary>Bounded asynchronous cache for frozen thumbnail bitmaps.</summary>
public sealed class ThumbnailCache : IDisposable
{
    private const int DefaultMaxEntries = 512;
    private const long DefaultMaxBytes = 128L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<ThumbnailKey, CacheEntry> _cache = [];
    private readonly LinkedList<ThumbnailKey> _lru = [];
    private readonly ConcurrentDictionary<ThumbnailKey, Task<BitmapSource?>> _inflight = new();
    private readonly SemaphoreSlim _decodeGate = new(Math.Max(2, Math.Min(4, Environment.ProcessorCount)));
    private readonly EditorPerformanceTelemetry _telemetry = EditorPerformanceTelemetry.Shared;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private long _cachedBytes;
    private bool _disposed;

    public ThumbnailCache(int maxEntries = DefaultMaxEntries, long maxBytes = DefaultMaxBytes)
    {
        _maxEntries = Math.Max(16, maxEntries);
        _maxBytes = Math.Max(8L * 1024 * 1024, maxBytes);
    }

    public Task<BitmapSource?> GetAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.FromResult<BitmapSource?>(null);

        var info = new FileInfo(path);
        var key = new ThumbnailKey(
            Path.GetFullPath(path),
            Math.Max(16, decodePixelWidth),
            info.Length,
            info.LastWriteTimeUtc.Ticks);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _telemetry.RecordThumbnailCache(hit: true);
                Touch(cached);
                return Task.FromResult<BitmapSource?>(cached.Bitmap);
            }
        }

        _telemetry.RecordThumbnailCache(hit: false);
        return _inflight.GetOrAdd(key, candidate => LoadAndCacheAsync(candidate, cancellationToken));
    }

    public void InvalidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_gate)
        {
            foreach (var key in _cache.Keys.Where(key =>
                         string.Equals(key.Path, fullPath, StringComparison.OrdinalIgnoreCase)).ToArray())
                Remove(key);
        }
    }

    private async Task<BitmapSource?> LoadAndCacheAsync(ThumbnailKey key, CancellationToken cancellationToken)
    {
        try
        {
            await _decodeGate.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = key.DecodePixelWidth;
                bitmap.UriSource = new Uri(key.Path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                cancellationToken.ThrowIfCancellationRequested();

                var estimatedBytes = EstimateBytes(bitmap);
                lock (_gate)
                {
                    if (_disposed) return (BitmapSource?)null;
                    var node = _lru.AddFirst(key);
                    _cache[key] = new CacheEntry(bitmap, estimatedBytes, node);
                    _cachedBytes += estimatedBytes;
                    Trim();
                }
                    return (BitmapSource?)bitmap;
                }, cancellationToken);
            }
            finally
            {
                _decodeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private void Touch(CacheEntry entry)
    {
        _lru.Remove(entry.Node);
        _lru.AddFirst(entry.Node);
    }

    private void Trim()
    {
        while (_cache.Count > _maxEntries || _cachedBytes > _maxBytes)
        {
            var last = _lru.Last;
            if (last == null) break;
            Remove(last.Value);
        }
    }

    private void Remove(ThumbnailKey key)
    {
        if (!_cache.Remove(key, out var entry)) return;
        _lru.Remove(entry.Node);
        _cachedBytes -= entry.EstimatedBytes;
    }

    private static long EstimateBytes(BitmapSource bitmap) =>
        Math.Max(1L, bitmap.PixelWidth) * Math.Max(1L, bitmap.PixelHeight) * 4;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _cache.Clear();
            _lru.Clear();
            _cachedBytes = 0;
        }
        _decodeGate.Dispose();
    }

    private readonly record struct ThumbnailKey(
        string Path,
        int DecodePixelWidth,
        long FileLength,
        long LastWriteTicks);

    private sealed record CacheEntry(BitmapSource Bitmap, long EstimatedBytes, LinkedListNode<ThumbnailKey> Node);
}
