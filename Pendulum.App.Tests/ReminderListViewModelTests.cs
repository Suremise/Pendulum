using System.ComponentModel;
using System.Windows.Threading;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.App.Tests;

/// Sort and filter behavior on a UI thread, without constructing a view.
[Collection(nameof(SettingsScreen))]
public sealed class ReminderListViewModelTests
{
    private static TriggerTimer Reminder(string name, int day) => new() { Name = name, TriggerAt = new DateTime(2090, 1, day) };
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    [Fact]
    public void Sort_toggles_the_same_column_and_starts_a_new_column_ascending() => UiThread.Invoke(() =>
    {
        var bravo = Reminder("Bravo", 2);
        var alpha = Reminder("Alpha", 1);
        using var store = new ReminderStore([bravo, alpha]);
        var vm = new TriggersViewModel(store, new AppSettings()) { SelectedTrigger = alpha };
        Assert.Equal(new[] { alpha, bravo }, vm.View.Cast<TriggerTimer>());
        Assert.Equal(nameof(TriggerTimer.TriggerAt), vm.SortProperty);

        vm.SortByCommand.Execute(nameof(TriggerTimer.Name));
        Assert.Equal(ListSortDirection.Ascending, vm.SortDirection);
        Assert.Equal(new[] { alpha, bravo }, vm.View.Cast<TriggerTimer>());
        vm.SortByCommand.Execute(nameof(TriggerTimer.Name));
        Assert.Equal(ListSortDirection.Descending, vm.SortDirection);
        Assert.Equal(new[] { bravo, alpha }, vm.View.Cast<TriggerTimer>());
        vm.SortByCommand.Execute(nameof(TriggerTimer.TriggerAt));
        Assert.Equal(ListSortDirection.Ascending, vm.SortDirection);
        Assert.Equal(new[] { alpha, bravo }, vm.View.Cast<TriggerTimer>());
        Assert.Same(alpha, vm.SelectedTrigger);
        Assert.Equal(new[] { bravo, alpha }, store.Items);
    });

    [Fact]
    public void Sort_keys_and_filter_membership_follow_live_reminder_edits() => UiThread.Invoke(() =>
    {
        var alpha = Reminder("Alpha", 1);
        var bravo = Reminder("Bravo", 2);
        using var store = new ReminderStore([alpha, bravo]);
        var vm = new TriggersViewModel(store, new AppSettings());
        vm.SortByCommand.Execute(nameof(TriggerTimer.Name));
        alpha.Name = "Zulu";
        Drain();
        Assert.Equal(new[] { bravo, alpha }, vm.View.Cast<TriggerTimer>());
        vm.Filter.Name = "Zulu";
        Assert.Same(alpha, Assert.Single(vm.View.Cast<TriggerTimer>()));
        alpha.Name = "renamed";
        Drain();
        Assert.True(vm.HasNoResults);
        alpha.Name = "Zulu again";
        Drain();
        Assert.False(vm.HasNoResults);
        Assert.Same(alpha, Assert.Single(vm.View.Cast<TriggerTimer>()));
    });

    [Fact]
    public void No_results_distinguishes_an_empty_store_from_a_filtered_store() => UiThread.Invoke(() =>
    {
        using var store = new ReminderStore();
        var vm = new TriggersViewModel(store, new AppSettings());
        vm.Filter.Name = "match";
        Assert.False(vm.HasNoResults);
        var other = Reminder("other", 1);
        store.Add(other);
        Assert.True(vm.HasNoResults);
        var match = Reminder("match", 2);
        match.IsSelectedForBulk = true;
        store.Add(match);
        Assert.False(vm.HasNoResults);
        store.Remove(match);
        Assert.True(vm.HasNoResults);
        vm.Filter.Name = string.Empty;
        Assert.False(vm.HasNoResults);
        store.Remove(other);
        Assert.False(vm.HasNoResults);
        Assert.True(match.IsSelectedForBulk);
    });

    [Fact]
    public void View_models_keep_independent_sort_and_filter_state_for_the_same_store() => UiThread.Invoke(() =>
    {
        var alpha = Reminder("Alpha", 1);
        var bravo = Reminder("Bravo", 2);
        using var store = new ReminderStore([alpha, bravo]);
        var first = new TriggersViewModel(store, new AppSettings());
        var second = new TriggersViewModel(store, new AppSettings());
        first.SortByCommand.Execute(nameof(TriggerTimer.TriggerAt));
        first.Filter.Name = "Bravo";
        Assert.Same(bravo, Assert.Single(first.View.Cast<TriggerTimer>()));
        Assert.Equal(new[] { alpha, bravo }, second.View.Cast<TriggerTimer>());
        Assert.Equal(ListSortDirection.Ascending, second.SortDirection);
        Assert.NotSame(first.View, second.View);
    });

    [Fact]
    public void Saved_filters_and_nested_repeat_changes_work_without_a_panel() => UiThread.Invoke(() =>
    {
        var reminder = Reminder("repeat", 1);
        reminder.Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.None };
        using var store = new ReminderStore([reminder]);
        var settings = new AppSettings { ReminderFilterTypeFixed = true };
        var vm = new TriggersViewModel(store, settings);
        Assert.Same(reminder, Assert.Single(vm.View.Cast<TriggerTimer>()));
        reminder.Recurrence.Frequency = RecurrenceFrequency.Daily;
        Drain();
        Assert.True(vm.HasNoResults);
        settings.ReminderFilterTypeScheduled = true;
        Assert.Same(reminder, Assert.Single(vm.View.Cast<TriggerTimer>()));
        vm.Filter.StatusUpcoming = true;
        reminder.HasFired = true;
        Drain();
        Assert.True(vm.HasNoResults);
    });
}
