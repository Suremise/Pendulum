using System.Globalization;
using System.Windows;
using Pendulum.Core.Models;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class RecurrenceWindow : FluentWindow
{
    private readonly DateTime _anchor;

    /// The configured rule if the user clicked OK, or null if they clicked "Don't Repeat".
    /// Only meaningful when DialogResult == true.
    public RecurrenceRule? Result { get; private set; }

    public RecurrenceWindow(DateTime anchor, RecurrenceRule? existing)
    {
        InitializeComponent();
        _anchor = anchor;

        StartText.Text = $"Start: {anchor:dd MMMM yyyy, HH:mm}";

        foreach (var monthName in CultureInfo.CurrentCulture.DateTimeFormat.MonthNames)
        {
            if (!string.IsNullOrEmpty(monthName))
                YearlyMonthBox.Items.Add(monthName);
        }

        LoadFrom(existing);
    }

    private void LoadFrom(RecurrenceRule? rule)
    {
        rule ??= new RecurrenceRule { Frequency = RecurrenceFrequency.Daily };

        DailyIntervalBox.Text = rule.Interval.ToString();
        WeeklyIntervalBox.Text = rule.Interval.ToString();
        MonthlyIntervalBox.Text = rule.Interval.ToString();

        MonthlyDayBox.Text = (rule.DayOfMonth > 0 ? rule.DayOfMonth : _anchor.Day).ToString();
        YearlyDayBox.Text = (rule.DayOfMonth > 0 ? rule.DayOfMonth : _anchor.Day).ToString();
        YearlyMonthBox.SelectedIndex = (rule.Month > 0 ? rule.Month : _anchor.Month) - 1;

        var days = rule.DaysOfWeek.Count > 0 ? rule.DaysOfWeek : new List<DayOfWeek> { _anchor.DayOfWeek };
        MonCheck.IsChecked = days.Contains(DayOfWeek.Monday);
        TueCheck.IsChecked = days.Contains(DayOfWeek.Tuesday);
        WedCheck.IsChecked = days.Contains(DayOfWeek.Wednesday);
        ThuCheck.IsChecked = days.Contains(DayOfWeek.Thursday);
        FriCheck.IsChecked = days.Contains(DayOfWeek.Friday);
        SatCheck.IsChecked = days.Contains(DayOfWeek.Saturday);
        SunCheck.IsChecked = days.Contains(DayOfWeek.Sunday);

        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Weekly: WeeklyRadio.IsChecked = true; break;
            case RecurrenceFrequency.Monthly: MonthlyRadio.IsChecked = true; break;
            case RecurrenceFrequency.Yearly: YearlyRadio.IsChecked = true; break;
            default: DailyRadio.IsChecked = true; break;
        }

        switch (rule.EndType)
        {
            case RecurrenceEndType.AfterOccurrences:
                EndAfterRadio.IsChecked = true;
                EndAfterCountBox.Text = rule.OccurrenceCount.ToString();
                break;
            case RecurrenceEndType.ByDate:
                EndByDateRadio.IsChecked = true;
                EndByDatePicker.SelectedDate = rule.EndDate ?? _anchor.AddMonths(1);
                break;
            default:
                EndNeverRadio.IsChecked = true;
                break;
        }

        if (EndByDatePicker.SelectedDate is null)
            EndByDatePicker.SelectedDate = _anchor.AddMonths(1);

        UpdatePanelVisibility();
    }

    private void Frequency_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            UpdatePanelVisibility();
    }

    private void UpdatePanelVisibility()
    {
        DailyPanel.Visibility = DailyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPanel.Visibility = WeeklyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MonthlyPanel.Visibility = MonthlyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        YearlyPanel.Visibility = YearlyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int ParseIntOrDefault(string text, int fallback) =>
        int.TryParse(text, out var value) && value > 0 ? value : fallback;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var rule = new RecurrenceRule();

        if (WeeklyRadio.IsChecked == true)
        {
            rule.Frequency = RecurrenceFrequency.Weekly;
            rule.Interval = ParseIntOrDefault(WeeklyIntervalBox.Text, 1);
            var days = new List<DayOfWeek>();
            if (MonCheck.IsChecked == true) days.Add(DayOfWeek.Monday);
            if (TueCheck.IsChecked == true) days.Add(DayOfWeek.Tuesday);
            if (WedCheck.IsChecked == true) days.Add(DayOfWeek.Wednesday);
            if (ThuCheck.IsChecked == true) days.Add(DayOfWeek.Thursday);
            if (FriCheck.IsChecked == true) days.Add(DayOfWeek.Friday);
            if (SatCheck.IsChecked == true) days.Add(DayOfWeek.Saturday);
            if (SunCheck.IsChecked == true) days.Add(DayOfWeek.Sunday);
            rule.DaysOfWeek = days;
        }
        else if (MonthlyRadio.IsChecked == true)
        {
            rule.Frequency = RecurrenceFrequency.Monthly;
            rule.Interval = ParseIntOrDefault(MonthlyIntervalBox.Text, 1);
            rule.DayOfMonth = Math.Clamp(ParseIntOrDefault(MonthlyDayBox.Text, _anchor.Day), 1, 31);
        }
        else if (YearlyRadio.IsChecked == true)
        {
            rule.Frequency = RecurrenceFrequency.Yearly;
            rule.Interval = 1;
            rule.Month = YearlyMonthBox.SelectedIndex >= 0 ? YearlyMonthBox.SelectedIndex + 1 : _anchor.Month;
            rule.DayOfMonth = Math.Clamp(ParseIntOrDefault(YearlyDayBox.Text, _anchor.Day), 1, 31);
        }
        else
        {
            rule.Frequency = RecurrenceFrequency.Daily;
            rule.Interval = ParseIntOrDefault(DailyIntervalBox.Text, 1);
        }

        if (EndAfterRadio.IsChecked == true)
        {
            rule.EndType = RecurrenceEndType.AfterOccurrences;
            rule.OccurrenceCount = ParseIntOrDefault(EndAfterCountBox.Text, 10);
        }
        else if (EndByDateRadio.IsChecked == true)
        {
            rule.EndType = RecurrenceEndType.ByDate;
            rule.EndDate = EndByDatePicker.SelectedDate ?? _anchor.AddMonths(1);
        }
        else
        {
            rule.EndType = RecurrenceEndType.Never;
        }

        Result = rule;
        DialogResult = true;
    }

    private void DontRepeatButton_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
