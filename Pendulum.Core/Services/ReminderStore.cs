using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;
using Pendulum.Core.Models;

namespace Pendulum.Core.Services;

public sealed class RemindersChangedEventArgs(
    bool membershipChanged, bool persisted, IReadOnlySet<string> properties, IReadOnlySet<TriggerTimer> items) : EventArgs
{
    public bool MembershipChanged { get; } = membershipChanged;
    public bool Persisted { get; } = persisted;
    public IReadOnlySet<string> Properties { get; } = properties;
    public IReadOnlySet<TriggerTimer> Items { get; } = items;
}

/// Owns membership and model subscriptions on the caller's thread. A synchronous batch emits
/// one change feed; it coalesces changes, rather than rolling back an arbitrary throwing Action.
public sealed class ReminderStore : IDisposable
{
    private static readonly HashSet<string> SavedProperties = typeof(TriggerTimer)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
        .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    private readonly ReminderCollection _items = new();
    private readonly Dictionary<TriggerTimer, RecurrenceRule?> _watched = new();
    private readonly Dictionary<RecurrenceRule, HashSet<TriggerTimer>> _ruleOwners = new();
    private readonly HashSet<string> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<TriggerTimer> _changedItems = new();
    private int _batch;
    private int _suppressPersistence;
    private bool _membershipChanged;
    private bool _persisted;
    private bool _publishing;
    private bool _disposed;

    public ReadOnlyObservableCollection<TriggerTimer> Items { get; }
    public event EventHandler<RemindersChangedEventArgs>? RemindersChanged;

    public ReminderStore(IEnumerable<TriggerTimer>? initial = null)
    {
        // Attach before the read-only wrapper forwards changes to views: new items must already
        // be watched if a binding reacts synchronously to the collection notification.
        _items.CollectionChanged += OnMembershipChanged;
        Items = new(_items);
        if (initial is not null) AddRange(initial);
    }

    public void ChangeTogether(Action change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _batch++;
        try { change(); }
        finally
        {
            _batch--;
            Publish();
        }
    }

    /// A removed reminder can still have an open alert/editor. Ignore that stale operation.
    public bool Update(TriggerTimer reminder, Action change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_watched.ContainsKey(reminder)) return false;
        ChangeTogether(change);
        return true;
    }

    public void Add(TriggerTimer reminder) => AddRange([reminder]);

    public void AddRange(IEnumerable<TriggerTimer> reminders)
    {
        var added = Materialize(reminders);
        ChangeTogether(() =>
        {
            foreach (var reminder in added)
            {
                reminder.Color ??= ReminderColors.ForNew(_items);
                _items.Add(reminder);
            }
        });
    }

    public bool Remove(TriggerTimer reminder)
    {
        var removed = false;
        ChangeTogether(() => removed = _items.Remove(reminder));
        return removed;
    }

    public void RemoveRange(IEnumerable<TriggerTimer> reminders)
    {
        var removed = reminders.ToList();
        ChangeTogether(() => { foreach (var reminder in removed) _items.Remove(reminder); });
    }

    public void Move(int oldIndex, int newIndex) => ChangeTogether(() => _items.Move(oldIndex, newIndex));

    /// Restore/load uses one Reset. Import preserves unchanged rows through incremental
    /// notifications, retaining selection while still publishing just one owner change.
    public void ReplaceAll(IEnumerable<TriggerTimer> reminders, bool preserveExisting = false, bool markDirty = true)
    {
        var desired = Materialize(reminders);
        ChangeTogether(() =>
        {
            if (!markDirty) _suppressPersistence++;
            try
            {
                if (_items.SequenceEqual(desired)) return;
                if (preserveExisting) ApplyMembership(desired);
                else
                {
                    var preceding = new List<TriggerTimer>();
                    foreach (var reminder in desired)
                    {
                        reminder.Color ??= ReminderColors.ForNew(preceding);
                        preceding.Add(reminder);
                    }
                    _items.ResetTo(desired);
                }
            }
            finally { if (!markDirty) _suppressPersistence--; }
        });
    }

    private void ApplyMembership(List<TriggerTimer> desired)
    {
        var remaining = desired.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (remaining.TryGetValue(item, out var count) && count > 0) remaining[item] = count - 1;
            else _items.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < _items.Count && ReferenceEquals(_items[i], item)) continue;
            var found = -1;
            for (var j = i + 1; j < _items.Count; j++)
                if (ReferenceEquals(_items[j], item)) { found = j; break; }
            if (found >= 0) _items.Move(found, i);
            else
            {
                // Choose against live committed membership, including independent additions
                // made during prompts and the preceding imported items.
                item.Color ??= ReminderColors.ForNew(_items);
                _items.Insert(i, item);
            }
        }
    }

    private static List<TriggerTimer> Materialize(IEnumerable<TriggerTimer> reminders)
    {
        var result = reminders.ToList();
        foreach (var reminder in result) ArgumentNullException.ThrowIfNull(reminder);
        return result;
    }

    private void OnMembershipChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            var current = _items.ToHashSet();
            foreach (var gone in _watched.Keys.Where(t => !current.Contains(t)).ToList())
            {
                _changedItems.Add(gone);
                Unwatch(gone);
            }
            foreach (var added in current) Watch(added);
            _changedItems.UnionWith(current);
        }
        else
        {
            if (e.OldItems is not null)
                foreach (TriggerTimer old in e.OldItems)
                {
                    _changedItems.Add(old);
                    if (!_items.Contains(old)) Unwatch(old);
                }
            if (e.NewItems is not null)
                foreach (TriggerTimer added in e.NewItems) { Watch(added); _changedItems.Add(added); }
        }
        _membershipChanged = true;
        _persisted |= _suppressPersistence == 0;
        Publish();
    }

    private void Watch(TriggerTimer reminder)
    {
        if (!_watched.TryAdd(reminder, null)) return;
        reminder.PropertyChanged += OnReminderChanged;
        WatchRule(reminder);
    }

    private void Unwatch(TriggerTimer reminder)
    {
        reminder.PropertyChanged -= OnReminderChanged;
        UnwatchRule(reminder);
        _watched.Remove(reminder);
    }

    private void UnwatchRule(TriggerTimer reminder)
    {
        if (!_watched.TryGetValue(reminder, out var rule) || rule is null) return;
        var owners = _ruleOwners[rule];
        owners.Remove(reminder);
        if (owners.Count != 0) return;
        rule.PropertyChanged -= OnRuleChanged;
        _ruleOwners.Remove(rule);
    }

    private void WatchRule(TriggerTimer reminder)
    {
        UnwatchRule(reminder);
        var rule = reminder.Recurrence;
        _watched[reminder] = rule;
        if (rule is null) return;
        if (!_ruleOwners.TryGetValue(rule, out var owners))
        {
            owners = new();
            _ruleOwners.Add(rule, owners);
            rule.PropertyChanged += OnRuleChanged;
        }
        owners.Add(reminder);
    }

    private void OnReminderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TriggerTimer reminder || !_watched.ContainsKey(reminder)) return;
        if (e.PropertyName == nameof(TriggerTimer.Recurrence)) WatchRule(reminder);
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            WatchRule(reminder);
            _properties.UnionWith(SavedProperties);
            Mark(reminder, nameof(TriggerTimer.IsSelectedForBulk), persisted: true);
        }
        else if (SavedProperties.Contains(e.PropertyName)) Mark(reminder, e.PropertyName, persisted: true);
        else if (e.PropertyName == nameof(TriggerTimer.IsSelectedForBulk)) Mark(reminder, e.PropertyName, persisted: false);
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RecurrenceRule rule || !_ruleOwners.TryGetValue(rule, out var owners)) return;
        ChangeTogether(() =>
        {
            foreach (var reminder in owners.ToList()) Mark(reminder, nameof(TriggerTimer.Recurrence), persisted: true);
        });
    }

    private void Mark(TriggerTimer reminder, string property, bool persisted)
    {
        _changedItems.Add(reminder);
        _properties.Add(property);
        _persisted |= persisted && _suppressPersistence == 0;
        Publish();
    }

    private void Publish()
    {
        if (_batch != 0 || _publishing) return;
        while (_membershipChanged || _properties.Count != 0)
        {
            var change = new RemindersChangedEventArgs(_membershipChanged, _persisted, _properties.ToHashSet(), _changedItems.ToHashSet());
            _membershipChanged = _persisted = false;
            _properties.Clear();
            _changedItems.Clear();
            _publishing = true;
            try { RemindersChanged?.Invoke(this, change); }
            finally { _publishing = false; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _items.CollectionChanged -= OnMembershipChanged;
        foreach (var reminder in _watched.Keys.ToList()) Unwatch(reminder);
        _properties.Clear();
        _changedItems.Clear();
        RemindersChanged = null;
    }

    private sealed class ReminderCollection : ObservableCollection<TriggerTimer>
    {
        public void ResetTo(List<TriggerTimer> replacement)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in replacement) Items.Add(item);
            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]")); // not user-visible: collection binding property
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
    }
}
