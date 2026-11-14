using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class CalendarFirstFrameTests
{
    private static readonly DateTime Today = new(2026, 11, 14);

    private static void Drain(Queue<Action> posted)
    {
        while (posted.TryDequeue(out var action)) action();
    }

    private static void AssertCurrentGrid(CalendarViewModel vm)
    {
        Assert.Equal(42, vm.Days.Count);
        var current = vm.Days.Where(day => day.IsCurrentMonth).ToList();
        Assert.Equal(vm.DisplayedMonth, current[0].Date);
        Assert.Equal(DateTime.DaysInMonth(vm.DisplayedMonth.Year, vm.DisplayedMonth.Month), current.Count);
        Assert.All(current, day => Assert.Equal(vm.DisplayedMonth.Month, day.Date.Month));
        Assert.All(vm.Days, day => Assert.Equal(vm.ChipLines, day.Lines));
        Assert.Equal(DisplayFormat.MonthAndYear(vm.DisplayedMonth), vm.MonthYearLabel);
    }

    [Fact]
    public void First_display_has_a_grid_before_the_render_priority_can_run() => UiThread.Invoke(() =>
    {
        using var store = new ReminderStore();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var vm = new CalendarViewModel(store, () => Today,
            action => dispatcher.BeginInvoke(action, DispatcherPriority.Background));
        try
        {
            vm.RefreshForDisplay();
            dispatcher.Invoke(() => AssertCurrentGrid(vm), DispatcherPriority.Render);
        }
        finally
        {
            vm.SetVisible(false);
            dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    });

    [Theory]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("month")]
    [InlineData("year")]
    [InlineData("today")]
    public void Navigation_publishes_a_matching_grid_without_waiting_for_background_work(string navigation) => UiThread.Invoke(() =>
    {
        using var store = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => Today, posted.Enqueue);
        if (navigation == "today") vm.DisplayedMonth = Today.AddMonths(-2).Date.AddDays(1 - Today.Day);
        vm.RefreshForDisplay();
        Drain(posted);
        var rebuilds = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Days)) rebuilds++; };

        switch (navigation)
        {
            case "next": vm.NextMonthCommand.Execute(null); break;
            case "previous": vm.PreviousMonthCommand.Execute(null); break;
            case "month": vm.SelectedMonthIndex = 1; break;
            case "year": vm.SelectedYear++; break;
            case "today": vm.TodayCommand.Execute(null); break;
        }

        Dispatcher.CurrentDispatcher.Invoke(() => AssertCurrentGrid(vm), DispatcherPriority.Render);
        Assert.Equal(1, rebuilds);
        Assert.Empty(posted);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Resizing_replaces_chip_lines_before_render(int lines) => UiThread.Invoke(() =>
    {
        using var store = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => Today, posted.Enqueue);
        vm.RefreshForDisplay();
        Drain(posted);

        vm.ChipLines = lines;

        Dispatcher.CurrentDispatcher.Invoke(() => AssertCurrentGrid(vm), DispatcherPriority.Render);
        Assert.Empty(posted);
    });

    [Fact]
    public void Returning_from_hidden_updates_the_month_size_and_today_before_render() => UiThread.Invoke(() =>
    {
        var today = Today;
        using var store = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        Drain(posted);
        var initial = vm.Days;
        vm.SetVisible(false);
        vm.DisplayedMonth = Today.AddMonths(1).AddDays(1 - Today.Day);
        vm.ChipLines = 0;
        today = Today.AddMonths(1);
        store.Add(new() { TriggerAt = today });
        Assert.Same(initial, vm.Days);
        Assert.Empty(posted);

        vm.RefreshForDisplay();

        Dispatcher.CurrentDispatcher.Invoke(() => AssertCurrentGrid(vm), DispatcherPriority.Render);
        Assert.Equal(today, Assert.Single(vm.Days, day => day.IsToday).Date);
        Assert.Empty(posted);
    });

    [Fact]
    public void A_laid_out_calendar_has_current_cells_on_the_first_render_after_resize() => UiThread.Invoke(() =>
    {
        using var store = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => Today, posted.Enqueue);
        var panel = new CalendarPanel { DataContext = vm, Width = 1000, Height = 760 };
        vm.RefreshForDisplay();
        try
        {
            LayoutAndCheck();
            var tallLines = vm.ChipLines;
            panel.Height = 360;
            LayoutAndCheck();
            Assert.True(vm.ChipLines < tallLines);
            panel.Height = 900;
            LayoutAndCheck();
            Assert.True(vm.ChipLines > tallLines);
            Assert.Empty(posted);
        }
        finally { vm.SetVisible(false); }

        void LayoutAndCheck()
        {
            panel.Measure(new Size(panel.Width, panel.Height));
            panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
            panel.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() =>
            {
                panel.UpdateLayout();
                AssertCurrentGrid(vm);
                Assert.Equal(CalendarPanel.LinesThatFit(panel.DayGrid.ActualHeight), vm.ChipLines);
                Assert.Same(vm.Days, panel.DayGrid.ItemsSource);
                for (var i = 0; i < vm.Days.Count; i++)
                    Assert.Same(vm.Days[i], Assert.IsType<ContentPresenter>(panel.DayGrid.ItemContainerGenerator.ContainerFromIndex(i)).Content);
            }, DispatcherPriority.Render);
        }
    });

    [Fact]
    public void Today_refreshes_a_changed_date_in_the_same_month_without_an_extra_callback() => UiThread.Invoke(() =>
    {
        var today = Today;
        using var store = new ReminderStore();
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => today, posted.Enqueue);
        vm.RefreshForDisplay();
        var initial = vm.Days;
        today = today.AddDays(1);

        vm.TodayCommand.Execute(null);

        Assert.NotSame(initial, vm.Days);
        Assert.Equal(today, Assert.Single(vm.Days, day => day.IsToday).Date);
        Assert.Empty(posted);
        var refreshed = vm.Days;
        vm.TodayCommand.Execute(null);
        Assert.Same(refreshed, vm.Days);
    });

    [Fact]
    public void Navigation_consumes_pending_changes_but_later_changes_still_coalesce() => UiThread.Invoke(() =>
    {
        var trigger = new TriggerTimer { TriggerAt = Today, RecurrenceAnchor = Today, Recurrence = new() };
        using var store = new ReminderStore([trigger]);
        var posted = new Queue<Action>();
        var vm = new CalendarViewModel(store, () => Today, posted.Enqueue);
        vm.RefreshForDisplay();
        Drain(posted);
        trigger.Name = "queued before navigation";
        Assert.Single(posted);
        vm.NextMonthCommand.Execute(null);
        AssertCurrentGrid(vm);
        var immediate = vm.Days;
        Drain(posted);
        Assert.Same(immediate, vm.Days);

        trigger.Recurrence!.OccurrencesSoFar++;
        trigger.HasFired = true;
        Assert.Same(immediate, vm.Days);
        Assert.Single(posted);
        vm.ChipLines = 0;
        AssertCurrentGrid(vm);
        var resized = vm.Days;
        trigger.Name = "after resize but before pending callback";
        trigger.Enabled = false;
        Assert.Single(posted);
        Drain(posted);
        Assert.NotSame(resized, vm.Days);
        AssertCurrentGrid(vm);
    });
}
