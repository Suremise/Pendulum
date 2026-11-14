namespace Pendulum.Core.Persistence;

/// Coalesces changes on their owning thread, then writes detached snapshots on a worker.
/// MarkDirty, Flush and Dispose are called on the owner; dispatch posts back to that owner.
/// Flush never waits for a posted callback, so it is safe during UI shutdown.
public sealed class DebouncedSave<T> : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(400);
    private readonly Func<T> _snapshot;
    private readonly Action<T> _write;
    private readonly Action<Exception> _report;
    private readonly ITimer _timer;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly object _writer = new();
    private bool _dirty;
    private bool _disposed;
    private long _version;
    private long _lastChange;
    private (long Version, T Value)? _pending;

    public DebouncedSave(Func<T> snapshot, Action<T> write, Action<Action> dispatch,
        Action<Exception> report, TimeProvider? timeProvider = null)
    {
        _snapshot = snapshot;
        _write = write;
        _report = report;
        _clock = timeProvider ?? TimeProvider.System;
        void Post(Action action)
        {
            try { dispatch(action); }
            catch (Exception ex) { _report(ex); }
        }
        _timer = _clock.CreateTimer(_ =>
        {
            long version;
            lock (_gate)
            {
                if (_disposed || !_dirty) return;
                version = _version;
            }
            Post(() =>
            {
                lock (_gate)
                {
                    if (_disposed || !_dirty || version != _version) return;
                    // A callback already in flight when MarkDirty reset the timer must still
                    // respect the latest change's full quiet period.
                    var remaining = QuietPeriod - _clock.GetElapsedTime(_lastChange);
                    if (remaining > TimeSpan.Zero)
                    {
                        _timer!.Change(remaining, Timeout.InfiniteTimeSpan);
                        return;
                    }
                }
                try
                {
                    Capture();
                    _ = Task.Run(() =>
                    {
                        try { Drain(); }
                        catch (Exception ex) { _report(ex); }
                    });
                }
                catch (Exception ex) { _report(ex); }
            });
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void MarkDirty()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _dirty = true;
            _version++;
            _lastChange = _clock.GetTimestamp();
            _timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void Capture()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            // No worker ever reads live models or collections.
            _pending = (_version, _snapshot());
            _dirty = false;
        }
    }

    private void Drain()
    {
        lock (_writer)
        {
            while (true)
            {
                (long Version, T Value)? pending;
                lock (_gate) pending = _pending;
                if (pending is not { } next) return;
                // Keep a failed snapshot pending: the next change or Flush retries it.
                _write(next.Value);
                lock (_gate)
                    if (_pending?.Version == next.Version) _pending = null;
            }
        }
    }

    /// Captures anything still dirty, waits for an active write, then saves the latest snapshot.
    /// Errors propagate to the caller; background failures are reported and remain retryable.
    public void Flush()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Capture();
        Drain();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Flush();
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}
