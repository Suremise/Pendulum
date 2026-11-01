using System.Globalization;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

public class RecurrenceCalculatorTests
{
    private static RecurrenceRule Daily(int interval = 1) => new() { Frequency = RecurrenceFrequency.Daily, Interval = interval };

    private static RecurrenceRule Weekly(int interval = 1, params DayOfWeek[] days) =>
        new() { Frequency = RecurrenceFrequency.Weekly, Interval = interval, DaysOfWeek = days.ToList() };

    private static RecurrenceRule Monthly(int dayOfMonth, int interval = 1) =>
        new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, DayOfMonth = dayOfMonth };

    private static RecurrenceRule Yearly(int month, int dayOfMonth, int interval = 1) =>
        new() { Frequency = RecurrenceFrequency.Yearly, Interval = interval, Month = month, DayOfMonth = dayOfMonth };

    private static DateTime D(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0);

    // ---- Daily ----

    [Fact]
    public void Daily_goes_to_the_next_day_at_the_anchor_time()
    {
        Assert.Equal(D(2026, 2, 1), RecurrenceCalculator.GetNextOccurrence(Daily(), D(2026, 1, 30), D(2026, 1, 31)));
    }

    [Fact]
    public void Daily_crosses_the_end_of_the_year()
    {
        Assert.Equal(D(2027, 1, 1), RecurrenceCalculator.GetNextOccurrence(Daily(), D(2026, 12, 1), D(2026, 12, 31)));
    }

    [Fact]
    public void Every_three_days_counts_from_the_anchor()
    {
        // 1st, 4th, 7th...
        Assert.Equal(D(2026, 1, 7), RecurrenceCalculator.GetNextOccurrence(Daily(3), D(2026, 1, 1), D(2026, 1, 5, 10)));
        Assert.Equal(D(2026, 1, 4), RecurrenceCalculator.GetNextOccurrence(Daily(3), D(2026, 1, 1), D(2026, 1, 1)));
    }

    [Fact]
    public void Before_the_anchor_the_next_occurrence_is_the_anchor()
    {
        Assert.Equal(D(2026, 1, 10), RecurrenceCalculator.GetNextOccurrence(Daily(), D(2026, 1, 10), D(2026, 1, 1)));
    }

    [Fact]
    public void Later_the_same_day_than_the_anchor_time_moves_to_tomorrow()
    {
        Assert.Equal(D(2026, 1, 11), RecurrenceCalculator.GetNextOccurrence(Daily(), D(2026, 1, 10), D(2026, 1, 10, 9, 1)));
        Assert.Equal(D(2026, 1, 11), RecurrenceCalculator.GetNextOccurrence(Daily(), D(2026, 1, 10), D(2026, 1, 10, 23, 59)));
    }

    [Fact]
    public void An_interval_below_one_is_treated_as_one()
    {
        Assert.Equal(D(2026, 1, 2), RecurrenceCalculator.GetNextOccurrence(Daily(0), D(2026, 1, 1), D(2026, 1, 1)));
    }

    [Fact]
    public void No_frequency_has_no_next_occurrence()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None };

        Assert.Null(RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 1, 1), D(2026, 1, 1)));
    }

    // ---- Weekly ----

    [Fact]
    public void Weekly_on_several_days_takes_the_next_one_in_the_week()
    {
        var rule = Weekly(1, DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
        var anchor = D(2026, 6, 1); // a Monday

        Assert.Equal(D(2026, 6, 3), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 6, 1)));
        Assert.Equal(D(2026, 6, 5), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 6, 3)));
        Assert.Equal(D(2026, 6, 8), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 6, 5)));
    }

    [Fact]
    public void Weekly_with_no_days_chosen_repeats_on_the_anchor_weekday()
    {
        Assert.Equal(D(2026, 6, 10), RecurrenceCalculator.GetNextOccurrence(Weekly(), D(2026, 6, 3), D(2026, 6, 3)));
    }

    [Fact]
    public void Every_other_week_skips_a_week()
    {
        var rule = Weekly(2, DayOfWeek.Monday);

        Assert.Equal(D(2026, 6, 15), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 6, 1), D(2026, 6, 1)));
        Assert.Equal(D(2026, 6, 15), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 6, 1), D(2026, 6, 8)));
    }

    [Fact]
    public void Weekly_crosses_a_month_end()
    {
        var rule = Weekly(1, DayOfWeek.Friday);

        Assert.Equal(D(2026, 7, 3), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 6, 26), D(2026, 6, 26)));
    }

    // ---- Monthly, and the 29th to 31st ----

    [Fact]
    public void Monthly_on_the_31st_lands_on_the_last_day_of_shorter_months()
    {
        var rule = Monthly(31);
        var anchor = D(2026, 1, 31, 8);

        Assert.Equal(D(2026, 2, 28, 8), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 1, 31, 8)));
        Assert.Equal(D(2026, 3, 31, 8), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 2, 28, 8)));
        Assert.Equal(D(2026, 4, 30, 8), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 3, 31, 8)));
        Assert.Equal(D(2026, 5, 31, 8), RecurrenceCalculator.GetNextOccurrence(rule, anchor, D(2026, 4, 30, 8)));
    }

    [Fact]
    public void Shorter_months_dont_pull_later_months_earlier()
    {
        // After February's 28th, March is still the 31st, not the 28th: February 2026 to January 2027.
        var occurrences = Walk(Monthly(31), D(2026, 1, 31, 8), D(2026, 1, 31, 8), 12);

        Assert.Equal(new[] { 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31, 31 }, occurrences.Select(o => o.Day));
        Assert.Equal(new DateTime(2027, 1, 31, 8, 0, 0), occurrences[^1]);
    }

    [Fact]
    public void Monthly_on_the_29th_and_30th_in_february()
    {
        Assert.Equal(D(2027, 2, 28), RecurrenceCalculator.GetNextOccurrence(Monthly(29), D(2027, 1, 29), D(2027, 1, 29)));
        Assert.Equal(D(2027, 2, 28), RecurrenceCalculator.GetNextOccurrence(Monthly(30), D(2027, 1, 30), D(2027, 1, 30)));
        Assert.Equal(D(2028, 2, 29), RecurrenceCalculator.GetNextOccurrence(Monthly(29), D(2028, 1, 29), D(2028, 1, 29)));
        Assert.Equal(D(2028, 2, 29), RecurrenceCalculator.GetNextOccurrence(Monthly(31), D(2028, 1, 31), D(2028, 1, 31)));
    }

    [Fact]
    public void Monthly_crosses_the_end_of_the_year()
    {
        Assert.Equal(D(2027, 1, 15), RecurrenceCalculator.GetNextOccurrence(Monthly(15), D(2026, 1, 15), D(2026, 12, 15)));
    }

    [Fact]
    public void Every_three_months_counts_from_the_anchor_month()
    {
        Assert.Equal(D(2026, 4, 15), RecurrenceCalculator.GetNextOccurrence(Monthly(15, 3), D(2026, 1, 15), D(2026, 1, 15)));
        Assert.Equal(D(2026, 10, 15), RecurrenceCalculator.GetNextOccurrence(Monthly(15, 3), D(2026, 1, 15), D(2026, 8, 1)));
    }

    [Fact]
    public void Monthly_later_in_the_anchor_month_than_its_day()
    {
        // Anchored on the 10th, repeating on the 20th: this month's 20th comes first.
        Assert.Equal(D(2026, 1, 20), RecurrenceCalculator.GetNextOccurrence(Monthly(20), D(2026, 1, 10), D(2026, 1, 10)));
    }

    // ---- Yearly and leap years ----

    [Fact]
    public void Yearly_on_the_29th_of_february_falls_on_the_28th_until_the_next_leap_year()
    {
        var rule = Yearly(2, 29);
        var anchor = D(2024, 2, 29, 10);

        var occurrences = Walk(rule, anchor, anchor, 5);

        Assert.Equal(
            new[] { D(2025, 2, 28, 10), D(2026, 2, 28, 10), D(2027, 2, 28, 10), D(2028, 2, 29, 10), D(2029, 2, 28, 10) },
            occurrences);
    }

    [Fact]
    public void Every_other_year()
    {
        Assert.Equal(D(2028, 7, 4), RecurrenceCalculator.GetNextOccurrence(Yearly(7, 4, 2), D(2026, 7, 4), D(2026, 7, 4)));
    }

    [Fact]
    public void Yearly_on_the_31st_of_a_30_day_month_uses_the_30th()
    {
        Assert.Equal(D(2027, 4, 30), RecurrenceCalculator.GetNextOccurrence(Yearly(4, 31), D(2026, 4, 30), D(2026, 4, 30)));
    }

    // ---- Daylight saving (wall-clock times: UK clocks go forward 29 Mar 2026 and back 25 Oct 2026) ----

    [Fact]
    public void Daily_keeps_its_wall_clock_time_across_the_spring_change()
    {
        // 01:30 doesn't exist on 29 March in the UK: the reminder keeps its 01:30 (TimerEngine fires
        // it at the first tick at or after that, 02:00 local), and the next day's is 01:30 again.
        var occurrences = Walk(Daily(), D(2026, 3, 27, 1, 30), D(2026, 3, 27, 1, 30), 3);

        Assert.Equal(new[] { D(2026, 3, 28, 1, 30), D(2026, 3, 29, 1, 30), D(2026, 3, 30, 1, 30) }, occurrences);
    }

    [Fact]
    public void Daily_keeps_its_wall_clock_time_across_the_autumn_change_and_comes_once()
    {
        // 01:30 happens twice on 25 October; the occurrence after it is the next day's, not the repeat.
        var occurrences = Walk(Daily(), D(2026, 10, 24, 1, 30), D(2026, 10, 24, 1, 30), 2);

        Assert.Equal(new[] { D(2026, 10, 25, 1, 30), D(2026, 10, 26, 1, 30) }, occurrences);
    }

    [Fact]
    public void Weekly_keeps_its_time_across_both_changes()
    {
        var rule = Weekly(1, DayOfWeek.Sunday);

        Assert.Equal(D(2026, 3, 29, 9), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 3, 22, 9), D(2026, 3, 22, 9)));
        Assert.Equal(D(2026, 10, 25, 9), RecurrenceCalculator.GetNextOccurrence(rule, D(2026, 3, 22, 9), D(2026, 10, 18, 9)));
    }

    // ---- End conditions ----

    [Fact]
    public void Ending_after_a_count_is_exhausted_once_that_many_have_fired()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.AfterOccurrences;
        rule.OccurrenceCount = 3;

        rule.OccurrencesSoFar = 2;
        Assert.False(RecurrenceCalculator.IsExhausted(rule));
        Assert.NotNull(RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1), D(2026, 1, 2), D(2026, 1, 2)));

        rule.OccurrencesSoFar = 3;
        Assert.True(RecurrenceCalculator.IsExhausted(rule));
        Assert.Null(RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1), D(2026, 1, 3), D(2026, 1, 3)));
    }

    [Fact]
    public void Ending_by_a_date_includes_that_date_and_nothing_after()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 1, 10);

        Assert.Equal(D(2026, 1, 10), RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1), D(2026, 1, 9), D(2026, 1, 9)));
        Assert.Null(RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1), D(2026, 1, 10), D(2026, 1, 10)));
    }

    [Fact]
    public void Ending_by_a_date_ignores_the_time_of_day()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 1, 10, 0, 0, 0);

        Assert.Equal(D(2026, 1, 10, 23), RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1, 23), D(2026, 1, 9, 23), D(2026, 1, 9, 23)));
    }

    [Fact]
    public void The_next_future_occurrence_skips_everything_already_past()
    {
        // Last fired 1 January; it's now 5 January, 12:00: the next is 6 January, 09:00.
        Assert.Equal(D(2026, 1, 6), RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(Daily(), D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 5, 12)));
    }

    [Fact]
    public void A_date_that_ends_before_now_has_no_future_occurrence()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 1, 3);

        Assert.Null(RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 5)));
    }

    [Fact]
    public void Counting_the_occurrences_between_two_times()
    {
        // From the 1st (counted) up to, not including, the 5th: 1st, 2nd, 3rd, 4th.
        Assert.Equal(4, RecurrenceCalculator.CountOccurrencesBetween(Daily(), D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 5)));
        Assert.Equal(1, RecurrenceCalculator.CountOccurrencesBetween(Daily(), D(2026, 1, 1), D(2026, 1, 1), D(2026, 1, 2)));
        Assert.Equal(2, RecurrenceCalculator.CountOccurrencesBetween(Weekly(1, DayOfWeek.Monday, DayOfWeek.Thursday), D(2026, 6, 1), D(2026, 6, 1), D(2026, 6, 8)));
    }

    // ---- The calendar's view of a reminder ----

    [Fact]
    public void The_calendar_shows_only_the_occurrences_a_count_leaves()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.AfterOccurrences;
        rule.OccurrenceCount = 5;
        rule.OccurrencesSoFar = 2;
        var trigger = new TriggerTimer { TriggerAt = D(2026, 1, 3), RecurrenceAnchor = D(2026, 1, 1), Recurrence = rule };

        var shown = RecurrenceCalculator.GetOccurrencesInRange(trigger, D(2026, 1, 1, 0), D(2026, 1, 31, 23)).ToList();

        Assert.Equal(new[] { D(2026, 1, 3), D(2026, 1, 4), D(2026, 1, 5) }, shown);
    }

    [Fact]
    public void The_calendar_shows_nothing_past_an_end_date()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 1, 4);
        var trigger = new TriggerTimer { TriggerAt = D(2026, 1, 2), RecurrenceAnchor = D(2026, 1, 1), Recurrence = rule };

        var shown = RecurrenceCalculator.GetOccurrencesInRange(trigger, D(2026, 1, 1, 0), D(2026, 1, 31, 23)).ToList();

        Assert.Equal(new[] { D(2026, 1, 2), D(2026, 1, 3), D(2026, 1, 4) }, shown);
    }

    [Fact]
    public void The_calendar_shows_a_one_off_or_spent_reminder_once_on_its_day()
    {
        var oneOff = new TriggerTimer { TriggerAt = D(2026, 1, 15), RecurrenceAnchor = D(2026, 1, 15) };
        var spent = new TriggerTimer { TriggerAt = D(2026, 1, 15), RecurrenceAnchor = D(2026, 1, 1), Recurrence = Daily(), HasFired = true };

        Assert.Equal(new[] { D(2026, 1, 15) }, RecurrenceCalculator.GetOccurrencesInRange(oneOff, D(2026, 1, 1, 0), D(2026, 1, 31, 23)));
        Assert.Equal(new[] { D(2026, 1, 15) }, RecurrenceCalculator.GetOccurrencesInRange(spent, D(2026, 1, 1, 0), D(2026, 1, 31, 23)));
        Assert.Empty(RecurrenceCalculator.GetOccurrencesInRange(oneOff, D(2026, 2, 1, 0), D(2026, 2, 28, 23)));
    }

    [Fact]
    public void The_calendar_shows_a_month_of_a_weekly_reminder()
    {
        var trigger = new TriggerTimer { TriggerAt = D(2026, 6, 1), RecurrenceAnchor = D(2026, 6, 1), Recurrence = Weekly(1, DayOfWeek.Monday) };

        var shown = RecurrenceCalculator.GetOccurrencesInRange(trigger, D(2026, 6, 1, 0), D(2026, 6, 30, 23)).ToList();

        Assert.Equal(new[] { D(2026, 6, 1), D(2026, 6, 8), D(2026, 6, 15), D(2026, 6, 22), D(2026, 6, 29) }, shown);
    }

    // ---- Descriptions ----

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, "Daily")]
    [InlineData(RecurrenceFrequency.Daily, 3, "Every 3 days")]
    [InlineData(RecurrenceFrequency.Monthly, 1, "Monthly on day 31")]
    [InlineData(RecurrenceFrequency.Monthly, 2, "Every 2 months on day 31")]
    [InlineData(RecurrenceFrequency.Yearly, 1, "Annually on March 31")]
    [InlineData(RecurrenceFrequency.None, 1, "Does not repeat")]
    public void Describes_the_rule(RecurrenceFrequency frequency, int interval, string expected)
    {
        var rule = new RecurrenceRule { Frequency = frequency, Interval = interval, DayOfMonth = 31, Month = 3 };

        // Describe names months in the user's language: pin one, so the test reads the same on any PC.
        Assert.Equal(expected, InCulture("en-GB", () => RecurrenceCalculator.Describe(rule)));
    }

    [Fact]
    public void Describes_months_in_the_users_language()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly, Month = 3, DayOfMonth = 31 };

        Assert.Equal("Annually on mars 31", InCulture("fr-FR", () => RecurrenceCalculator.Describe(rule)));
    }

    [Fact]
    public void Describes_weekly_days_and_end_conditions()
    {
        var rule = Weekly(2, DayOfWeek.Friday, DayOfWeek.Monday);
        rule.EndType = RecurrenceEndType.AfterOccurrences;
        rule.OccurrenceCount = 6;

        Assert.Equal("Every 2 weeks on Mon, Fri, 6 times", InCulture("en-GB", () => RecurrenceCalculator.Describe(rule)));
    }

    [Fact]
    public void Describes_an_end_date()
    {
        var rule = Daily();
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 7, 4);

        Assert.Equal("Daily, until 04 Jul 2026", InCulture("en-GB", () => RecurrenceCalculator.Describe(rule)));
    }

    private static T InCulture<T>(string name, Func<T> body)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        try { return body(); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous; }
    }

    /// The next <paramref name="count"/> occurrences after <paramref name="from"/>.
    private static List<DateTime> Walk(RecurrenceRule rule, DateTime anchor, DateTime from, int count)
    {
        var result = new List<DateTime>();
        var after = from;
        for (var i = 0; i < count; i++)
        {
            after = RecurrenceCalculator.GetNextOccurrence(rule, anchor, after) ?? throw new InvalidOperationException("no next occurrence");
            result.Add(after);
        }
        return result;
    }
}
