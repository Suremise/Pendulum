using Pendulum.Core.Models;

namespace Pendulum.Core.Services;

/// Caches the earliest enabled, unspent reminder. Reading Next does no collection work;
/// only membership or scheduling changes recompute it. Owned and read on the caller's thread.
public sealed class NextReminderTracker : IDisposable
{
    private readonly ReminderStore _store;
    private string? _nextName;

    public TriggerTimer? Next { get; private set; }
    public event Action? Changed;

    public NextReminderTracker(ReminderStore store)
    {
        _store = store;
        _store.RemindersChanged += OnRemindersChanged;
        Recompute();
    }

    private void OnRemindersChanged(object? sender, RemindersChangedEventArgs e)
    {
        if (e.MembershipChanged || e.Properties.Overlaps([nameof(TriggerTimer.Enabled), nameof(TriggerTimer.HasFired), nameof(TriggerTimer.TriggerAt)]))
            Recompute();
        else if (e.Properties.Contains(nameof(TriggerTimer.Name)) && Next?.Name != _nextName)
        {
            _nextName = Next?.Name;
            Changed?.Invoke();
        }
    }

    private void Recompute()
    {
        TriggerTimer? next = null;
        foreach (var reminder in _store.Items)
            if (reminder.Enabled && !reminder.HasFired && (next is null || reminder.TriggerAt < next.TriggerAt))
                next = reminder;
        Next = next;
        _nextName = next?.Name;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _store.RemindersChanged -= OnRemindersChanged;
    }
}
