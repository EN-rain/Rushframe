using System.Collections.Concurrent;
using System.IO;

namespace Rushframe.Desktop.Services;

internal sealed class SoundLibraryWatchService : IDisposable
{
    private readonly Func<CancellationToken, Task<SoundLibraryCatalogStatus>> _statusProvider;
    private readonly Func<string, CancellationToken, Task> _reindexRootAsync;
    private readonly ConcurrentDictionary<string, WatchRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _disposed;

    public SoundLibraryWatchService(
        Func<CancellationToken, Task<SoundLibraryCatalogStatus>> statusProvider,
        Func<string, CancellationToken, Task> reindexRootAsync)
    {
        _statusProvider = statusProvider;
        _reindexRootAsync = reindexRootAsync;
    }

    public event EventHandler<string>? RootIndexed;
    public event EventHandler<string>? IndexFailed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var status = await _statusProvider(cancellationToken);
            var desired = status.Roots
                .Where(root => root.WatchEnabled && Directory.Exists(root.Path))
                .ToDictionary(root => Path.GetFullPath(root.Path), StringComparer.OrdinalIgnoreCase);

            foreach (var existing in _registrations.Keys)
            {
                if (desired.ContainsKey(existing)) continue;
                if (_registrations.TryRemove(existing, out var registration))
                    registration.Dispose();
            }

            foreach (var root in desired.Keys)
            {
                _registrations.GetOrAdd(root, CreateRegistration);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private WatchRegistration CreateRegistration(string root)
    {
        var registration = new WatchRegistration(root, QueueReindex, ReindexRootAsync);
        registration.Start();
        return registration;
    }

    private void QueueReindex(string root, string changedPath)
    {
        if (_disposed || !SoundLibraryAudioPolicy.IsKnownAudioExtension(changedPath)) return;
        if (_registrations.TryGetValue(root, out var registration))
            registration.Schedule();
    }

    private async Task ReindexRootAsync(string root)
    {
        if (_disposed) return;
        try
        {
            await _reindexRootAsync(root, _lifetimeCancellation.Token);
            RootIndexed?.Invoke(this, root);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            IndexFailed?.Invoke(this, $"{root}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        foreach (var registration in _registrations.Values)
            registration.Dispose();
        _registrations.Clear();
        _lifetimeCancellation.Dispose();
        _refreshGate.Dispose();
    }

    private sealed class WatchRegistration : IDisposable
    {
        private readonly string _root;
        private readonly Action<string, string> _queue;
        private readonly Func<string, Task> _reindexAsync;
        private readonly FileSystemWatcher _watcher;
        private readonly object _sync = new();
        private Timer? _timer;
        private bool _disposed;
        private bool _running;
        private bool _pending;

        public WatchRegistration(
            string root,
            Action<string, string> queue,
            Func<string, Task> reindexAsync)
        {
            _root = root;
            _queue = queue;
            _reindexAsync = reindexAsync;
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size
                               | NotifyFilters.CreationTime,
                InternalBufferSize = 32 * 1024,
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, _) => Schedule();
        }

        public void Start() => _watcher.EnableRaisingEvents = true;

        public void Schedule()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _pending = true;
                _timer ??= new Timer(_ => _ = RunAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(TimeSpan.FromMilliseconds(900), Timeout.InfiniteTimeSpan);
            }
        }

        private async Task RunAsync()
        {
            lock (_sync)
            {
                if (_disposed) return;
                if (_running)
                {
                    _pending = true;
                    return;
                }
                _running = true;
                _pending = false;
            }

            while (true)
            {
                await _reindexAsync(_root);
                lock (_sync)
                {
                    if (_disposed || !_pending)
                    {
                        _running = false;
                        return;
                    }
                    _pending = false;
                }
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs args) => _queue(_root, args.FullPath);

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            _queue(_root, args.OldFullPath);
            _queue(_root, args.FullPath);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
