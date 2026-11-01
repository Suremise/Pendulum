using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Polls fixed triggers once a second on a background thread and raises
/// TriggerFired for anything due. Consumers must marshal to the UI thread.
public class TimerEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly System.Threading.Timer? _timer;
    private readonly Func<DateTime> _clock;
    private List<TriggerTimer> _triggers = new();

    public event Action<TriggerTimer>? TriggerFired;
    public event Action<Exception>? TickFailed;

    public TimerEngine() : this(() => DateTime.Now, startPolling: true)
    {
    }

    /// For tests: the clock the engine reads instead of DateTime.Now, and whether it polls on
    /// its own. Without polling, nothing fires until <see cref="CheckDue"/> is called.
    internal TimerEngine(Func<DateTime> clock, bool startPolling)
    {
        _clock = clock;
        if (startPolling)
            _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// One poll: raises TriggerFired for everything due by the clock, as the timer does each second.
    internal void CheckDue() => Tick(null);

    public void SetTriggers(IEnumerable<TriggerTimer> triggers)
    {
        lock (_lock)
        {
            _triggers = triggers.ToList();
        }
    }

    private void Tick(object? state)
    {
        // Runs on a ThreadPool thread: an unhandled exception here would take
        // down the whole process, so nothing from here on is allowed to escape.
        try
        {
            List<TriggerTimer> due;
            lock (_lock)
            {
                var now = _clock();
                due = _triggers.Where(t => t.Enabled && !t.HasFired && t.TriggerAt <= now).ToList();
                foreach (var t in due)
                {
                    t.HasFired = true;
                    if (t.Recurrence is not null)
                        t.Recurrence.OccurrencesSoFar++;
                }
            }

            foreach (var t in due)
            {
                try
                {
                    TriggerFired?.Invoke(t);
                }
                catch (Exception ex)
                {
                    TickFailed?.Invoke(ex);
                }
            }
        }
        catch (Exception ex)
        {
            TickFailed?.Invoke(ex);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
