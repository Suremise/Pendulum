using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Decides when a reminder is due and how firing, dismissing, snoozing or catching up
/// changes its state. The engine only polls and dispatches those decisions.
public static class TriggerReconciler
{
    /// Whether a reminder's time has come: by the real instant for a span from now (a snooze,
    /// "in 20 min": <see cref="TriggerTimer.DueUtc"/>), by the clock for any other. The same rule the
    /// engine fires by, so the startup catch-up, import, restore, re-enabling and editing never
    /// count as gone a time the engine would still ring for, across a clock or zone change.
    public static bool HasPassed(TriggerTimer trigger, DateTime now, DateTime utcNow) =>
        trigger.DueUtc is { } dueUtc ? utcNow >= dueUtc : trigger.TriggerAt <= now;

    /// An armed reminder whose time has come, by the same rule used for catch-up and editing.
    public static bool IsDue(TriggerTimer trigger, DateTime now, DateTime utcNow) =>
        trigger.Enabled && !trigger.HasFired && HasPassed(trigger, now, utcNow);

    /// Records one firing accepted by IsDue. A snooze gives this occurrence back until it
    /// fires again. The engine owns polling/dispatch and delegates this state change here.
    public static void MarkFired(TriggerTimer trigger)
    {
        if (trigger.RepeatRule is { } rule) rule.OccurrencesSoFar++;
        trigger.HasFired = true;
    }

    /// The UTC instant of <paramref name="now"/>, when the caller didn't give one: the clock's
    /// own reckoning for DateTime.Now.
    private static DateTime UtcOf(DateTime now, DateTime? utcNow) =>
        utcNow ?? WallClock.ToUtc(now, TimeZoneInfo.Local);

    /// Advances a recurring trigger to its next future occurrence, or marks the
    /// trigger spent (disabled, HasFired) if it's one-shot or its recurrence has
    /// no more occurrences left. A snoozed one moves on from the occurrence it was snoozed from,
    /// not from the snooze's time, which across the clocks going back can read earlier.
    public static void AdvancePastFiring(TriggerTimer trigger, DateTime now)
    {
        if (trigger.RepeatRule is { } rule)
        {
            var next = RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(
                rule, trigger.RecurrenceAnchor, trigger.ScheduleFrom, now);

            if (next is not null)
            {
                trigger.TriggerAt = next.Value;
                trigger.HasFired = false;
                return;
            }
        }

        trigger.HasFired = true;
        trigger.Enabled = false;
    }

    /// What turning a reminder back on did.
    public enum EnableOutcome
    {
        /// Enabled, at the time it had (still ahead).
        Enabled,
        /// A repeating reminder whose time had gone by while it was off: enabled at its next time.
        MovedOn,
        /// A repeating reminder whose series ended while it was off: marked spent, not enabled.
        SeriesEnded,
        /// A one-off whose time has gone by: left off and unchanged; it needs a new time.
        PastOneOff,
    }

    /// Turns a disabled reminder back on without ringing it for a time that went by while it was
    /// off. A repeating one moves to its next future occurrence first (the ones it was off for
    /// aren't counted: they never happened), so the engine never sees it enabled and due. A
    /// one-off in the past stays as it was, for the user to give it a new time.
    public static EnableOutcome Enable(TriggerTimer trigger, DateTime now, DateTime? utcNow = null)
    {
        if (!HasPassed(trigger, now, UtcOf(now, utcNow)))
        {
            trigger.Enabled = true;
            return EnableOutcome.Enabled;
        }

        if (trigger.RepeatRule is not { } rule)
            return EnableOutcome.PastOneOff;

        var next = RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(
            rule, trigger.RecurrenceAnchor, trigger.ScheduleFrom, now);
        if (next is null)
        {
            trigger.HasFired = true;
            return EnableOutcome.SeriesEnded;
        }

        // The time first, so the reminder is never enabled while it's due.
        trigger.TriggerAt = next.Value;
        trigger.Enabled = true;
        return EnableOutcome.MovedOn;
    }

    /// What saving the edit window sets: the series' anchor, and when the reminder next goes off
    /// (null when nothing is left to fire). <paramref name="shown"/> is the date and time the
    /// window loaded (<see cref="RecurrenceCalculator.ScheduledOccurrence"/>), <paramref name="edited"/>
    /// what it holds now.
    ///
    /// When the user changed neither the time nor the repeat, the series stays as it was: its anchor
    /// keeps its time of day, and a pending snooze still goes off. Otherwise the edited time is the
    /// new start, and the first firing follows the pattern from it.
    public static (DateTime Anchor, DateTime? Next) ScheduleAfterEdit(
        TriggerTimer before, RecurrenceRule? rule, bool ruleChanged, DateTime shown, DateTime edited, DateTime now,
        DateTime? utcNow = null)
    {
        // This parameter is the proposed pattern, not necessarily the reminder's saved one.
        rule = rule?.Frequency == RecurrenceFrequency.None ? null : rule;
        if (!ruleChanged && edited == shown)
        {
            // Time and repeat untouched, still to come (by its instant, for a span from now):
            // it keeps its time, and with it a pending snooze or "in 20 min", repeating or not.
            var anchor = rule is not null ? before.RecurrenceAnchor : edited;
            if (!before.HasFired && !HasPassed(before, now, UtcOf(now, utcNow)))
                return (anchor, before.TriggerAt);
            if (rule is not null)
                return (anchor, RecurrenceCalculator.FirstOccurrence(rule, anchor, now));
        }

        return (edited, rule is not null
            ? RecurrenceCalculator.FirstOccurrence(rule, edited, now)
            : edited > now ? edited : null);
    }

    /// Re-arms a fired trigger to go off again <paramref name="snoozeMinutes"/> from now.
    /// MarkFired counts every firing toward the repeat rule, but a snooze re-fires
    /// the same occurrence rather than starting a new one, so that count is undone here and
    /// "end after N occurrences" isn't exhausted early by snoozing. The snooze is real minutes, so
    /// it's right across the clocks going back or forward (see <see cref="WallClock"/>).
    public static void Snooze(TriggerTimer trigger, DateTime now, int snoozeMinutes, TimeZoneInfo? zone = null)
    {
        if (trigger.RepeatRule is { } rule)
            rule.OccurrencesSoFar = Math.Max(0, rule.OccurrencesSoFar - 1);

        // The occurrence this snooze is of: the time it rang, or for a snooze of a snooze, the one
        // that first rang. A series moves on from it, not from the snooze's time.
        var occurrence = trigger.RepeatRule is not null
            ? trigger.ScheduleFrom
            : (DateTime?)null;
        var (wall, utc) = WallClock.AddRealTime(now, TimeSpan.FromMinutes(snoozeMinutes), zone);
        trigger.SetDue(wall, utc, occurrence);
        trigger.HasFired = false;
    }

    /// Whether an open alert still speaks for its reminder: it went off at
    /// <paramref name="firedAt"/> and nothing has moved it since. Editing while the alert is open
    /// gives it a new time, or makes it unfired, and then the old alert's buttons must not act.
    public static bool StillRinging(TriggerTimer trigger, DateTime firedAt) =>
        trigger.HasFired && trigger.TriggerAt == firedAt;

    /// The alert's Snooze (and its auto-resolve's twin, Dismiss, below): acts only while the
    /// reminder is still the one that rang at <paramref name="firedAt"/>. Returns whether it did.
    public static bool Snooze(TriggerTimer trigger, DateTime firedAt, DateTime now, int snoozeMinutes, TimeZoneInfo? zone = null)
    {
        if (!StillRinging(trigger, firedAt))
            return false;

        Snooze(trigger, now, snoozeMinutes, zone);
        return true;
    }

    /// The alert's Dismiss, and auto-resolve: moves the reminder that rang at
    /// <paramref name="firedAt"/> on to its next occurrence, or marks it spent. The engine counted
    /// the firing itself; any occurrences that went by while the alert was open (a PC asleep for
    /// days, say) are counted here too, as the startup catch-up does, so "end after N" ends on time.
    /// Does nothing if the reminder was edited since it rang. Returns whether it acted.
    public static bool Dismiss(TriggerTimer trigger, DateTime firedAt, DateTime now)
    {
        if (!StillRinging(trigger, firedAt))
            return false;

        CreditSkippedOccurrences(trigger, now, alreadyCounted: true);
        AdvancePastFiring(trigger, now);
        return true;
    }

    /// Resolves every trigger that came due while the app wasn't running: a recurring one moves
    /// on to its next future occurrence, anything else is marked spent. Returns the reminders
    /// resolved, for a startup notice or an import/restore count. Before they reach the engine,
    /// old reminders move on instead of all ringing at once.
    public static List<TriggerTimer> CatchUpMissed(IEnumerable<TriggerTimer> triggers, DateTime now, DateTime? utcNow = null)
    {
        var missed = new List<TriggerTimer>();
        var utc = UtcOf(now, utcNow);

        foreach (var t in triggers)
        {
            var wasRinging = WasRingingWhenClosed(t);
            if (!wasRinging && !IsDue(t, now, utc))
                continue;

            CreditSkippedOccurrences(t, now, alreadyCounted: wasRinging);
            AdvancePastFiring(t, now);
            missed.Add(t);
        }

        return missed;
    }

    /// A repeating reminder whose alert was still open when Pendulum closed (Exit, a crash, a
    /// Windows restart): the engine marked it fired, and only Dismiss or Snooze would have moved it
    /// on. It's the only state that is both enabled and fired, since a spent reminder is also
    /// disabled. Left alone it would never fire again, so it's caught up like a missed one, unless
    /// that firing was its last. A one-off that was ringing stays as it is: fired.
    private static bool WasRingingWhenClosed(TriggerTimer t) =>
        t.Enabled
        && t.HasFired
        && t.RepeatRule is { } rule
        && !RecurrenceCalculator.IsExhausted(rule);

    /// Credits skipped occurrences before advancing. A live/open-at-close firing already has
    /// its own credit; a reminder missed while closed does not. With no future occurrence,
    /// only that due occurrence is credited, preserving the end-condition behavior.
    private static void CreditSkippedOccurrences(TriggerTimer trigger, DateTime now, bool alreadyCounted)
    {
        if (trigger.RepeatRule is not { } rule) return;
        var from = trigger.ScheduleFrom;
        var next = RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(rule, trigger.RecurrenceAnchor, from, now);
        var counted = next is not null
            ? RecurrenceCalculator.CountOccurrencesBetween(rule, trigger.RecurrenceAnchor, from, next.Value)
            : 1;
        rule.OccurrencesSoFar += counted - (alreadyCounted ? 1 : 0);
    }
}
