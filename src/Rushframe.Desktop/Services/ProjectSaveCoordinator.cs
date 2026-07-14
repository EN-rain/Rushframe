using Rushframe.Domain;
using Rushframe.Domain.Serialization;
using Rushframe.Infrastructure;

namespace Rushframe.Desktop.Services;

/// <summary>
/// Coalesces project persistence so editing commands never perform file I/O.
/// Snapshot serialization runs off-thread and uses an optimistic mutation epoch to
/// guarantee that only a complete, stable project revision is written.
/// </summary>
public sealed class ProjectSaveCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(1);

    private readonly ProjectRepository _projectRepository;
    private readonly AutosaveService _autosaveService;
    private readonly EditorPerformanceTelemetry _telemetry;
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();

    private CancellationTokenSource? _debounceCancellation;
    private CancellationTokenSource? _periodicCancellation;
    private Task? _debounceTask;
    private Task? _periodicTask;
    private Project? _pendingProject;
    private long _pendingRevision = -1;
    private long _lastAutosavedRevision = -1;
    private long _lastExplicitlySavedRevision = -1;
    private bool _autosaveEnabled = true;
    private TimeSpan _periodicInterval = TimeSpan.FromSeconds(30);
    private long _mutationEpoch;
    private int _mutationDepth;
    private bool _disposed;

    public ProjectSaveCoordinator(
        ProjectRepository projectRepository,
        AutosaveService autosaveService,
        EditorPerformanceTelemetry telemetry)
    {
        _projectRepository = projectRepository;
        _autosaveService = autosaveService;
        _telemetry = telemetry;
        RestartPeriodicLoop();
    }

    public long LastAutosavedRevision => Interlocked.Read(ref _lastAutosavedRevision);
    public long LastExplicitlySavedRevision => Interlocked.Read(ref _lastExplicitlySavedRevision);
    public bool HasPendingSave
    {
        get
        {
            lock (_gate) return _pendingRevision > _lastAutosavedRevision;
        }
    }

    public event EventHandler<ProjectSaveCompletedEventArgs>? SaveCompleted;
    public event EventHandler<Exception>? SaveFailed;

    public IDisposable BeginMutation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_mutationDepth++ == 0) Interlocked.Increment(ref _mutationEpoch);
        }
        return new MutationScope(this);
    }

    public void ConfigureAutosave(bool enabled, TimeSpan periodicInterval)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _autosaveEnabled = enabled;
        _periodicInterval = periodicInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(30)
            : periodicInterval;
        RestartPeriodicLoop();
        if (!enabled) CancelDebounce();
    }

    public void MarkDirty(Project project)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_pendingProject != null && ReferenceEquals(_pendingProject, project) && project.Revision <= _pendingRevision)
            {
                _telemetry.RecordCoalescedSave();
                return;
            }

            if (_pendingRevision >= 0 && project.Revision > _pendingRevision)
                _telemetry.RecordCoalescedSave();
            _pendingProject = project;
            _pendingRevision = project.Revision;
        }

        if (_autosaveEnabled) ScheduleDebouncedAutosave();
    }

    public void ResetForProject(Project project)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelDebounce();
        lock (_gate)
        {
            _pendingProject = project;
            _pendingRevision = project.Revision;
            _lastAutosavedRevision = project.Revision;
            _lastExplicitlySavedRevision = project.Revision;
        }
    }

    public async Task SaveExplicitAsync(
        Project project,
        string path,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = await CaptureSnapshotAsync(project, cancellationToken);
        await _writerGate.WaitAsync(cancellationToken);
        try
        {
            using (_telemetry.MeasureProjectWrite(snapshot.Revision, "project"))
                await _projectRepository.SaveSerializedAsync(snapshot.Json, path, cancellationToken);
            Interlocked.Exchange(ref _lastExplicitlySavedRevision, snapshot.Revision);
            SaveCompleted?.Invoke(this, new ProjectSaveCompletedEventArgs(snapshot.Revision, path, false));
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(this, ex);
            throw;
        }
        finally
        {
            _writerGate.Release();
        }
    }

    public async Task FlushAutosaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelDebounce();
        await SavePendingAutosaveAsync(cancellationToken);
    }

    private void ScheduleDebouncedAutosave()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            cancellation = _debounceCancellation;
            _debounceTask = Task.Run(
                () => DebounceAndSaveAsync(cancellation.Token),
                cancellation.Token);
        }
    }

    private async Task DebounceAndSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DefaultDebounce, cancellationToken);
            await SavePendingAutosaveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SavePendingAutosaveAsync(CancellationToken cancellationToken)
    {
        if (!_autosaveEnabled) return;

        Project? project;
        long requestedRevision;
        lock (_gate)
        {
            project = _pendingProject;
            requestedRevision = _pendingRevision;
        }
        if (project == null || requestedRevision <= Interlocked.Read(ref _lastAutosavedRevision)) return;

        var snapshot = await CaptureSnapshotAsync(project, cancellationToken);
        if (snapshot.Revision <= Interlocked.Read(ref _lastAutosavedRevision)) return;

        await _writerGate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot.Revision <= Interlocked.Read(ref _lastAutosavedRevision)) return;
            string path;
            using (_telemetry.MeasureProjectWrite(snapshot.Revision, "autosave"))
                path = await _autosaveService.SaveSerializedAsync(
                    snapshot.ProjectId,
                    snapshot.Json,
                    cancellationToken);
            Interlocked.Exchange(ref _lastAutosavedRevision, snapshot.Revision);
            SaveCompleted?.Invoke(this, new ProjectSaveCompletedEventArgs(snapshot.Revision, path, true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke(this, ex);
        }
        finally
        {
            _writerGate.Release();
        }
    }

    private async Task<ProjectSnapshot> CaptureSnapshotAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var epochBefore = Interlocked.Read(ref _mutationEpoch);
            if ((epochBefore & 1) != 0)
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            var revisionBefore = project.Revision;
            string json;
            try
            {
                using (_telemetry.MeasureProjectSnapshot(revisionBefore))
                    json = await Task.Run(() => ProjectSerializer.Serialize(project), cancellationToken);
            }
            catch (Exception) when (
                !cancellationToken.IsCancellationRequested
                && (Interlocked.Read(ref _mutationEpoch) != epochBefore || project.Revision != revisionBefore))
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            var epochAfter = Interlocked.Read(ref _mutationEpoch);
            if (epochAfter == epochBefore
                && (epochAfter & 1) == 0
                && project.Revision == revisionBefore)
                return new ProjectSnapshot(project.Id, revisionBefore, json);

            _telemetry.RecordCoalescedSave();
            await Task.Delay(10, cancellationToken);
        }
    }

    private void EndMutation()
    {
        lock (_gate)
        {
            if (_mutationDepth <= 0) return;
            if (--_mutationDepth == 0) Interlocked.Increment(ref _mutationEpoch);
        }
    }

    private void RestartPeriodicLoop()
    {
        if (_disposed) return;
        _periodicCancellation?.Cancel();
        _periodicCancellation?.Dispose();
        _periodicCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _periodicCancellation.Token;
        _periodicTask = Task.Run(() => RunPeriodicLoopAsync(token), token);
    }

    private async Task RunPeriodicLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var interval = _periodicInterval;
            await Task.Delay(interval, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_autosaveEnabled) await SavePendingAutosaveAsync(cancellationToken);
                interval = _periodicInterval;
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelDebounce()
    {
        lock (_gate)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            _debounceTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        CancelDebounce();
        _periodicCancellation?.Cancel();
        _lifetime.Cancel();
        try
        {
            if (_periodicTask != null) await _periodicTask.ConfigureAwait(false);
            if (_debounceTask != null) await _debounceTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _periodicCancellation?.Dispose();
            _lifetime.Dispose();
            _writerGate.Dispose();
        }
    }

    private sealed class MutationScope(ProjectSaveCoordinator owner) : IDisposable
    {
        private ProjectSaveCoordinator? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndMutation();
    }

    private sealed record ProjectSnapshot(ProjectId ProjectId, long Revision, string Json);
}

public sealed record ProjectSaveCompletedEventArgs(long Revision, string Path, bool IsAutosave);
