using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Polls the reminders once a second and raises TriggerFired for each one that comes due.
///
/// The poll runs on a timer thread, but it only looks: everything that changes a reminder
/// (marking it fired, counting the occurrence) and TriggerFired itself run on the thread that
/// called <see cref="Start"/>, through its SynchronizationContext. In the app that's the UI
/// thread, so the saves those changes cause, and the UI's own collection, are never touched
/// from the timer thread. Nothing fires before Start, so the app subscribes first.
public class TimerEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;
    private readonly bool _polls;
    private System.Threading.Timer? _timer;
    private SynchronizationContext? _context;
    private bool _started;
    private List<TriggerTimer> _triggers = new();

    public event Action<TriggerTimer>? TriggerFired;
    public event Action<Exception>? TickFailed;

    public TimerEngine() : this(() => DateTime.Now, polls: true)
    {
    }

    /// For tests: the clock the engine reads instead of DateTime.Now, and whether Start begins
    /// polling on its own. Without polling, nothing is checked until <see cref="CheckDue"/>.
    internal TimerEngine(Func<DateTime> clock, bool polls)
    {
        _clock = clock;
        _polls = polls;
    }

    /// Starts firing reminders, on the calling thread's SynchronizationContext (the UI thread's,
    /// in the app). Call it after subscribing to TriggerFired.
    public void Start() => Start(SynchronizationContext.Current);

    /// For tests: the context reminders fire on, or null to fire them on the polling thread.
    internal void Start(SynchronizationContext? context)
    {
        lock (_lock)
        {
            if (_started)
                return;

            _started = true;
            _context = context;
            if (_polls)
                _timer = new System.Threading.Timer(_ => CheckDue(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public void SetTriggers(IEnumerable<TriggerTimer> triggers)
    {
        lock (_lock)
        {
            _triggers = triggers.ToList();
        }
    }

    /// One poll, as the timer does each second: if anything is due, fire it on Start's context.
    internal void CheckDue()
    {
        // Runs on a ThreadPool thread: an unhandled exception here would take down the whole
        // process, so nothing from here on is allowed to escape.
        try
        {
            SynchronizationContext? context;
            lock (_lock)
            {
                if (!_started || !AnyDue(_clock()))
                    return;
                context = _context;
            }

            if (context is null)
                FireDue();
            else
                context.Post(_ => FireDue(), null);
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private bool AnyDue(DateTime now) => _triggers.Any(t => IsDue(t, now));

    private static bool IsDue(TriggerTimer t, DateTime now) => t.Enabled && !t.HasFired && t.TriggerAt <= now;

    /// On Start's context: marks each reminder that is still due as fired, and raises TriggerFired
    /// for it. Each reminder is handled on its own, so a failure (a save that throws, a handler
    /// that throws) can't cost another reminder its alert, nor its own.
    private void FireDue()
    {
        List<TriggerTimer> due;
        try
        {
            lock (_lock)
            {
                var now = _clock();
                // Checked again here: a poll posted earlier may already have fired them.
                due = _triggers.Where(t => IsDue(t, now)).ToList();
            }
        }
        catch (Exception ex)
        {
            Report(ex);
            return;
        }

        foreach (var t in due)
        {
            try
            {
                if (t.Recurrence is not null)
                    t.Recurrence.OccurrencesSoFar++;
                t.HasFired = true;
            }
            catch (Exception ex)
            {
                // Marking it fired set the value before its change handlers ran: one of them
                // (the save) failed, but the alert still goes off.
                Report(ex);
            }

            try
            {
                TriggerFired?.Invoke(t);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            TickFailed?.Invoke(ex);
        }
        catch
        {
            // Reporting must never be what takes the process down.
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
