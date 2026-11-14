using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.Core.Tests;

public sealed class ReminderStoreTests
{
    [Fact]
    public void Nested_updates_publish_one_complete_persisted_change()
    {
        var reminder = new TriggerTimer { Recurrence = new() };
        using var store = new ReminderStore([reminder]);
        var changes = new List<RemindersChangedEventArgs>();
        store.RemindersChanged += (_, e) => changes.Add(e);
        store.ChangeTogether(() =>
        {
            store.Update(reminder, () => reminder.Name = "edited");
            store.Update(reminder, () =>
            {
                reminder.Recurrence.OccurrencesSoFar++;
                reminder.HasFired = true;
            });
            Assert.Empty(changes);
        });
        var change = Assert.Single(changes);
        Assert.True(change.Persisted);
        Assert.False(change.MembershipChanged);
        Assert.Contains(nameof(TriggerTimer.Name), change.Properties);
        Assert.Contains(nameof(TriggerTimer.Recurrence), change.Properties);
        Assert.Contains(nameof(TriggerTimer.HasFired), change.Properties);
        Assert.DoesNotContain(nameof(TriggerTimer.StatusSortOrder), change.Properties);
        Assert.Same(reminder, Assert.Single(change.Items));
    }

    [Fact]
    public void Selection_does_not_persist_or_recompute_the_next_reminder()
    {
        var reminder = new TriggerTimer();
        using var store = new ReminderStore([reminder]);
        using var tracker = new NextReminderTracker(store);
        var recomputes = 0;
        tracker.Changed += () => recomputes++;
        var changes = new List<RemindersChangedEventArgs>();
        store.RemindersChanged += (_, e) => changes.Add(e);
        reminder.IsSelectedForBulk = true;
        Assert.False(Assert.Single(changes).Persisted);
        Assert.Equal(0, recomputes);
        changes.Clear();
        reminder.Color = reminder.Color == "teal" ? "sage" : "teal";
        Assert.True(Assert.Single(changes).Persisted); // derived DisplayColor is not a second change
        Assert.Equal(0, recomputes);
    }

    [Fact]
    public void Add_range_keeps_incremental_binding_notifications_and_one_owner_change()
    {
        using var store = new ReminderStore();
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)store.Items).CollectionChanged += (_, e) => collectionChanges.Add(e.Action);
        var changes = 0;
        store.RemindersChanged += (_, _) => changes++;
        store.AddRange([new(), new(), new()]);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add }, collectionChanges);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Replacement_detaches_old_items_and_rules_including_shared_rules_and_duplicates()
    {
        var rule = new RecurrenceRule();
        var first = new TriggerTimer { Recurrence = rule };
        var second = new TriggerTimer { Recurrence = rule };
        using var store = new ReminderStore([first, first, second]);
        var changes = new List<RemindersChangedEventArgs>();
        store.RemindersChanged += (_, e) => changes.Add(e);
        store.Remove(first);
        changes.Clear();
        rule.OccurrencesSoFar++;
        Assert.Equal(2, Assert.Single(changes).Items.Count);
        store.Remove(first);
        changes.Clear();
        rule.OccurrencesSoFar++;
        Assert.Same(second, Assert.Single(Assert.Single(changes).Items));
        second.Recurrence = new();
        changes.Clear();
        rule.OccurrencesSoFar++;
        first.Name = "removed";
        Assert.Empty(changes);
        store.ReplaceAll([new()]);
        changes.Clear();
        second.Name = "restored away";
        second.Recurrence.OccurrencesSoFar++;
        Assert.Empty(changes);
    }

    [Fact]
    public void A_restored_away_item_and_rule_do_not_keep_the_store_alive()
    {
        var (weak, old, rule) = ReplacedStore();
        for (var i = 0; i < 5 && IsAlive(weak); i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Assert.False(IsAlive(weak));
        GC.KeepAlive(old);
        GC.KeepAlive(rule);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<ReminderStore>, TriggerTimer, RecurrenceRule) ReplacedStore()
    {
        var rule = new RecurrenceRule();
        var old = new TriggerTimer { Recurrence = rule };
        var store = new ReminderStore([old]);
        store.ReplaceAll([new()]);
        return (new(store), old, rule);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<ReminderStore> weak) => weak.TryGetTarget(out _);

    [Fact]
    public void Update_ignores_a_removed_target_and_disposal_detaches_remaining_items()
    {
        var reminder = new TriggerTimer();
        using var store = new ReminderStore([reminder]);
        var changes = 0;
        store.RemindersChanged += (_, _) => changes++;
        Assert.False(store.Update(new(), () => throw new InvalidOperationException()));
        Assert.Equal(0, changes);
        store.Dispose();
        reminder.Name = "after disposal";
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Initial_load_does_not_mark_dirty_but_later_changes_do()
    {
        using var store = new ReminderStore();
        var reminder = new TriggerTimer();
        var changes = new List<RemindersChangedEventArgs>();
        store.RemindersChanged += (_, e) => changes.Add(e);
        store.ReplaceAll([reminder], markDirty: false);
        Assert.True(Assert.Single(changes).MembershipChanged);
        Assert.False(changes[0].Persisted);
        changes.Clear();
        reminder.Name = "after load";
        Assert.True(Assert.Single(changes).Persisted);
    }

    [Fact]
    public void An_engine_firing_is_one_owner_batch_and_still_delivers_its_alert()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0);
        var reminder = new TriggerTimer { TriggerAt = now, Recurrence = new() };
        using var store = new ReminderStore([reminder]);
        using var engine = new TimerEngine(() => now, polls: false, changeTogether: store.ChangeTogether);
        var changes = 0;
        var alerts = 0;
        store.RemindersChanged += (_, _) => changes++;
        engine.TriggerFired += _ => alerts++;
        engine.SetTriggers(store.Items);
        engine.Start(null);
        engine.CheckDue();
        Assert.Equal(1, changes);
        Assert.Equal(1, alerts);
        Assert.Equal(1, reminder.Recurrence.OccurrencesSoFar);
    }

    [Fact]
    public void A_throwing_synchronous_action_publishes_its_applied_changes_and_ends_the_batch()
    {
        var reminder = new TriggerTimer();
        using var store = new ReminderStore([reminder]);
        var changes = 0;
        store.RemindersChanged += (_, _) => changes++;
        Assert.Throws<InvalidOperationException>(() => store.ChangeTogether(() =>
        {
            reminder.Name = "applied";
            throw new InvalidOperationException();
        }));
        Assert.Equal(1, changes);
        reminder.Name = "next";
        Assert.Equal(2, changes);
    }
}
