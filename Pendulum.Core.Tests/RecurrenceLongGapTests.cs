using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

public sealed class RecurrenceLongGapTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.Daily)]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void The_last_representable_date_has_no_later_occurrence(RecurrenceFrequency frequency)
    {
        var rule = new RecurrenceRule { Frequency = frequency, DayOfMonth = 31, Month = 12 };
        Assert.Null(RecurrenceCalculator.GetNextOccurrence(rule, new DateTime(2026, 1, 1, 9, 0, 0), DateTime.MaxValue));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Daily, 1, 1, 2)]
    [InlineData(RecurrenceFrequency.Weekly, 1, 1, 8)]
    [InlineData(RecurrenceFrequency.Monthly, 1, 2, 1)]
    [InlineData(RecurrenceFrequency.Yearly, 2, 1, 1)]
    public void Searches_from_the_first_representable_date_progress(RecurrenceFrequency frequency, int year, int month, int day)
    {
        var rule = new RecurrenceRule { Frequency = frequency };
        Assert.Equal(new DateTime(year, month, day), RecurrenceCalculator.GetNextOccurrence(rule, DateTime.MinValue, DateTime.MinValue));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Daily)]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void An_interval_beyond_the_date_range_ends_without_overflow(RecurrenceFrequency frequency)
    {
        var anchor = new DateTime(2026, 1, 1, 9, 0, 0);
        var rule = new RecurrenceRule { Frequency = frequency, Interval = int.MaxValue };
        Assert.Null(RecurrenceCalculator.GetNextOccurrence(rule, anchor, anchor));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    public void Daily_counting_preserves_half_open_endpoints_and_a_snoozed_start(int interval)
    {
        var anchor = new DateTime(2019, 11, 14, 9, 0, 0);
        var from = anchor.AddMinutes(5); // from counts as one even when off the pattern
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, Interval = interval };
        foreach (var tick in new[] { -1, 0, 1 })
        {
            var to = anchor.AddDays(2800L * interval).AddTicks(tick);
            var expected = 1 + Enumerable.Range(0, 2801).Select(i => anchor.AddDays((long)i * interval))
                .Count(occurrence => occurrence > from && occurrence < to);
            Assert.Equal(expected, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, to));
        }
    }

    [Fact]
    public void A_daily_reminder_missed_for_seven_years_moves_on_and_credits_every_missed_day()
    {
        var anchor = new DateTime(2019, 11, 14, 9, 0, 0);
        var now = new DateTime(2026, 11, 14, 10, 0, 0);
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 3 };
        var reminder = new TriggerTimer { TriggerAt = anchor, RecurrenceAnchor = anchor, Recurrence = rule };
        var next = now.Date.AddDays(1).AddHours(9);

        Assert.Single(TriggerReconciler.CatchUpMissed([reminder], now));
        Assert.Equal(next, reminder.TriggerAt);
        Assert.True(reminder.Enabled);
        Assert.False(reminder.HasFired);
        Assert.Equal(3 + (next - anchor).Days, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_weekly_rule_anchored_fifty_years_ago_still_has_a_next_occurrence()
    {
        var anchor = new DateTime(1976, 11, 14, 9, 0, 0); // Sunday
        var after = new DateTime(2026, 11, 14, 10, 0, 0); // Saturday
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly };
        Assert.Equal(new DateTime(2026, 11, 15, 9, 0, 0), RecurrenceCalculator.GetNextOccurrence(rule, anchor, after));
    }

    [Fact]
    public void A_monthly_rule_more_than_two_thousand_months_old_does_not_return_the_search_time()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 31 };
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0), RecurrenceCalculator.GetNextOccurrence(rule,
            new DateTime(1800, 1, 31, 9, 0, 0), new DateTime(2026, 11, 14, 10, 0, 0)));
    }

    [Fact]
    public void A_yearly_rule_more_than_two_thousand_years_old_does_not_return_the_search_time()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly, Month = 12, DayOfMonth = 31 };
        Assert.Equal(new DateTime(3026, 12, 31, 9, 0, 0), RecurrenceCalculator.GetNextOccurrence(rule,
            new DateTime(1000, 1, 1, 9, 0, 0), new DateTime(3026, 11, 14, 10, 0, 0)));
    }
}
