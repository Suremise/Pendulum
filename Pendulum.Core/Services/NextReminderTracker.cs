using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Pendulum.Core.Models;

namespace Pendulum.Core.Services;

/// Caches the earliest enabled, unspent reminder. Reading Next does no collection work;
/// only membership or scheduling changes recompute it. Owned and read on the caller's thread.
public sealed class NextReminderTracker : IDisposable
{
    private readonly ObservableCollection<TriggerTimer> _reminders;
    private readonly HashSet<TriggerTimer> _watched = [];

    public TriggerTimer? Next { get; private set; }
    public event Action? Changed;

    public NextReminderTracker(ObservableCollection<TriggerTimer> reminders)
    {
        _reminders = reminders;
        _reminders.CollectionChanged += OnCollectionChanged;
        Rewire();
        Recompute();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Rewire();
        Recompute();
    }

    private void Rewire()
    {
        var current = _reminders.ToHashSet();
        foreach (var old in _watched.Except(current).ToList())
        {
            old.PropertyChanged -= OnReminderChanged;
            _watched.Remove(old);
        }
        foreach (var added in current.Except(_watched).ToList())
        {
            added.PropertyChanged += OnReminderChanged;
            _watched.Add(added);
        }
    }

    private void OnReminderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(TriggerTimer.Enabled) or nameof(TriggerTimer.HasFired) or nameof(TriggerTimer.TriggerAt))
            Recompute();
        else if (ReferenceEquals(sender, Next) && e.PropertyName == nameof(TriggerTimer.Name))
            Changed?.Invoke();
    }

    private void Recompute()
    {
        TriggerTimer? next = null;
        foreach (var reminder in _reminders)
            if (reminder.Enabled && !reminder.HasFired && (next is null || reminder.TriggerAt < next.TriggerAt))
                next = reminder;
        Next = next;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _reminders.CollectionChanged -= OnCollectionChanged;
        foreach (var reminder in _watched)
            reminder.PropertyChanged -= OnReminderChanged;
        _watched.Clear();
    }
}
