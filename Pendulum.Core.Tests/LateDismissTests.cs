using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// An alert answered late: Dismiss counts what went by while it was open, and an alert whose
/// reminder was edited since leaves the edit alone. Fixed clocks, polled by hand, no waits.
public class LateDismissTests
{
    private static readonly DateTime Anchor = new(2026, 6, 1, 9, 0, 0);

    private static RecurrenceRule EndAfter(int count, int soFar) =>
        new() { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = count, OccurrencesSoFar = soFar };

    /// Fires the reminder through the engine at its time, as the app does, and returns that time.
    private static DateTime Ring(TriggerTimer t)
    {
        var at = t.TriggerAt;
        using var engine = new TimerEngine(() => at, polls: false);
        engine.SetTriggers(new[] { t });
        engine.Start(null);
        engine.CheckDue();
        Assert.True(t.HasFired);
        return at;
    }

    // ---- "End after N" after a long sleep ----

    [Fact]
    public void Dismissing_after_a_long_sleep_counts_the_days_that_went_by()
    {
        // End after 10: 1-3 June done; 4 June rings (4); the PC sleeps until 9 June 12:00.
        var rule = EndAfter(10, 3);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };
        var firedAt = Ring(t);
        Assert.Equal(4, rule.OccurrencesSoFar);

        Assert.True(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 9, 12, 0, 0)));

        // 5-9 June went by: 9 counted; 10 June is the 10th and last.
        Assert.Equal(9, rule.OccurrencesSoFar);
        Assert.Equal(new DateTime(2026, 6, 10, 9, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
    }

    [Fact]
    public void The_series_then_ends_on_time()
    {
        var rule = EndAfter(10, 3);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };
        TriggerReconciler.Dismiss(t, Ring(t), new DateTime(2026, 6, 9, 12, 0, 0));

        // The 10th rings on 10 June, and that's the end.
        TriggerReconciler.Dismiss(t, Ring(t), new DateTime(2026, 6, 10, 9, 1, 0));

        Assert.Equal(10, rule.OccurrencesSoFar);
        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void Without_the_count_the_series_ran_on_past_its_end()
    {
        // What Dismiss did before: AdvancePastFiring alone.
        var rule = EndAfter(10, 3);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };
        Ring(t);

        TriggerReconciler.AdvancePastFiring(t, new DateTime(2026, 6, 9, 12, 0, 0));

        Assert.Equal(4, rule.OccurrencesSoFar);
        Assert.False(t.HasFired); // six more to go, from 10 June: it ends on the 15th, not the 10th
    }

    [Fact]
    public void A_sleep_past_the_last_occurrence_ends_the_series_at_dismiss()
    {
        var rule = EndAfter(6, 4);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 5, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };

        TriggerReconciler.Dismiss(t, Ring(t), new DateTime(2026, 6, 9, 12, 0, 0));

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void A_dismiss_on_time_counts_nothing_extra()
    {
        var rule = EndAfter(10, 3);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };

        TriggerReconciler.Dismiss(t, Ring(t), new DateTime(2026, 6, 4, 9, 1, 0));

        Assert.Equal(4, rule.OccurrencesSoFar);
        Assert.Equal(new DateTime(2026, 6, 5, 9, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void Dismissing_a_rung_snooze_late_counts_only_the_occurrences_after_it()
    {
        // 4 June's rang, was snoozed to 09:10, rang again, and is dismissed on 6 June 12:00.
        var rule = EndAfter(10, 3);
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = rule };
        var first = Ring(t);
        Assert.True(TriggerReconciler.Snooze(t, first, new DateTime(2026, 6, 4, 9, 5, 0), 5));
        var second = Ring(t);

        TriggerReconciler.Dismiss(t, second, new DateTime(2026, 6, 6, 12, 0, 0));

        // 4 June counted once (snooze gave its count back, the second ring took it again), plus 5 and 6 June.
        Assert.Equal(6, rule.OccurrencesSoFar);
        Assert.Equal(new DateTime(2026, 6, 7, 9, 0, 0), t.TriggerAt);
    }

    // ---- An old alert after an edit ----

    [Fact]
    public void Dismissing_an_old_alert_after_an_edit_leaves_the_edit_alone()
    {
        // "Call mom" rings at 15:00; while its alert is open it's edited to 18:00.
        var t = new TriggerTimer { Name = "Call mom", TriggerAt = new DateTime(2026, 6, 3, 15, 0, 0) };
        var firedAt = Ring(t);
        t.TriggerAt = new DateTime(2026, 6, 3, 18, 0, 0);
        t.HasFired = false;

        Assert.False(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 3, 15, 2, 0)));

        Assert.Equal(new DateTime(2026, 6, 3, 18, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
    }

    [Fact]
    public void Snoozing_an_old_alert_after_an_edit_leaves_the_edit_alone()
    {
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 3, 15, 0, 0) };
        var firedAt = Ring(t);
        t.TriggerAt = new DateTime(2026, 6, 3, 18, 0, 0);
        t.HasFired = false;

        Assert.False(TriggerReconciler.Snooze(t, firedAt, new DateTime(2026, 6, 3, 15, 2, 0), 10));

        Assert.Equal(new DateTime(2026, 6, 3, 18, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void An_edit_that_moved_a_repeating_reminder_on_isnt_moved_again()
    {
        // Edited while ringing, the window moved it to its next time (unfired); Dismiss mustn't skip one more.
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 3, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };
        var firedAt = Ring(t);
        t.TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0);
        t.HasFired = false;

        Assert.False(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 3, 9, 5, 0)));
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void An_alert_for_the_reminder_as_it_rang_still_works()
    {
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 3, 15, 0, 0) };
        var firedAt = Ring(t);
        t.Name = "Call mum"; // renaming doesn't change when it rang

        Assert.True(TriggerReconciler.StillRinging(t, firedAt));
        Assert.True(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 3, 15, 2, 0)));
        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void A_second_dismiss_of_the_same_alert_does_nothing()
    {
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 3, 9, 0, 0), RecurrenceAnchor = Anchor, Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };
        var firedAt = Ring(t);

        Assert.True(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 3, 9, 1, 0)));
        Assert.False(TriggerReconciler.Dismiss(t, firedAt, new DateTime(2026, 6, 3, 9, 1, 0)));
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
    }
}
