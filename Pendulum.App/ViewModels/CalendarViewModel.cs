using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public partial class CalendarViewModel : ObservableObject
{
    private ObservableCollection<TriggerTimer> Triggers => AppServices.Instance.Triggers;

    [ObservableProperty] private DateTime displayedMonth;

    public string MonthYearLabel => DisplayedMonth.ToString("MMMM yyyy");

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

    public ObservableCollection<CalendarDayCell> Days { get; } = new();

    // Tracks which RecurrenceRule instance is currently subscribed per trigger, so that when
    // TriggerTimer.Recurrence is replaced (e.g. via the Repeat dialog) we can unsubscribe the
    // specific old instance — by the time the PropertyChanged event fires, t.Recurrence already
    // holds the new value, so there's no other way to reach the one we need to detach from.
    private readonly Dictionary<TriggerTimer, RecurrenceRule?> _wiredRecurrence = new();

    public CalendarViewModel()
    {
        displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        Triggers.CollectionChanged += OnTriggersCollectionChanged;
        foreach (var t in Triggers)
            Wire(t);

        RebuildGrid();
    }

    partial void OnDisplayedMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthYearLabel));
        OnPropertyChanged(nameof(SelectedMonthIndex));
        OnPropertyChanged(nameof(SelectedYear));
        RebuildGrid();
    }

    [RelayCommand]
    private void PreviousMonth() => DisplayedMonth = DisplayedMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => DisplayedMonth = DisplayedMonth.AddMonths(1);

    [RelayCommand]
    private void Today() => DisplayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

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
        if (e.OldItems is not null)
            foreach (TriggerTimer t in e.OldItems)
                Unwire(t);
        if (e.NewItems is not null)
            foreach (TriggerTimer t in e.NewItems)
                Wire(t);

        RebuildGrid();
    }

    private void Wire(TriggerTimer t)
    {
        t.PropertyChanged += OnTriggerPropertyChanged;
        _wiredRecurrence[t] = t.Recurrence;
        if (t.Recurrence is not null)
            t.Recurrence.PropertyChanged += OnRecurrencePropertyChanged;
    }

    private void Unwire(TriggerTimer t)
    {
        t.PropertyChanged -= OnTriggerPropertyChanged;
        if (_wiredRecurrence.TryGetValue(t, out var rule) && rule is not null)
            rule.PropertyChanged -= OnRecurrencePropertyChanged;
        _wiredRecurrence.Remove(t);
    }

    private void OnTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TriggerTimer t)
            return;

        if (e.PropertyName == nameof(TriggerTimer.Recurrence))
        {
            if (_wiredRecurrence.TryGetValue(t, out var oldRule) && oldRule is not null)
                oldRule.PropertyChanged -= OnRecurrencePropertyChanged;
            _wiredRecurrence[t] = t.Recurrence;
            if (t.Recurrence is not null)
                t.Recurrence.PropertyChanged += OnRecurrencePropertyChanged;
        }

        if (e.PropertyName is nameof(TriggerTimer.TriggerAt) or nameof(TriggerTimer.RecurrenceAnchor)
            or nameof(TriggerTimer.HasFired) or nameof(TriggerTimer.Enabled) or nameof(TriggerTimer.Name)
            or nameof(TriggerTimer.Recurrence))
        {
            RebuildGrid();
        }
    }

    private void OnRecurrencePropertyChanged(object? sender, PropertyChangedEventArgs e) => RebuildGrid();

    private void RebuildGrid()
    {
        var firstOfMonth = DisplayedMonth;
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var leadingDays = ((int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-leadingDays);
        var gridEnd = gridStart.AddDays(42).AddTicks(-1);
        var today = DateTime.Today;

        var cells = new List<CalendarDayCell>(42);
        for (int i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            cells.Add(new CalendarDayCell
            {
                Date = date,
                IsCurrentMonth = date.Month == firstOfMonth.Month && date.Year == firstOfMonth.Year,
                IsToday = date.Date == today
            });
        }

        foreach (var trigger in Triggers)
        {
            foreach (var occurrence in RecurrenceCalculator.GetOccurrencesInRange(trigger, gridStart, gridEnd))
            {
                var cell = cells.FirstOrDefault(c => c.Date == occurrence.Date);
                cell?.Reminders.Add(trigger);
            }
        }

        Days.Clear();
        foreach (var cell in cells)
            Days.Add(cell);
    }
}
