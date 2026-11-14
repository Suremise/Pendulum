using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

public sealed class RecurrenceCountingTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.Weekly, false)]
    [InlineData(RecurrenceFrequency.Weekly, true)]
    [InlineData(RecurrenceFrequency.Monthly, false)]
    [InlineData(RecurrenceFrequency.Monthly, true)]
    [InlineData(RecurrenceFrequency.Yearly, false)]
    [InlineData(RecurrenceFrequency.Yearly, true)]
    public void Long_catch_up_credits_every_missed_occurrence_and_exhausts_end_after_N(RecurrenceFrequency frequency, bool ends)
    {
        var (anchor, now, next, expected) = frequency switch
        {
            RecurrenceFrequency.Weekly => (new DateTime(1976, 11, 14, 9, 0, 0), new DateTime(2026, 11, 14, 10, 0, 0),
                new DateTime(2026, 11, 15, 9, 0, 0), 2609),
            RecurrenceFrequency.Monthly => (new DateTime(1800, 1, 31, 9, 0, 0), new DateTime(2026, 11, 14, 10, 0, 0),
                new DateTime(2026, 11, 30, 9, 0, 0), 2722),
            _ => (new DateTime(1000, 2, 28, 9, 0, 0), new DateTime(4026, 3, 1, 10, 0, 0),
                new DateTime(4027, 2, 28, 9, 0, 0), 3027),
        };
        var rule = new RecurrenceRule
        {
            Frequency = frequency,
            DayOfMonth = anchor.Day,
            Month = anchor.Month,
            EndType = ends ? RecurrenceEndType.AfterOccurrences : RecurrenceEndType.Never,
            OccurrenceCount = 2500,
        };
        var reminder = new TriggerTimer { TriggerAt = anchor, RecurrenceAnchor = anchor, Recurrence = rule };

        Assert.Single(TriggerReconciler.CatchUpMissed([reminder], now));

        Assert.Equal(expected, rule.OccurrencesSoFar);
        Assert.Equal(ends, reminder.HasFired);
        Assert.Equal(!ends, reminder.Enabled);
        Assert.Equal(ends ? anchor : next, reminder.TriggerAt);
    }

    [Fact]
    public void A_full_date_range_of_weekdays_counts_without_duplicate_mask_entries_or_tick_overflow()
    {
        var rule = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = [.. Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday],
        };
        var expected = (DateTime.MaxValue.Date - DateTime.MinValue).Days + 1;
        Assert.Equal(expected, RecurrenceCalculator.CountOccurrencesBetween(rule, DateTime.MinValue, DateTime.MinValue, DateTime.MaxValue));
        rule.Interval = int.MaxValue;
        Assert.Equal(6, RecurrenceCalculator.CountOccurrencesBetween(rule, DateTime.MinValue, DateTime.MinValue, DateTime.MaxValue));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void Long_counts_keep_half_open_boundaries_and_an_off_pattern_start(RecurrenceFrequency frequency)
    {
        var anchor = new DateTime(1000, 1, 31, 9, 0, 0).AddTicks(1234);
        var from = anchor.AddMinutes(5);
        var rule = new RecurrenceRule
        {
            Frequency = frequency,
            Interval = 2,
            DaysOfWeek = [DayOfWeek.Friday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Friday],
            Month = 2,
            MonthDayRule = MonthDayRule.RelativeDay,
            WeekOfMonth = WeekOfMonth.Last,
            RelativeDay = RelativeDay.Weekday,
        };
        // A bounded reference walk builds one distant endpoint; the production counter
        // must count the same occurrences directly, including across February/leap years.
        var boundary = from;
        for (var i = 0; i < 3000; i++)
            boundary = RecurrenceCalculator.GetNextOccurrence(rule, anchor, boundary)!.Value;

        Assert.Equal(3000, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, boundary.AddTicks(-1)));
        Assert.Equal(3000, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, boundary));
        Assert.Equal(3001, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, boundary.AddTicks(1)));
        Assert.Equal(1, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, from));
        Assert.Equal(1, RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, from.AddTicks(-1)));
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void Seeded_counts_match_the_original_counter_within_its_bounds(RecurrenceFrequency frequency)
    {
        var random = new Random(9300 + (int)frequency);
        for (var sample = 0; sample < 300; sample++)
        {
            var anchor = new DateTime(random.Next(1600, 6000), random.Next(1, 13), random.Next(1, 29),
                random.Next(24), random.Next(60), random.Next(60)).AddTicks(random.Next(1, 10000));
            var rule = new RecurrenceRule
            {
                Frequency = frequency,
                Interval = random.Next(-1, 14),
                DaysOfWeek = Enum.GetValues<DayOfWeek>().Where(_ => random.Next(2) == 0).ToList(),
                DayOfMonth = random.Next(0, 34),
                Month = random.Next(0, 14),
                MonthDayRule = (MonthDayRule)(sample % 2),
                WeekOfMonth = (WeekOfMonth)(sample % 5 + 1),
                RelativeDay = (RelativeDay)(sample % 10),
                EndType = RecurrenceEndType.AfterOccurrences,
                OccurrenceCount = 2,
            };
            var from = anchor.AddDays(random.Next(-40, 300)).AddMinutes(5);
            var boundary = RecurrenceCalculator.GetNextOccurrence(rule, anchor, from.AddDays(random.Next(1, 900)))!.Value;
            foreach (var tick in new[] { -1, 0, 1 })
            {
                var to = boundary.AddTicks(tick);
                var expected = LegacyRecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, to);
                var actual = RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, from, to);
                Assert.True(expected == actual, $"{frequency}, sample {sample}, tick {tick}: expected {expected}, got {actual}");
            }
        }
    }
}
