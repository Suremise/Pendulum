using System.Diagnostics;
using System.Windows;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

/// The repeat window's relative days ("the last Friday"), built on the tests' UI thread and read
/// back through its own controls. It is never shown: nothing takes the foreground or focus from
/// the user's apps, and there is no keyboard or mouse input.
[Collection(nameof(SettingsScreen))]
public sealed class RecurrenceWindowTests
{
    private static readonly DateTime ThirdWednesday = new(2026, 6, 17, 9, 0, 0);

    private static T WithWindow<T>(DateTime anchor, RecurrenceRule? rule, Func<RecurrenceWindow, T> read) =>
        UiThread.Invoke(() =>
        {
            var window = new RecurrenceWindow(anchor, rule);
            try { return read(window); }
            finally
            {
                window.Close();
                Assert.Empty(Application.Current.Windows);
                Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
            }
        });

    [Fact]
    public void A_new_monthly_rule_offers_the_start_dates_own_place_in_its_month()
    {
        var (which, kind, relative) = WithWindow(ThirdWednesday, new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 17 },
            w => ((string)w.MonthlyWhichBox.SelectedItem, (string)w.MonthlyKindBox.SelectedItem, w.MonthlyByRelativeRadio.IsChecked));

        Assert.Equal("third", which);
        Assert.Equal("Wednesday", kind);
        Assert.False(relative);
    }

    [Fact]
    public void A_start_in_the_last_week_offers_the_last()
    {
        var which = WithWindow(new DateTime(2026, 6, 26, 9, 0, 0), null, w => (string)w.MonthlyWhichBox.SelectedItem);

        Assert.Equal("last", which);
    }

    [Fact]
    public void A_day_number_rule_still_builds_as_a_day_number()
    {
        var rule = WithWindow(ThirdWednesday, new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 17 }, w => w.BuildRule());

        Assert.Equal(MonthDayRule.DayNumber, rule.MonthDayRule);
        Assert.Equal(17, rule.DayOfMonth);
    }

    [Fact]
    public void Choosing_the_last_friday_builds_that_rule()
    {
        var rule = WithWindow(ThirdWednesday, new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly }, w =>
        {
            w.MonthlyByRelativeRadio.IsChecked = true;
            w.MonthlyWhichBox.SelectedItem = "last";
            w.MonthlyKindBox.SelectedItem = "Friday";
            w.MonthlyIntervalBox.Text = "2";
            return w.BuildRule();
        });

        Assert.Equal(RecurrenceFrequency.Monthly, rule.Frequency);
        Assert.Equal(MonthDayRule.RelativeDay, rule.MonthDayRule);
        Assert.Equal(WeekOfMonth.Last, rule.WeekOfMonth);
        Assert.Equal(RelativeDay.Friday, rule.RelativeDay);
        Assert.Equal(2, rule.Interval);
    }

    [Fact]
    public void Choosing_the_fourth_thursday_of_november_builds_that_yearly_rule()
    {
        var rule = WithWindow(ThirdWednesday, new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly }, w =>
        {
            w.YearlyByRelativeRadio.IsChecked = true;
            w.YearlyMonthBox.SelectedIndex = 10;
            w.YearlyWhichBox.SelectedItem = "fourth";
            w.YearlyKindBox.SelectedItem = "Thursday";
            return w.BuildRule();
        });

        Assert.Equal(RecurrenceFrequency.Yearly, rule.Frequency);
        Assert.Equal(11, rule.Month);
        Assert.Equal(MonthDayRule.RelativeDay, rule.MonthDayRule);
        Assert.Equal(WeekOfMonth.Fourth, rule.WeekOfMonth);
        Assert.Equal(RelativeDay.Thursday, rule.RelativeDay);
    }

    [Theory]
    [InlineData(WeekOfMonth.Second, RelativeDay.Weekday)]
    [InlineData(WeekOfMonth.Last, RelativeDay.Day)]
    [InlineData(WeekOfMonth.First, RelativeDay.WeekendDay)]
    [InlineData(WeekOfMonth.Third, RelativeDay.Sunday)]
    public void A_saved_relative_rule_opens_as_it_was_and_builds_back_the_same(WeekOfMonth which, RelativeDay kind)
    {
        var saved = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 3,
            MonthDayRule = MonthDayRule.RelativeDay,
            WeekOfMonth = which,
            RelativeDay = kind,
        };

        var rebuilt = WithWindow(ThirdWednesday, saved, w => w.BuildRule());

        Assert.Equal(MonthDayRule.RelativeDay, rebuilt.MonthDayRule);
        Assert.Equal(which, rebuilt.WeekOfMonth);
        Assert.Equal(kind, rebuilt.RelativeDay);
        Assert.Equal(3, rebuilt.Interval);
    }
}
