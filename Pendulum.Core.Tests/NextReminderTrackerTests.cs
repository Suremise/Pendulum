using System.Collections.ObjectModel;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.Core.Tests;

public sealed class NextReminderTrackerTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0);
    private static TriggerTimer Reminder(int minutes) => new() { TriggerAt = Now.AddMinutes(minutes) };

    [Fact]
    public void Membership_and_schedule_changes_update_the_cached_next_reminder()
    {
        var first = Reminder(1);
        var second = Reminder(2);
        var disabled = Reminder(0);
        disabled.Enabled = false;
        var spent = Reminder(-1);
        spent.HasFired = true;
        ObservableCollection<TriggerTimer> reminders = [second, first, disabled, spent];
        using var tracker = new NextReminderTracker(reminders);
        Assert.Same(first, tracker.Next);

        first.Enabled = false;
        Assert.Same(second, tracker.Next);
        disabled.Enabled = true;
        Assert.Same(disabled, tracker.Next);
        disabled.HasFired = true;
        Assert.Same(second, tracker.Next);
        first.Enabled = true;
        first.TriggerAt = Now.AddMinutes(3);
        Assert.Same(second, tracker.Next);
        second.TriggerAt = Now.AddMinutes(4);
        Assert.Same(first, tracker.Next);

        reminders.Remove(first);
        Assert.Same(second, tracker.Next);
        var replacement = Reminder(1);
        reminders[0] = replacement;
        Assert.Same(replacement, tracker.Next);
        reminders.Clear();
        Assert.Null(tracker.Next);
        reminders.Add(first);
        Assert.Same(first, tracker.Next);
    }

    [Fact]
    public void Equal_times_keep_collection_order_and_moves_are_observed()
    {
        var first = Reminder(1);
        var second = Reminder(1);
        ObservableCollection<TriggerTimer> reminders = [first, second];
        using var tracker = new NextReminderTracker(reminders);
        Assert.Same(first, tracker.Next);
        reminders.Move(1, 0);
        Assert.Same(second, tracker.Next);
    }

    [Fact]
    public void Reads_and_cosmetic_edits_do_not_refresh_but_the_next_name_does()
    {
        var next = Reminder(1);
        var later = Reminder(2);
        ObservableCollection<TriggerTimer> reminders = [next, later];
        using var tracker = new NextReminderTracker(reminders);
        var updates = 0;
        tracker.Changed += () => updates++;

        Assert.Same(next, tracker.Next);
        Assert.Same(next, tracker.Next);
        next.IsSelectedForBulk = true;
        next.Color = "teal";
        later.Name = "later";
        Assert.Equal(0, updates);
        next.Name = "renamed";
        Assert.Equal(1, updates);
    }

    [Fact]
    public void Removed_reset_and_disposed_reminders_no_longer_notify()
    {
        var first = Reminder(1);
        var second = Reminder(2);
        ObservableCollection<TriggerTimer> reminders = [first, second];
        using var tracker = new NextReminderTracker(reminders);
        var updates = 0;
        tracker.Changed += () => updates++;
        reminders.Remove(first);
        reminders.Clear();
        updates = 0;
        first.TriggerAt = Now;
        second.TriggerAt = Now;
        Assert.Equal(0, updates);
        reminders.Add(first);
        tracker.Dispose();
        updates = 0;
        first.Enabled = false;
        reminders.Add(second);
        Assert.Equal(0, updates);
    }

    [Fact]
    public void Removing_one_duplicate_keeps_watching_the_remaining_entry()
    {
        var next = Reminder(1);
        ObservableCollection<TriggerTimer> reminders = [next, next];
        using var tracker = new NextReminderTracker(reminders);
        reminders.RemoveAt(0);
        next.Enabled = false;
        Assert.Null(tracker.Next);
    }
}
