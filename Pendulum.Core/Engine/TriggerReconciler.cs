using Pendulum.Core.Models;

namespace Pendulum.Core.Engine;

/// Decides what happens to a trigger once its alert has been accounted for —
/// whether that's a live dismiss or a firing discovered stale at startup.
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
}
