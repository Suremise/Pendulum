using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// Compare with the frozen pre-optimisation loops, before and after the implementation changes.
/// These dates stay within the old loops' bounds; separate regressions cover their false ends.
public sealed class RecurrenceEquivalenceTests
{
    [Theory]
    [InlineData(RecurrenceFrequency.None)]
    [InlineData(RecurrenceFrequency.Daily)]
    [InlineData(RecurrenceFrequency.Weekly)]
    [InlineData(RecurrenceFrequency.Monthly)]
    [InlineData(RecurrenceFrequency.Yearly)]
    public void Direct_search_matches_the_original_loops(RecurrenceFrequency frequency)
    {
        var random = new Random(7200 + (int)frequency);
        int[] intervals = [-1, 0, 1, 2, 3, 7, 13];
        for (var sample = 0; sample < 500; sample++)
        {
            var year = random.Next(1950, 2051);
            var month = random.Next(1, 13);
            var anchor = new DateTime(year, month, random.Next(1, DateTime.DaysInMonth(year, month) + 1),
                random.Next(24), random.Next(60), random.Next(60), (DateTimeKind)(sample % 3))
                .AddTicks(random.Next(10_000_000));
            var rule = new RecurrenceRule
            {
                Frequency = frequency,
                Interval = intervals[sample % intervals.Length],
                DaysOfWeek = Enum.GetValues<DayOfWeek>().Where(_ => random.Next(2) == 0).OrderBy(_ => random.Next()).ToList(),
                DayOfMonth = random.Next(0, 34),
                Month = random.Next(0, 14),
                MonthDayRule = (MonthDayRule)(sample % 2),
                WeekOfMonth = (WeekOfMonth)(1 + (sample / 2) % 5),
                RelativeDay = (RelativeDay)((sample / 10) % 10),
                EndType = (RecurrenceEndType)(sample % 3),
                EndDate = sample % 11 == 0 ? null : anchor.AddDays(random.Next(1400)),
                OccurrenceCount = random.Next(1, 12),
                OccurrencesSoFar = random.Next(12),
            };
            var after = sample % 10 == 0 ? anchor : anchor.AddDays(random.Next(-45, 1000)).AddHours(random.Next(-23, 24));
            var now = after.AddDays(random.Next(-20, 400));
            var context = $"sample={sample}, anchor={anchor:o}, after={after:o}, now={now:o}, rule={JsonSerializer.Serialize(rule)}";
            Same(LegacyRecurrenceCalculator.GetNextOccurrence(rule, anchor, after),
                RecurrenceCalculator.GetNextOccurrence(rule, anchor, after), context);
            Same(LegacyRecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, anchor, after, now),
                RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, anchor, after, now), context);
            Assert.True(LegacyRecurrenceCalculator.CountOccurrencesBetween(rule, anchor, after, now)
                == RecurrenceCalculator.CountOccurrencesBetween(rule, anchor, after, now), context);
        }
    }

    private static void Same(DateTime? expected, DateTime? actual, string context) =>
        Assert.True(expected == actual && expected?.Kind == actual?.Kind,
            $"Expected {expected:o} ({expected?.Kind}), got {actual:o} ({actual?.Kind}): {context}");
}
