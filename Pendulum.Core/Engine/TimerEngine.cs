using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Polls fixed triggers once a second on a background thread and raises
/// TriggerFired for anything due. Consumers must marshal to the UI thread.
public class TimerEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly System.Threading.Timer _timer;
    private List<TriggerTimer> _triggers = new();

    public event Action<TriggerTimer>? TriggerFired;
    public event Action<Exception>? TickFailed;

    public TimerEngine()
    {
        _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

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
                var now = DateTime.Now;
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

    public void Dispose() => _timer.Dispose();
}
