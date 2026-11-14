using Pendulum.Core.Services;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class CalendarRefreshTests
{
    [Fact]
    public void The_real_dispatcher_coalesces_a_firing_before_replacing_the_bound_list() => UiThread.Invoke(() =>
    {
        var trigger = new TriggerTimer { Recurrence = new() };
        AppServices.Instance.Reminders.Add(trigger);
        var vm = new CalendarViewModel();
        try
        {
            vm.RefreshForDisplay();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var rebuilds = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Days)) rebuilds++; };
            trigger.Recurrence.OccurrencesSoFar++;
            trigger.HasFired = true;
            Assert.Equal(0, rebuilds);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(1, rebuilds);
        }
        finally
        {
            vm.SetVisible(false);
            AppServices.Instance.Reminders.Remove(trigger);
        }
    });

    [Fact]
    public void Date_indexing_keeps_occurrences_on_both_edges_and_every_day_between() => UiThread.Invoke(() =>
    {
        var today = new DateTime(2026, 11, 14);
        using var triggers = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(triggers, () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
        var first = vm.Days[0].Date;
        var last = vm.Days[^1].Date;
        var daily = new TriggerTimer { TriggerAt = first, RecurrenceAnchor = first, Recurrence = new() };
        var left = new TriggerTimer { TriggerAt = first };
        var right = new TriggerTimer { TriggerAt = last.AddHours(23) };
        triggers.Add(daily);
        triggers.Add(left);
        triggers.Add(right);
        triggers.Add(new() { TriggerAt = first.AddDays(-1) });
        triggers.Add(new() { TriggerAt = last.AddDays(1) });
        Assert.Single(posted)();
        Assert.All(vm.Days, day => Assert.Contains(daily, day.Reminders));
        Assert.Contains(left, vm.Days[0].Reminders);
        Assert.Contains(right, vm.Days[^1].Reminders);
        Assert.Equal(44, vm.Days.Sum(day => day.Reminders.Count));
    });

    [Fact]
    public void A_firing_and_snooze_each_replace_the_grid_once_after_the_whole_change() => UiThread.Invoke(() =>
    {
        var today = new DateTime(2026, 11, 14);
        var trigger = new TriggerTimer { TriggerAt = today.AddHours(9), RecurrenceAnchor = today, Recurrence = new() };
        using var triggers = new ReminderStore([trigger]);
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(triggers, () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
        var rebuilds = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Days)) rebuilds++; };

        trigger.Recurrence.OccurrencesSoFar++;
        trigger.HasFired = true;
        Assert.Equal(0, rebuilds);
        Assert.Single(posted)();
        posted.Clear();
        Assert.Equal(1, rebuilds);

        Pendulum.Core.Engine.TriggerReconciler.Snooze(trigger, today.AddHours(9), 5, TimeZoneInfo.Utc);
        Assert.Equal(1, rebuilds);
        Assert.Single(posted)();
        Assert.Equal(2, rebuilds);
        Assert.Equal(42, vm.Days.Count);
    });

    [Fact]
    public void Hidden_changes_wait_until_display_and_repeated_display_does_not_rebuild() => UiThread.Invoke(() =>
    {
        var today = new DateTime(2026, 11, 14);
        var trigger = new TriggerTimer { TriggerAt = today, RecurrenceAnchor = today };
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(new ReminderStore([trigger]), () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
        var initial = vm.Days;
        trigger.Name = "Before hiding";
        vm.SetVisible(false);
        posted.Dequeue()(); // a callback queued before hiding must not rebuild
        trigger.Name = "While hidden";
        trigger.Enabled = false;
        vm.ChipLines = 3;
        Assert.Empty(posted);
        Assert.Same(initial, vm.Days);

        vm.RefreshForDisplay();
        Assert.Empty(posted);
        Assert.NotSame(initial, vm.Days);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Midnight_and_returning_from_hidden_refresh_today_without_changing_the_browsed_month(bool hidden) => UiThread.Invoke(() =>
    {
        var today = new DateTime(2026, 11, 14);
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(new(), () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
        Assert.Equal(today, Assert.Single(vm.Days, d => d.IsToday).Date);
        var month = vm.DisplayedMonth;
        vm.SetVisible(!hidden);
        today = today.AddDays(1);
        vm.RefreshDate(); // the same entry point as midnight, resume and clock changes
        if (hidden)
        {
            Assert.Empty(posted);
            vm.RefreshForDisplay();
            Assert.Empty(posted);
        }
        else Assert.Single(posted)();
        Assert.Equal(today, Assert.Single(vm.Days, d => d.IsToday).Date);
        Assert.Equal(month, vm.DisplayedMonth);
    });

    [Fact]
    public void Reset_and_replacing_a_repeat_rule_detach_the_old_subscriptions() => UiThread.Invoke(() =>
    {
        var today = new DateTime(2026, 11, 14);
        var oldRule = new RecurrenceRule();
        var trigger = new TriggerTimer { Recurrence = oldRule };
        using var triggers = new ReminderStore([trigger]);
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(triggers, () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Assert.Empty(posted);
        trigger.Recurrence = new();
        posted.Dequeue()();
        oldRule.OccurrencesSoFar++;
        Assert.Empty(posted);
        triggers.ReplaceAll([]);
        posted.Dequeue()();
        trigger.Name = "Removed";
        trigger.Recurrence.OccurrencesSoFar++;
        Assert.Empty(posted);
    });

    [Fact]
    public void A_calendar_that_has_not_been_displayed_does_not_build_when_a_reminder_fires() => UiThread.Invoke(() =>
    {
        var trigger = new TriggerTimer { Recurrence = new() };
        AppServices.Instance.Reminders.Add(trigger);
        try
        {
            var vm = new CalendarViewModel();
            trigger.Recurrence.OccurrencesSoFar++;
            trigger.HasFired = true;
            Assert.Empty(vm.Days);
        }
        finally { AppServices.Instance.Reminders.Remove(trigger); }
    });
}
