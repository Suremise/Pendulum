using System.Globalization;
using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// Outlook's relative monthly and yearly days: "the last Friday", "the second Tuesday",
/// "the first weekday". Expected dates checked against a calendar.
public class RelativeDayTests
{
    private static RecurrenceRule Monthly(WeekOfMonth which, RelativeDay kind, int interval = 1) => new()
    {
        Frequency = RecurrenceFrequency.Monthly,
        Interval = interval,
        MonthDayRule = MonthDayRule.RelativeDay,
        WeekOfMonth = which,
        RelativeDay = kind,
    };

    private static RecurrenceRule Yearly(int month, WeekOfMonth which, RelativeDay kind, int interval = 1) => new()
    {
        Frequency = RecurrenceFrequency.Yearly,
        Interval = interval,
        Month = month,
        MonthDayRule = MonthDayRule.RelativeDay,
        WeekOfMonth = which,
        RelativeDay = kind,
    };

    private static DateTime D(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0);

    private static List<DateTime> Walk(RecurrenceRule rule, DateTime anchor, int count)
    {
        var result = new List<DateTime>();
        var after = anchor;
        for (var i = 0; i < count; i++)
        {
            after = RecurrenceCalculator.GetNextOccurrence(rule, anchor, after)!.Value;
            result.Add(after);
        }
        return result;
    }

    // ---- Which day of the month ----

    [Fact]
    public void The_last_friday_through_a_year_of_four_and_five_friday_months()
    {
        // 2026: January, May, July and October have five Fridays.
        var lastFridays = Enumerable.Range(1, 12)
            .Select(m => RecurrenceCalculator.RelativeDayInMonth(2026, m, WeekOfMonth.Last, RelativeDay.Friday));

        Assert.Equal(new[] { 30, 27, 27, 24, 29, 26, 31, 28, 25, 30, 27, 25 }, lastFridays);
    }

    [Fact]
    public void The_second_tuesday_through_a_year()
    {
        var secondTuesdays = Enumerable.Range(1, 12)
            .Select(m => RecurrenceCalculator.RelativeDayInMonth(2026, m, WeekOfMonth.Second, RelativeDay.Tuesday));

        Assert.Equal(new[] { 13, 10, 10, 14, 12, 9, 14, 11, 8, 13, 10, 8 }, secondTuesdays);
    }

    [Theory]
    [InlineData(2028, 2, WeekOfMonth.Last, RelativeDay.Day, 29)]          // leap year
    [InlineData(2027, 2, WeekOfMonth.Last, RelativeDay.Day, 28)]
    [InlineData(2028, 2, WeekOfMonth.Last, RelativeDay.Tuesday, 29)]      // a leap February with five Tuesdays
    [InlineData(2028, 2, WeekOfMonth.Last, RelativeDay.Monday, 28)]
    [InlineData(2026, 2, WeekOfMonth.Fourth, RelativeDay.Sunday, 22)]     // February 2026 has exactly four of each
    [InlineData(2026, 2, WeekOfMonth.Last, RelativeDay.Sunday, 22)]
    [InlineData(2026, 8, WeekOfMonth.First, RelativeDay.Weekday, 3)]     // 1 August 2026 is a Saturday
    [InlineData(2026, 8, WeekOfMonth.First, RelativeDay.WeekendDay, 1)]
    [InlineData(2026, 8, WeekOfMonth.Second, RelativeDay.WeekendDay, 2)]
    [InlineData(2026, 5, WeekOfMonth.Last, RelativeDay.Weekday, 29)]     // 31 May 2026 is a Sunday
    [InlineData(2026, 6, WeekOfMonth.First, RelativeDay.WeekendDay, 6)]
    [InlineData(2026, 6, WeekOfMonth.Third, RelativeDay.Day, 3)]
    [InlineData(2026, 6, WeekOfMonth.Third, RelativeDay.Wednesday, 17)]
    [InlineData(2026, 11, WeekOfMonth.Fourth, RelativeDay.Thursday, 26)]
    public void Picks_the_right_day(int year, int month, WeekOfMonth which, RelativeDay kind, int day)
    {
        Assert.Equal(day, RecurrenceCalculator.RelativeDayInMonth(year, month, which, kind));
    }

    // ---- Monthly ----

    [Fact]
    public void Monthly_on_the_last_friday()
    {
        var occurrences = Walk(Monthly(WeekOfMonth.Last, RelativeDay.Friday), D(2026, 1, 1), 4);

        Assert.Equal(new[] { D(2026, 1, 30), D(2026, 2, 27), D(2026, 3, 27), D(2026, 4, 24) }, occurrences);
    }

    [Fact]
    public void Every_three_months_on_the_second_tuesday()
    {
        var occurrences = Walk(Monthly(WeekOfMonth.Second, RelativeDay.Tuesday, interval: 3), D(2026, 1, 1), 4);

        Assert.Equal(new[] { D(2026, 1, 13), D(2026, 4, 14), D(2026, 7, 14), D(2026, 10, 13) }, occurrences);
    }

    [Fact]
    public void Monthly_on_the_last_day_lands_on_each_months_last_day_through_february()
    {
        var occurrences = Walk(Monthly(WeekOfMonth.Last, RelativeDay.Day), D(2027, 12, 31), 3);

        Assert.Equal(new[] { D(2028, 1, 31), D(2028, 2, 29), D(2028, 3, 31) }, occurrences);
    }

    [Fact]
    public void An_occurrence_later_the_same_month_than_the_anchor_comes_first()
    {
        // Anchored on 3 June; the last Friday of June is the 26th.
        Assert.Equal(D(2026, 6, 26), RecurrenceCalculator.GetNextOccurrence(Monthly(WeekOfMonth.Last, RelativeDay.Friday), D(2026, 6, 3), D(2026, 6, 3)));
    }

    // ---- Yearly ----

    [Fact]
    public void Yearly_on_the_last_sunday_of_march()
    {
        var occurrences = Walk(Yearly(3, WeekOfMonth.Last, RelativeDay.Sunday), D(2026, 1, 1), 2);

        Assert.Equal(new[] { D(2026, 3, 29), D(2027, 3, 28) }, occurrences);
    }

    [Fact]
    public void Yearly_on_the_fourth_thursday_of_november()
    {
        var occurrences = Walk(Yearly(11, WeekOfMonth.Fourth, RelativeDay.Thursday), D(2026, 1, 1), 2);

        Assert.Equal(new[] { D(2026, 11, 26), D(2027, 11, 25) }, occurrences);
    }

    // ---- The first firing, the end, and the calendar ----

    [Fact]
    public void The_first_firing_snaps_to_the_relative_day()
    {
        var rule = Monthly(WeekOfMonth.Last, RelativeDay.Friday);

        Assert.Equal(D(2026, 6, 26), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 6, 3), D(2026, 6, 1)));
        Assert.Equal(D(2026, 6, 26), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 6, 26), D(2026, 6, 1)));
        Assert.Equal(D(2026, 7, 31), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 6, 27), D(2026, 6, 1)));
    }

    [Fact]
    public void A_yearly_relative_day_fits_only_in_its_month()
    {
        var rule = Yearly(11, WeekOfMonth.Fourth, RelativeDay.Thursday);

        Assert.Equal(D(2026, 11, 26), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 11, 26), D(2026, 6, 1)));
        Assert.Equal(D(2026, 11, 26), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 3, 26), D(2026, 3, 1)));
    }

    [Fact]
    public void An_end_date_stops_a_relative_series()
    {
        var rule = Monthly(WeekOfMonth.Last, RelativeDay.Friday);
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 2, 27);
        var trigger = new TriggerTimer { TriggerAt = D(2026, 1, 30), RecurrenceAnchor = D(2026, 1, 1), Recurrence = rule };

        var shown = RecurrenceCalculator.GetOccurrencesInRange(trigger, D(2026, 1, 1, 0), D(2026, 12, 31, 23)).ToList();

        Assert.Equal(new[] { D(2026, 1, 30), D(2026, 2, 27) }, shown);
    }

    [Fact]
    public void Dismissing_moves_to_the_next_relative_day()
    {
        var trigger = new TriggerTimer { TriggerAt = D(2026, 1, 30), RecurrenceAnchor = D(2026, 1, 1), Recurrence = Monthly(WeekOfMonth.Last, RelativeDay.Friday) };

        TriggerReconciler.AdvancePastFiring(trigger, D(2026, 1, 30, 10));

        Assert.Equal(D(2026, 2, 27), trigger.TriggerAt);
    }

    // ---- Describe ----

    [Theory]
    [InlineData(WeekOfMonth.Last, RelativeDay.Friday, 1, "Monthly on the last Friday")]
    [InlineData(WeekOfMonth.Second, RelativeDay.Tuesday, 3, "Every 3 months on the second Tuesday")]
    [InlineData(WeekOfMonth.First, RelativeDay.Weekday, 1, "Monthly on the first weekday")]
    [InlineData(WeekOfMonth.Third, RelativeDay.WeekendDay, 1, "Monthly on the third weekend day")]
    [InlineData(WeekOfMonth.Fourth, RelativeDay.Day, 1, "Monthly on the fourth day")]
    public void Describes_a_monthly_relative_day(WeekOfMonth which, RelativeDay kind, int interval, string expected)
    {
        Assert.Equal(expected, InCulture("en-GB", () => RecurrenceCalculator.Describe(Monthly(which, kind, interval))));
    }

    [Fact]
    public void Describes_a_yearly_relative_day()
    {
        Assert.Equal("Annually on the last Sunday of March", InCulture("en-GB", () => RecurrenceCalculator.Describe(Yearly(3, WeekOfMonth.Last, RelativeDay.Sunday))));
        Assert.Equal("Every 2 years on the fourth Thursday of November", InCulture("en-GB", () => RecurrenceCalculator.Describe(Yearly(11, WeekOfMonth.Fourth, RelativeDay.Thursday, 2))));
    }

    // ---- Files: old ones load as day numbers, new ones keep the rule ----

    [Fact]
    public void A_rule_saved_before_relative_days_loads_as_a_day_number()
    {
        var rule = JsonSerializer.Deserialize<RecurrenceRule>("""{ "Frequency": 3, "Interval": 1, "DayOfMonth": 31 }""")!;

        Assert.Equal(MonthDayRule.DayNumber, rule.MonthDayRule);
        Assert.Equal(D(2026, 2, 28), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 1, 31), D(2026, 1, 31)));
    }

    [Fact]
    public void A_relative_rule_saves_and_loads_the_same()
    {
        var rule = Yearly(11, WeekOfMonth.Fourth, RelativeDay.Thursday, 2);

        var loaded = JsonSerializer.Deserialize<RecurrenceRule>(JsonSerializer.Serialize(rule))!;

        Assert.Equal(MonthDayRule.RelativeDay, loaded.MonthDayRule);
        Assert.Equal(WeekOfMonth.Fourth, loaded.WeekOfMonth);
        Assert.Equal(RelativeDay.Thursday, loaded.RelativeDay);
        Assert.Equal(JsonSerializer.Serialize(rule), JsonSerializer.Serialize(loaded));
    }

    private static T InCulture<T>(string name, Func<T> body)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        try { return body(); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous; }
    }
}
