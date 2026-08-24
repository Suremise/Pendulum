using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Computes recurrence occurrences for a TriggerTimer, in the spirit of Outlook's
/// "recurring appointment" pattern: a fixed anchor date/time plus a repeat rule.
public static class RecurrenceCalculator
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
            RecurrenceFrequency.Monthly => NextMonthly(anchor, after, interval, rule.DayOfMonth),
            RecurrenceFrequency.Yearly => NextYearly(anchor, after, interval, rule.Month, rule.DayOfMonth),
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

    /// Counts how many scheduled occurrences lie in [from, to) — from itself counts as one, plus
    /// every occurrence strictly between from and to (to itself is excluded, since it's the next
    /// still-pending one, not yet consumed). Used when catching up a reminder that was missed for
    /// a while, so OccurrencesSoFar is credited accurately even when several occurrences were
    /// skipped in a single jump rather than just the one immediately due.
    public static int CountOccurrencesBetween(RecurrenceRule rule, DateTime anchor, DateTime from, DateTime to)
    {
        var count = 1;
        var candidate = from;
        for (int i = 0; i < MaxIterations; i++)
        {
            var next = GetNextOccurrence(rule, anchor, candidate);
            if (next is null || next.Value >= to)
                break;

            count++;
            candidate = next.Value;
        }

        return count;
    }

    /// Expands a trigger (one-shot or recurring) into every occurrence date that falls within
    /// [rangeStart, rangeEnd], inclusive — used to populate a calendar month view. A trigger
    /// that has already fired (HasFired) has nothing left scheduled by definition (see
    /// TriggerReconciler/TimerEngine — a recurring reminder only stays HasFired once fully
    /// exhausted), so it just yields its final TriggerAt, same as a spent one-shot. This does
    /// not reconstruct history: TriggerTimer only tracks a count of past occurrences
    /// (OccurrencesSoFar), not their dates, so past months only ever show a trigger's actual
    /// current TriggerAt, not a guessed-at full history.
    public static IEnumerable<DateTime> GetOccurrencesInRange(TriggerTimer trigger, DateTime rangeStart, DateTime rangeEnd)
    {
        if (trigger.Recurrence is null || trigger.HasFired)
        {
            if (trigger.TriggerAt >= rangeStart && trigger.TriggerAt <= rangeEnd)
                yield return trigger.TriggerAt;
            yield break;
        }

        var rule = trigger.Recurrence;
        var occurrence = trigger.TriggerAt;
        var count = rule.OccurrencesSoFar;

        for (int i = 0; i < MaxIterations && occurrence <= rangeEnd; i++)
        {
            // Checked before yielding (and with the same strict ">" as
            // GetNextFutureOccurrenceWithinEndCondition) so this agrees with what the live engine
            // would actually do — previously this yielded the occurrence first and only stopped
            // afterward, showing one phantom occurrence past the recurrence's real end date.
            if (rule.EndType == RecurrenceEndType.ByDate && rule.EndDate is { } endDate && occurrence.Date > endDate.Date)
                yield break;

            if (occurrence >= rangeStart)
                yield return occurrence;

            count++;
            if (rule.EndType == RecurrenceEndType.AfterOccurrences && count >= rule.OccurrenceCount)
                yield break;

            var next = GetNextOccurrence(rule, trigger.RecurrenceAnchor, occurrence);
            if (next is null)
                yield break;

            occurrence = next.Value;
        }
    }

    public static bool IsExhausted(RecurrenceRule rule) =>
        rule.EndType == RecurrenceEndType.AfterOccurrences && rule.OccurrencesSoFar >= rule.OccurrenceCount;

    public static string Describe(RecurrenceRule rule)
    {
        string core = rule.Frequency switch
        {
            RecurrenceFrequency.Daily => rule.Interval <= 1 ? "Daily" : $"Every {rule.Interval} days",
            RecurrenceFrequency.Weekly => DescribeWeekly(rule),
            RecurrenceFrequency.Monthly => rule.Interval <= 1
                ? $"Monthly on day {rule.DayOfMonth}"
                : $"Every {rule.Interval} months on day {rule.DayOfMonth}",
            RecurrenceFrequency.Yearly => rule.Interval <= 1
                ? $"Annually on {new DateTime(2000, Math.Clamp(rule.Month, 1, 12), 1):MMMM} {rule.DayOfMonth}"
                : $"Every {rule.Interval} years on {new DateTime(2000, Math.Clamp(rule.Month, 1, 12), 1):MMMM} {rule.DayOfMonth}",
            _ => "Does not repeat"
        };

        string end = rule.EndType switch
        {
            RecurrenceEndType.AfterOccurrences => $", {rule.OccurrenceCount} times",
            RecurrenceEndType.ByDate when rule.EndDate is { } d => $", until {d:dd MMM yyyy}",
            _ => ""
        };

        return core + end;
    }

    private static string DescribeWeekly(RecurrenceRule rule)
    {
        var prefix = rule.Interval <= 1 ? "Weekly" : $"Every {rule.Interval} weeks";
        if (rule.DaysOfWeek.Count == 0)
            return prefix;

        var days = string.Join(", ", rule.DaysOfWeek
            .OrderBy(d => (int)d)
            .Select(d => d.ToString()[..3]));
        return $"{prefix} on {days}";
    }

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

    private static DateTime NextMonthly(DateTime anchor, DateTime after, int interval, int dayOfMonth)
    {
        var start = new DateTime(anchor.Year, anchor.Month, 1).Add(anchor.TimeOfDay);
        for (int i = 0; i < MaxIterations; i++)
        {
            var monthDate = start.AddMonths(i * interval);
            var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(monthDate.Year, monthDate.Month));
            var candidate = new DateTime(monthDate.Year, monthDate.Month, day).Add(anchor.TimeOfDay);
            if (candidate > after)
                return candidate;
        }
        return after;
    }

    private static DateTime NextYearly(DateTime anchor, DateTime after, int interval, int month, int dayOfMonth)
    {
        month = Math.Clamp(month, 1, 12);
        for (int i = 0; i < MaxIterations; i++)
        {
            var year = anchor.Year + i * interval;
            var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(year, month));
            var candidate = new DateTime(year, month, day).Add(anchor.TimeOfDay);
            if (candidate > after)
                return candidate;
        }
        return after;
    }
}
