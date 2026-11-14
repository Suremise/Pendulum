// Frozen period-search loops from 18c3697, before the direct-index implementation.
using Pendulum.Core.Engine;
using Pendulum.Core.Resources;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// Computes recurrence occurrences for a TriggerTimer, in the spirit of Outlook's
/// "recurring appointment" pattern: a fixed anchor date/time plus a repeat rule.
internal static class LegacyRecurrenceCalculator
{
    private const int MaxIterations = 2000;

    /// The next scheduled occurrence strictly after <paramref name="after"/>, following
    /// <paramref name="rule"/> anchored at <paramref name="anchor"/>. Returns null if the
    /// rule has no frequency or the calculation can't find one within a safety bound.
    public static DateTime? GetNextOccurrence(RecurrenceRule rule, DateTime anchor, DateTime after)
    {
        if (rule.Frequency == RecurrenceFrequency.None)
            return null;

        int interval = Math.Max(1, rule.Interval);

        return rule.Frequency switch
        {
            RecurrenceFrequency.Daily => NextDaily(anchor, after, interval),
            RecurrenceFrequency.Weekly => NextWeekly(anchor, after, interval, rule.DaysOfWeek),
            RecurrenceFrequency.Monthly => NextMonthly(rule, anchor, after, interval),
            RecurrenceFrequency.Yearly => NextYearly(rule, anchor, after, interval),
            _ => null
        };
    }

    /// The next occurrence that also satisfies the rule's end condition (never / after N
    /// occurrences / by date), skipping forward past any occurrences already missed so the
    /// result is guaranteed to be in the future relative to <paramref name="now"/>.
    public static DateTime? GetNextFutureOccurrenceWithinEndCondition(
        RecurrenceRule rule, DateTime anchor, DateTime searchFrom, DateTime now)
    {
        if (IsExhausted(rule))
            return null;

        var candidate = searchFrom;
        for (int i = 0; i < MaxIterations; i++)
        {
            var next = GetNextOccurrence(rule, anchor, candidate);
            if (next is null)
                return null;

            if (rule.EndType == RecurrenceEndType.ByDate && rule.EndDate is { } endDate && next.Value.Date > endDate.Date)
                return null;

            if (next.Value > now)
                return next.Value;

            candidate = next.Value;
        }

        return null;
    }

    public static int CountOccurrencesBetween(RecurrenceRule rule, DateTime anchor, DateTime from, DateTime to)
    {
        var count = 1;
        var candidate = from;
        for (int i = 0; i < MaxIterations; i++)
        {
            var next = GetNextOccurrence(rule, anchor, candidate);
            if (next is null || next.Value >= to) break;
            count++;
            candidate = next.Value;
        }
        return count;
    }

    private static bool IsExhausted(RecurrenceRule rule) =>
        rule.EndType == RecurrenceEndType.AfterOccurrences && rule.OccurrencesSoFar >= rule.OccurrenceCount;
    private static int DayInMonth(RecurrenceRule rule, int year, int month) => RecurrenceCalculator.DayInMonth(rule, year, month);
    private static DateTime NextDaily(DateTime anchor, DateTime after, int interval)
    {
        if (after < anchor)
            return anchor;

        var days = (after.Date - anchor.Date).Days;
        var stepsPassed = days / interval;
        var next = anchor.AddDays((double)stepsPassed * interval);
        while (next <= after)
            next = next.AddDays(interval);
        return next;
    }

    private static DateTime? NextWeekly(DateTime anchor, DateTime after, int interval, List<DayOfWeek> daysOfWeek)
    {
        var days = daysOfWeek.Count > 0 ? daysOfWeek : new List<DayOfWeek> { anchor.DayOfWeek };
        var anchorWeekStart = anchor.Date.AddDays(-(int)anchor.DayOfWeek);
        var timeOfDay = anchor.TimeOfDay;

        for (int week = 0; week < MaxIterations; week++)
        {
            var weekStart = anchorWeekStart.AddDays(week * interval * 7);
            DateTime? best = null;
            foreach (var dow in days)
            {
                var candidate = weekStart.AddDays((int)dow).Add(timeOfDay);
                if (candidate > after && (best is null || candidate < best))
                    best = candidate;
            }
            if (best is not null)
                return best;
        }

        return null;
    }

    private static DateTime NextMonthly(RecurrenceRule rule, DateTime anchor, DateTime after, int interval)
    {
        var start = new DateTime(anchor.Year, anchor.Month, 1).Add(anchor.TimeOfDay);
        for (int i = 0; i < MaxIterations; i++)
        {
            var monthDate = start.AddMonths(i * interval);
            var day = DayInMonth(rule, monthDate.Year, monthDate.Month);
            var candidate = new DateTime(monthDate.Year, monthDate.Month, day).Add(anchor.TimeOfDay);
            if (candidate > after)
                return candidate;
        }
        return after;
    }

    private static DateTime NextYearly(RecurrenceRule rule, DateTime anchor, DateTime after, int interval)
    {
        var month = Math.Clamp(rule.Month, 1, 12);
        for (int i = 0; i < MaxIterations; i++)
        {
            var year = anchor.Year + i * interval;
            var day = DayInMonth(rule, year, month);
            var candidate = new DateTime(year, month, day).Add(anchor.TimeOfDay);
            if (candidate > after)
                return candidate;
        }
        return after;
    }
}
