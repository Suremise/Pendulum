using System.Threading;

namespace Pendulum.App.Services;

/// Owns the primary copy's mutex and wake listener. Create and dispose on the same thread:
/// Windows mutex ownership belongs to that thread. The wake callback must not block on the UI.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Pendulum.SingleInstance.Mutex"; // not user-visible
    private const string WakeEventName = "Pendulum.WakeExisting.Event"; // not user-visible
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _wake;
    private readonly ManualResetEvent? _stop;
    private readonly Thread? _listener;
    private bool _disposed;

    public bool IsPrimary { get; }
    internal bool IsListening => _listener?.IsAlive == true;

    public SingleInstance(Action onWake) : this(MutexName, WakeEventName, onWake) { }

    // Unique names in tests keep the installed copy completely out of their scope.
    internal SingleInstance(string mutexName, string wakeEventName, Action onWake)
    {
        _mutex = new Mutex(true, mutexName, out var createdNew);
        IsPrimary = createdNew;
        if (!IsPrimary)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(wakeEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The primary copy has not created its wake event yet (or is exiting).
            }
            return;
        }

        try
        {
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, wakeEventName);
            _stop = new ManualResetEvent(false);
            _listener = new Thread(() =>
            {
                WaitHandle[] signals = [_stop, _wake];
                while (WaitHandle.WaitAny(signals) != 0)
                {
                    if (_stop.WaitOne(0)) break;
                    try { onWake(); }
                    catch (Exception ex) { CrashLog.Write("SingleInstance.Wake", ex); } // not user-visible
                }
            })
            {
                IsBackground = true,
                Name = "Pendulum instance wake", // not user-visible
            };
            _listener.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop?.Set();
        if (_listener?.IsAlive == true) _listener.Join();
        _wake?.Dispose();
        _stop?.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
