using System.Diagnostics;
using System.Windows.Media;

namespace Rushframe.Desktop.Services;

/// <summary>
/// Owns the single compositor callback used while preview is playing. Visual frames and
/// low-frequency transport labels are scheduled independently to avoid duplicate work.
/// </summary>
public sealed class PreviewFrameScheduler : IDisposable
{
    private static readonly TimeSpan TransportInterval = TimeSpan.FromMilliseconds(100);

    private readonly Action _renderFrame;
    private readonly Action _updateTransport;
    private readonly Func<double> _targetFramesPerSecond;
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastFrameAt;
    private TimeSpan _lastTransportAt;
    private bool _running;
    private int _renderingGate;
    private bool _disposed;

    public PreviewFrameScheduler(
        Action renderFrame,
        Action updateTransport,
        Func<double> targetFramesPerSecond)
    {
        _renderFrame = renderFrame;
        _updateTransport = updateTransport;
        _targetFramesPerSecond = targetFramesPerSecond;
    }

    public bool IsRunning => _running;
    public long FramesRendered { get; private set; }
    public long FramesDropped { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running) return;
        _lastFrameAt = TimeSpan.Zero;
        _lastTransportAt = TimeSpan.Zero;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
        _running = true;
    }

    public void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();
        _running = false;
        Interlocked.Exchange(ref _renderingGate, 0);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running || Interlocked.Exchange(ref _renderingGate, 1) != 0) return;
        try
        {
            var now = _clock.Elapsed;
            var fps = Math.Clamp(_targetFramesPerSecond(), 1, 240);
            var frameInterval = TimeSpan.FromSeconds(1 / fps);
            if (now - _lastFrameAt >= frameInterval)
            {
                var elapsedIntervals = Math.Max(1, (int)((now - _lastFrameAt).Ticks / Math.Max(1, frameInterval.Ticks)));
                if (elapsedIntervals > 1) FramesDropped += elapsedIntervals - 1;
                _lastFrameAt = now;
                _renderFrame();
                FramesRendered++;
            }

            if (now - _lastTransportAt >= TransportInterval)
            {
                _lastTransportAt = now;
                _updateTransport();
            }
        }
        finally
        {
            Volatile.Write(ref _renderingGate, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
