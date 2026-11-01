using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// What happens to a reminder once it has fired: dismissed, snoozed, left unanswered, or missed
/// while Pendulum was closed.
public class TriggerReconcilerTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 12, 0, 0);

    private static TriggerTimer OneOff(DateTime at, string name = "one-off") =>
        new() { Name = name, TriggerAt = at, RecurrenceAnchor = at };

    private static TriggerTimer DailyAt(DateTime at, string name = "daily", RecurrenceRule? rule = null) =>
        new() { Name = name, TriggerAt = at, RecurrenceAnchor = at, Recurrence = rule ?? new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };

    // ---- Dismiss ----

    [Fact]
    public void Dismissing_a_one_off_marks_it_spent()
    {
        var t = OneOff(Now.AddMinutes(-1));
        t.HasFired = true;

        TriggerReconciler.AdvancePastFiring(t, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
        Assert.Equal(Now.AddMinutes(-1), t.TriggerAt);
    }

    [Fact]
    public void Dismissing_a_recurring_reminder_moves_it_to_the_next_occurrence()
    {
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0));
        t.HasFired = true;

        TriggerReconciler.AdvancePastFiring(t, Now);

        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void Dismissing_the_last_of_a_counted_series_marks_it_spent()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 3, OccurrencesSoFar = 3 };
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0), rule: rule);
        t.HasFired = true;

        TriggerReconciler.AdvancePastFiring(t, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void Dismissing_past_an_end_date_marks_it_spent()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.ByDate, EndDate = new DateTime(2026, 6, 3) };
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0), rule: rule);

        TriggerReconciler.AdvancePastFiring(t, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void A_recurrence_with_no_frequency_is_treated_as_a_one_off()
    {
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0), rule: new RecurrenceRule { Frequency = RecurrenceFrequency.None });

        TriggerReconciler.AdvancePastFiring(t, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    // ---- Snooze ----

    [Fact]
    public void Snoozing_rearms_the_reminder_that_many_minutes_from_now()
    {
        var t = OneOff(Now.AddMinutes(-1));
        t.HasFired = true;

        TriggerReconciler.Snooze(t, Now, snoozeMinutes: 10);

        Assert.Equal(Now.AddMinutes(10), t.TriggerAt);
        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
    }

    [Fact]
    public void Snoozing_doesnt_use_up_an_occurrence_of_a_counted_series()
    {
        // The engine counted this firing; the snooze gives it back, since it's the same occurrence.
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 3, OccurrencesSoFar = 3 };
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0), rule: rule);
        t.HasFired = true;

        TriggerReconciler.Snooze(t, Now, snoozeMinutes: 5);

        Assert.Equal(2, rule.OccurrencesSoFar);
        Assert.False(RecurrenceCalculator.IsExhausted(rule));
    }

    [Fact]
    public void Snoozing_never_takes_the_count_below_zero()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 0 };
        var t = DailyAt(Now, rule: rule);

        TriggerReconciler.Snooze(t, Now, snoozeMinutes: 5);

        Assert.Equal(0, rule.OccurrencesSoFar);
    }

    [Fact]
    public void Snoozing_then_dismissing_a_recurring_reminder_goes_on_to_its_next_scheduled_time()
    {
        var t = DailyAt(new DateTime(2026, 6, 3, 9, 0, 0));
        t.HasFired = true;

        TriggerReconciler.Snooze(t, new DateTime(2026, 6, 3, 9, 0, 30), snoozeMinutes: 5);
        TriggerReconciler.AdvancePastFiring(t, new DateTime(2026, 6, 3, 9, 6, 0));

        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
    }

    // ---- Auto-resolve of unanswered alerts ----

    [Fact]
    public void An_alert_that_plays_once_resolves_itself_after_the_set_minutes()
    {
        var settings = new AppSettings { RepeatAlertUntilDismissed = false, AutoResolveMinutes = 30 };

        Assert.Equal(TimeSpan.FromMinutes(30), TriggerReconciler.AutoResolveDelay(settings));
    }

    [Fact]
    public void An_alert_that_repeats_until_dismissed_never_resolves_itself()
    {
        var settings = new AppSettings { RepeatAlertUntilDismissed = true, AutoResolveMinutes = 30 };

        Assert.Null(TriggerReconciler.AutoResolveDelay(settings));
    }

    [Fact]
    public void Zero_minutes_turns_auto_resolve_off()
    {
        var settings = new AppSettings { RepeatAlertUntilDismissed = false, AutoResolveMinutes = 0 };

        Assert.Null(TriggerReconciler.AutoResolveDelay(settings));
    }

    [Fact]
    public void Auto_resolve_is_off_for_new_settings_because_alerts_repeat_by_default()
    {
        Assert.Null(TriggerReconciler.AutoResolveDelay(new AppSettings()));
    }

    // ---- Missed while Pendulum was closed ----

    [Fact]
    public void A_one_off_that_came_due_while_closed_is_marked_spent_and_reported()
    {
        var t = OneOff(Now.AddHours(-3), "call the dentist");

        var missed = TriggerReconciler.CatchUpMissed(new[] { t }, Now);

        Assert.Equal(new[] { "call the dentist" }, missed);
        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void A_recurring_reminder_missed_for_days_is_rescheduled_and_counts_each_missed_occurrence()
    {
        // Due daily at 09:00, last due 31 May; it's now 3 June 12:00. Missed: 31 May, 1, 2 and 3 June.
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 5 };
        var t = DailyAt(new DateTime(2026, 5, 31, 9, 0, 0), "stand-up", rule);
        t.RecurrenceAnchor = new DateTime(2026, 5, 20, 9, 0, 0);

        var missed = TriggerReconciler.CatchUpMissed(new[] { t }, Now);

        Assert.Equal(new[] { "stand-up" }, missed);
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
        Assert.Equal(9, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_counted_series_that_ran_out_while_closed_is_marked_spent()
    {
        // 2 of 3 used, then closed for three days: the last one went by, and it's done.
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 3, OccurrencesSoFar = 2 };
        var t = DailyAt(new DateTime(2026, 5, 31, 9, 0, 0), rule: rule);

        TriggerReconciler.CatchUpMissed(new[] { t }, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void A_series_whose_end_date_passed_while_closed_is_marked_spent()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.ByDate, EndDate = new DateTime(2026, 6, 1) };
        var t = DailyAt(new DateTime(2026, 5, 31, 9, 0, 0), rule: rule);

        TriggerReconciler.CatchUpMissed(new[] { t }, Now);

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void Future_disabled_and_spent_reminders_are_left_alone()
    {
        var future = OneOff(Now.AddMinutes(1), "future");
        var disabled = OneOff(Now.AddHours(-1), "disabled");
        disabled.Enabled = false;
        var spent = OneOff(Now.AddHours(-1), "spent");
        spent.HasFired = true;
        spent.Enabled = false;

        var missed = TriggerReconciler.CatchUpMissed(new[] { future, disabled, spent }, Now);

        Assert.Empty(missed);
        Assert.Equal(Now.AddMinutes(1), future.TriggerAt);
        Assert.True(future.Enabled);
        Assert.False(future.HasFired);
        Assert.False(disabled.HasFired);
        Assert.Equal(Now.AddHours(-1), disabled.TriggerAt);
    }

    [Fact]
    public void A_reminder_due_exactly_now_counts_as_missed()
    {
        var t = OneOff(Now);

        Assert.Single(TriggerReconciler.CatchUpMissed(new[] { t }, Now));
    }

    [Fact]
    public void Catching_up_keeps_every_reminder_and_reports_each_missed_one_once()
    {
        var list = new List<TriggerTimer>
        {
            OneOff(Now.AddDays(-1), "a"),
            DailyAt(Now.AddDays(-2), "b"),
            OneOff(Now.AddDays(1), "c"),
        };

        var missed = TriggerReconciler.CatchUpMissed(list, Now);

        Assert.Equal(new[] { "a", "b" }, missed);
        Assert.Equal(3, list.Count);
    }
}
