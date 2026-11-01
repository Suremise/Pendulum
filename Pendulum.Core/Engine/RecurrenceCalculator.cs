using System.Globalization;
using Pendulum.Core.Resources;
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
    /// that has fired (HasFired) yields just its TriggerAt, same as a spent one-shot: a recurring
    /// reminder stays HasFired only once it's spent, or while its alert is open (TimerEngine marks
    /// it fired; Dismiss or Snooze, or TriggerReconciler.CatchUpMissed after a restart, moves it
    /// on). This does
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

    /// When a repeating reminder that starts at <paramref name="start"/> first goes off, the way
    /// Outlook does it: at the start itself if it fits the pattern, otherwise at the pattern's first
    /// occurrence after it (start on a Wednesday, repeat on Mondays: the first is the next Monday).
    /// Anything already past <paramref name="now"/> is skipped, and the end condition holds. Null
    /// when nothing is left to fire.
    public static DateTime? FirstOccurrence(RecurrenceRule rule, DateTime start, DateTime now)
    {
        if (rule.Frequency == RecurrenceFrequency.None)
            return start > now ? start : null;

        if (IsExhausted(rule))
            return null;

        var first = FitsPattern(rule, start) ? start : GetNextOccurrence(rule, start, start);
        if (first is null)
            return null;

        if (rule.EndType == RecurrenceEndType.ByDate && rule.EndDate is { } endDate && first.Value.Date > endDate.Date)
            return null;

        return first.Value > now
            ? first.Value
            : GetNextFutureOccurrenceWithinEndCondition(rule, start, first.Value, now);
    }

    /// The occurrence a reminder is scheduled for, for showing and editing it. That's its
    /// TriggerAt, except for a snoozed repeating reminder, whose TriggerAt is the snooze time (now
    /// plus the snooze minutes) and off its pattern: then it's the pattern's latest occurrence at or
    /// before that, so the edit window shows 09:00 for a daily 09:00 reminder snoozed to 09:10:37.
    public static DateTime ScheduledOccurrence(TriggerTimer trigger)
    {
        if (trigger.Recurrence is not { Frequency: not RecurrenceFrequency.None } rule)
            return trigger.TriggerAt;

        // A snooze saved since this was known: it says which occurrence it's of.
        if (trigger.SnoozedOccurrence is { } snoozedFrom)
            return snoozedFrom;

        var anchor = trigger.RecurrenceAnchor;
        var interval = Math.Max(1, rule.Interval);
        var lookBack = rule.Frequency switch
        {
            RecurrenceFrequency.Daily => trigger.TriggerAt.AddDays(-(interval + 1)),
            RecurrenceFrequency.Weekly => trigger.TriggerAt.AddDays(-7 * (interval + 1)),
            RecurrenceFrequency.Monthly => trigger.TriggerAt.AddMonths(-(interval + 1)),
            _ => trigger.TriggerAt.AddYears(-(interval + 1)),
        };
        var after = lookBack > anchor ? lookBack : anchor.AddTicks(-1);

        DateTime? latest = null;
        for (int i = 0; i < MaxIterations; i++)
        {
            var next = GetNextOccurrence(rule, anchor, after);
            if (next is null || next.Value > trigger.TriggerAt)
                break;
            latest = next;
            after = next.Value;
        }

        return latest ?? trigger.TriggerAt;
    }

    /// Whether <paramref name="start"/> is itself one of the rule's occurrences, by day (the time
    /// of day is the start's own, so it always fits).
    private static bool FitsPattern(RecurrenceRule rule, DateTime start)
    {
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Weekly:
                return rule.DaysOfWeek.Count == 0 || rule.DaysOfWeek.Contains(start.DayOfWeek);
            case RecurrenceFrequency.Monthly:
                return start.Day == DayInMonth(rule, start.Year, start.Month);
            case RecurrenceFrequency.Yearly:
                return start.Month == Math.Clamp(rule.Month, 1, 12) && start.Day == DayInMonth(rule, start.Year, start.Month);
            default:
                return true;
        }
    }

    public static bool IsExhausted(RecurrenceRule rule) =>
        rule.EndType == RecurrenceEndType.AfterOccurrences && rule.OccurrencesSoFar >= rule.OccurrenceCount;

    /// The rule in words ("Every 2 weeks on Mon, Fri, 6 times"), from CoreStrings, with month and
    /// day names and the end date in the user's culture.
    public static string Describe(RecurrenceRule rule)
    {
        var culture = CultureInfo.CurrentCulture;
        string Month() => culture.DateTimeFormat.GetMonthName(Math.Clamp(rule.Month, 1, 12));

        string core = rule.Frequency switch
        {
            RecurrenceFrequency.Daily => rule.Interval <= 1
                ? CoreStrings.Repeat_Daily
                : string.Format(culture, CoreStrings.Repeat_EveryNDays, rule.Interval),
            RecurrenceFrequency.Weekly => DescribeWeekly(rule, culture),
            RecurrenceFrequency.Monthly when rule.MonthDayRule == MonthDayRule.RelativeDay => rule.Interval <= 1
                ? string.Format(culture, CoreStrings.Repeat_MonthlyOnRelative, DescribeRelativeDay(rule, culture))
                : string.Format(culture, CoreStrings.Repeat_EveryNMonthsOnRelative, rule.Interval, DescribeRelativeDay(rule, culture)),
            RecurrenceFrequency.Monthly => rule.Interval <= 1
                ? string.Format(culture, CoreStrings.Repeat_MonthlyOnDay, rule.DayOfMonth)
                : string.Format(culture, CoreStrings.Repeat_EveryNMonthsOnDay, rule.Interval, rule.DayOfMonth),
            RecurrenceFrequency.Yearly when rule.MonthDayRule == MonthDayRule.RelativeDay => rule.Interval <= 1
                ? string.Format(culture, CoreStrings.Repeat_AnnuallyOnRelative, DescribeRelativeDay(rule, culture), Month())
                : string.Format(culture, CoreStrings.Repeat_EveryNYearsOnRelative, rule.Interval, DescribeRelativeDay(rule, culture), Month()),
            RecurrenceFrequency.Yearly => rule.Interval <= 1
                ? string.Format(culture, CoreStrings.Repeat_AnnuallyOn, Month(), rule.DayOfMonth)
                : string.Format(culture, CoreStrings.Repeat_EveryNYearsOn, rule.Interval, Month(), rule.DayOfMonth),
            _ => CoreStrings.Repeat_DoesNotRepeat
        };

        return rule.EndType switch
        {
            RecurrenceEndType.AfterOccurrences => string.Format(culture, CoreStrings.Repeat_EndAfterTimes, core, rule.OccurrenceCount),
            RecurrenceEndType.ByDate when rule.EndDate is { } d =>
                string.Format(culture, CoreStrings.Repeat_EndUntil, core, d.ToString(CoreStrings.Format_EndDate, culture)),
            _ => core
        };
    }

    private static string DescribeWeekly(RecurrenceRule rule, CultureInfo culture)
    {
        var prefix = rule.Interval <= 1
            ? CoreStrings.Repeat_Weekly
            : string.Format(culture, CoreStrings.Repeat_EveryNWeeks, rule.Interval);
        if (rule.DaysOfWeek.Count == 0)
            return prefix;

        var days = string.Join(CoreStrings.Repeat_DaySeparator, rule.DaysOfWeek
            .OrderBy(d => (int)d)
            .Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d)));
        return string.Format(culture, CoreStrings.Repeat_OnDays, prefix, days);
    }

    /// The day of <paramref name="month"/> a monthly or yearly rule lands on: its day number,
    /// clamped to the month's length, or its relative day ("the last Friday").
    public static int DayInMonth(RecurrenceRule rule, int year, int month) =>
        rule.MonthDayRule == MonthDayRule.RelativeDay
            ? RelativeDayInMonth(year, month, rule.WeekOfMonth, rule.RelativeDay)
            : Math.Min(Math.Max(1, rule.DayOfMonth), DateTime.DaysInMonth(year, month));

    /// The first to fourth, or the last, day of the given kind in the month: "the second Tuesday",
    /// "the last weekday", "the first weekend day", "the last day".
    public static int RelativeDayInMonth(int year, int month, WeekOfMonth which, RelativeDay kind)
    {
        var matching = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Where(d => Matches(new DateTime(year, month, d).DayOfWeek, kind))
            .ToList();

        return which == WeekOfMonth.Last
            ? matching[^1]
            : matching[Math.Clamp((int)which, 1, 4) - 1];
    }

    private static bool Matches(DayOfWeek day, RelativeDay kind) => kind switch
    {
        RelativeDay.Day => true,
        RelativeDay.Weekday => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        RelativeDay.WeekendDay => day is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => (int)day == (int)kind,
    };

    /// "the last Friday", "the first weekday": the week from CoreStrings, a named day in the user's
    /// culture.
    private static string DescribeRelativeDay(RecurrenceRule rule, CultureInfo culture) =>
        string.Format(culture, CoreStrings.Repeat_RelativeDay, WeekWord(rule.WeekOfMonth), DayKindName(rule.RelativeDay, culture));

    /// "first" to "last", as the repeat window lists them and Describe says them.
    public static string WeekWord(WeekOfMonth week) => week switch
    {
        WeekOfMonth.Second => CoreStrings.Week_Second,
        WeekOfMonth.Third => CoreStrings.Week_Third,
        WeekOfMonth.Fourth => CoreStrings.Week_Fourth,
        WeekOfMonth.Last => CoreStrings.Week_Last,
        _ => CoreStrings.Week_First,
    };

    /// A day's name in the user's culture ("Friday"), or "day", "weekday", "weekend day".
    public static string DayKindName(RelativeDay kind, CultureInfo culture) => kind switch
    {
        RelativeDay.Day => CoreStrings.DayKind_Day,
        RelativeDay.Weekday => CoreStrings.DayKind_Weekday,
        RelativeDay.WeekendDay => CoreStrings.DayKind_WeekendDay,
        var named => culture.DateTimeFormat.GetDayName((DayOfWeek)(int)named),
    };

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
