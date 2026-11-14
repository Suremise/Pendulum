using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public partial class CalendarViewModel : ObservableObject
{
    private readonly ObservableCollection<TriggerTimer> Triggers;
    private readonly Func<DateTime> _today;
    private readonly Action<Action> _post;
    private DateTime _lastToday;
    private bool _visible;
    private bool _dirty = true;
    private bool _rebuildPosted;

    [ObservableProperty] private DateTime displayedMonth;

    public string MonthYearLabel => DisplayFormat.MonthAndYear(DisplayedMonth);

    public IReadOnlyList<string> MonthNames { get; } = Enumerable.Range(1, 12)
        .Select(m => new DateTime(2000, m, 1).ToString("MMMM"))
        .ToList();

    public IReadOnlyList<int> Years { get; } = Enumerable.Range(DateTime.Today.Year - 5, 16).ToList();

    public IReadOnlyList<string> WeekdayHeaders { get; } = BuildWeekdayHeaders();

    private static List<string> BuildWeekdayHeaders()
    {
        var first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7)
            .Select(i => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(first + i) % 7])
            .ToList();
    }

    public int SelectedMonthIndex
    {
        get => DisplayedMonth.Month - 1;
        set => DisplayedMonth = new DateTime(DisplayedMonth.Year, value + 1, 1);
    }

    public int SelectedYear
    {
        get => DisplayedMonth.Year;
        set => DisplayedMonth = new DateTime(value, DisplayedMonth.Month, 1);
    }

    private IReadOnlyList<CalendarDayCell> _days = Array.Empty<CalendarDayCell>();
    public IReadOnlyList<CalendarDayCell> Days
    {
        get => _days;
        private set => SetProperty(ref _days, value);
    }

    /// How many lines of chips fit in a day's cell, as the view measures it (CalendarPanel): the
    /// grid is rebuilt when it changes.
    private int _chipLines = 2;

    public int ChipLines
    {
        get => _chipLines;
        set
        {
            value = Math.Max(0, value);
            if (value == _chipLines)
                return;
            _chipLines = value;
            OnPropertyChanged();
            MarkDirty();
        }
    }

    // Tracks which RecurrenceRule instance is currently subscribed per trigger, so that when
    // TriggerTimer.Recurrence is replaced (e.g. via the Repeat dialog) we can unsubscribe the
    // specific old instance — by the time the PropertyChanged event fires, t.Recurrence already
    // holds the new value, so there's no other way to reach the one we need to detach from.
    private readonly Dictionary<TriggerTimer, RecurrenceRule?> _wiredRecurrence = new();

    public CalendarViewModel() : this(AppServices.Instance.Triggers, () => DateTime.Today,
        action => Application.Current.Dispatcher.BeginInvoke(action, DispatcherPriority.Background))
    {
    }

    internal CalendarViewModel(ObservableCollection<TriggerTimer> triggers, Func<DateTime> today, Action<Action> post)
    {
        Triggers = triggers;
        _today = today;
        _post = post;
        _lastToday = today().Date;
        displayedMonth = new DateTime(_lastToday.Year, _lastToday.Month, 1);

        // Views can be unloaded and loaded again with the same view model. Weak subscriptions
        // let a closed window's calendar go away without keeping it alive through the store.
        CollectionChangedEventManager.AddHandler(Triggers, OnTriggersCollectionChanged);
        foreach (var t in Triggers)
            Wire(t);
    }

    partial void OnDisplayedMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthYearLabel));
        OnPropertyChanged(nameof(SelectedMonthIndex));
        OnPropertyChanged(nameof(SelectedYear));
        MarkDirty();
    }

    [RelayCommand]
    private void PreviousMonth() => DisplayedMonth = DisplayedMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => DisplayedMonth = DisplayedMonth.AddMonths(1);

    [RelayCommand]
    private void Today()
    {
        RefreshDate();
        DisplayedMonth = new DateTime(_lastToday.Year, _lastToday.Month, 1);
    }

    /// Also used by the off-screen renderer: prepare the calendar as if it became visible.
    public void RefreshForDisplay() => SetVisible(true);

    internal void SetVisible(bool visible)
    {
        _visible = visible;
        if (!visible) return;
        RefreshDate();
        ScheduleRebuild();
    }

    /// Called at midnight, when shown again, and after a time change or resume. It never moves
    /// the month the user is browsing; only the today marker and reminder occurrences change.
    internal void RefreshDate()
    {
        var today = _today().Date;
        if (today == _lastToday) return;
        _lastToday = today;
        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (!_visible || !_dirty || _rebuildPosted) return;
        _rebuildPosted = true;
        _post(() =>
        {
            _rebuildPosted = false;
            if (!_visible || !_dirty) return;
            _dirty = false;
            RebuildGrid();
        });
    }

    [RelayCommand]
    private void EditReminder(TriggerTimer? trigger)
    {
        if (trigger is null)
            return;

        var dialog = new TriggerEditWindow(trigger, isNew: false) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
            AppServices.Instance.RefreshTriggers();
    }

    private void OnTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var t in _wiredRecurrence.Keys.ToList()) Unwire(t);
            foreach (var t in Triggers) Wire(t);
        }
        if (e.OldItems is not null)
            foreach (TriggerTimer t in e.OldItems)
                Unwire(t);
        if (e.NewItems is not null)
            foreach (TriggerTimer t in e.NewItems)
                Wire(t);

        MarkDirty();
    }

    private void Wire(TriggerTimer t)
    {
        PropertyChangedEventManager.AddHandler(t, OnTriggerPropertyChanged, string.Empty);
        _wiredRecurrence[t] = t.Recurrence;
        if (t.Recurrence is not null)
            PropertyChangedEventManager.AddHandler(t.Recurrence, OnRecurrencePropertyChanged, string.Empty);
    }

    private void Unwire(TriggerTimer t)
    {
        PropertyChangedEventManager.RemoveHandler(t, OnTriggerPropertyChanged, string.Empty);
        if (_wiredRecurrence.TryGetValue(t, out var rule) && rule is not null)
            PropertyChangedEventManager.RemoveHandler(rule, OnRecurrencePropertyChanged, string.Empty);
        _wiredRecurrence.Remove(t);
    }

    private void OnTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TriggerTimer t)
            return;

        if (e.PropertyName == nameof(TriggerTimer.Recurrence))
        {
            if (_wiredRecurrence.TryGetValue(t, out var oldRule) && oldRule is not null)
                PropertyChangedEventManager.RemoveHandler(oldRule, OnRecurrencePropertyChanged, string.Empty);
            _wiredRecurrence[t] = t.Recurrence;
            if (t.Recurrence is not null)
                PropertyChangedEventManager.AddHandler(t.Recurrence, OnRecurrencePropertyChanged, string.Empty);
        }

        if (e.PropertyName is nameof(TriggerTimer.TriggerAt) or nameof(TriggerTimer.RecurrenceAnchor)
            or nameof(TriggerTimer.HasFired) or nameof(TriggerTimer.Enabled) or nameof(TriggerTimer.Name)
            or nameof(TriggerTimer.Recurrence))
        {
            MarkDirty();
        }
    }

    // TimerEngine and user edits both change reminders on the UI thread. Posting once at
    // Background priority coalesces the whole operation instead of rebuilding per property.
    private void OnRecurrencePropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void RebuildGrid()
    {
        var firstOfMonth = DisplayedMonth;
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var leadingDays = ((int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-leadingDays);
        var gridEnd = gridStart.AddDays(42).AddTicks(-1);
        var today = _today().Date;
        _lastToday = today;

        var cells = new List<CalendarDayCell>(42);
        for (int i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            cells.Add(new CalendarDayCell
            {
                Date = date,
                IsCurrentMonth = date.Month == firstOfMonth.Month && date.Year == firstOfMonth.Year,
                IsToday = date.Date == today,
                Lines = ChipLines,
            });
        }

        foreach (var trigger in Triggers)
        {
            foreach (var occurrence in RecurrenceCalculator.GetOccurrencesInRange(trigger, gridStart, gridEnd))
            {
                var index = (occurrence.Date - gridStart).Days;
                if (index >= 0 && index < cells.Count)
                    cells[index].Reminders.Add(trigger);
            }
        }

        Days = cells;
    }
}
