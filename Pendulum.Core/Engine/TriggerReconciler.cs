using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Decides what happens to a trigger once its alert has been accounted for —
/// whether that's a live dismiss, a snooze, or a firing discovered stale at startup.
public static class TriggerReconciler
{
    /// Advances a recurring trigger to its next future occurrence, or marks the
    /// trigger spent (disabled, HasFired) if it's one-shot or its recurrence has
    /// no more occurrences left.
    public static void AdvancePastFiring(TriggerTimer trigger, DateTime now)
    {
        if (trigger.Recurrence is { Frequency: not RecurrenceFrequency.None } rule)
        {
            var next = RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(
                rule, trigger.RecurrenceAnchor, trigger.TriggerAt, now);

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

    /// Re-arms a fired trigger to go off again <paramref name="snoozeMinutes"/> from now.
    /// TimerEngine counts every firing toward Recurrence.OccurrencesSoFar, but a snooze re-fires
    /// the same occurrence rather than starting a new one, so that count is undone here and
    /// "end after N occurrences" isn't exhausted early by snoozing.
    public static void Snooze(TriggerTimer trigger, DateTime now, int snoozeMinutes)
    {
        if (trigger.Recurrence is not null)
            trigger.Recurrence.OccurrencesSoFar = Math.Max(0, trigger.Recurrence.OccurrencesSoFar - 1);

        trigger.TriggerAt = now.AddMinutes(snoozeMinutes);
        trigger.HasFired = false;
    }

    /// Resolves every trigger that came due while the app wasn't running: a recurring one moves
    /// on to its next future occurrence, anything else is marked spent. Returns the names of the
    /// triggers it resolved, for the "missed while Pendulum was closed" notice.
    public static List<string> CatchUpMissed(IEnumerable<TriggerTimer> triggers, DateTime now)
    {
        var missed = new List<string>();

        foreach (var t in triggers)
        {
            var wasRinging = WasRingingWhenClosed(t);
            if (!wasRinging && (!t.Enabled || t.HasFired || t.TriggerAt > now))
                continue;

            if (t.Recurrence is not null)
            {
                // AdvancePastFiring can silently skip past several missed occurrences in one
                // jump (e.g. the app was closed for days over a daily reminder) — count
                // exactly how many were actually skipped instead of assuming just one, so
                // "end after N occurrences" reminders aren't undercounted and over-fire later.
                // One that was ringing had its own occurrence counted when it fired, so only
                // the ones after it are added.
                var next = RecurrenceCalculator.GetNextFutureOccurrenceWithinEndCondition(
                    t.Recurrence, t.RecurrenceAnchor, t.TriggerAt, now);
                var counted = next is not null
                    ? RecurrenceCalculator.CountOccurrencesBetween(t.Recurrence, t.RecurrenceAnchor, t.TriggerAt, next.Value)
                    : 1;
                t.Recurrence.OccurrencesSoFar += wasRinging ? counted - 1 : counted;
            }

            AdvancePastFiring(t, now);
            missed.Add(t.Name);
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
        && t.Recurrence is { Frequency: not RecurrenceFrequency.None } rule
        && !RecurrenceCalculator.IsExhausted(rule);

    /// How long an unanswered alert waits before resolving itself as if dismissed, or null when
    /// it never does: an alert that repeats until dismissed keeps going, and 0 minutes turns
    /// auto-resolve off.
    public static TimeSpan? AutoResolveDelay(AppSettings settings) =>
        !settings.RepeatAlertUntilDismissed && settings.AutoResolveMinutes > 0
            ? TimeSpan.FromMinutes(settings.AutoResolveMinutes)
            : null;
}
