using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// Editing a snoozed repeating reminder keeps its pattern's time: the edit window shows the
/// scheduled occurrence, and an untouched time keeps the series as it was. Fixed clocks, no waits.
public class EditSnoozedReminderTests
{
    private static readonly DateTime Anchor = new(2026, 5, 1, 9, 0, 0);
    private static readonly DateTime Snoozed = new(2026, 6, 3, 9, 10, 37);
    private static readonly DateTime Now = new(2026, 6, 3, 9, 5, 0);

    /// A daily 09:00 stand-up that rang at 09:00 today and was snoozed: TriggerAt is the snooze time.
    private static TriggerTimer SnoozedStandUp(RecurrenceRule? rule = null) => new()
    {
        Name = "stand-up",
        TriggerAt = Snoozed,
        RecurrenceAnchor = Anchor,
        Recurrence = rule ?? new RecurrenceRule { Frequency = RecurrenceFrequency.Daily },
    };

    // ---- What the edit window shows ----

    [Fact]
    public void A_snoozed_daily_reminder_shows_its_scheduled_time_not_the_snooze_time()
    {
        Assert.Equal(new DateTime(2026, 6, 3, 9, 0, 0), RecurrenceCalculator.ScheduledOccurrence(SnoozedStandUp()));
    }

    [Fact]
    public void A_reminder_on_its_pattern_shows_its_own_time()
    {
        var t = SnoozedStandUp();
        t.TriggerAt = new DateTime(2026, 6, 4, 9, 0, 0);

        Assert.Equal(t.TriggerAt, RecurrenceCalculator.ScheduledOccurrence(t));
    }

    [Fact]
    public void A_one_off_shows_its_own_time()
    {
        var t = new TriggerTimer { TriggerAt = Snoozed, RecurrenceAnchor = Anchor };

        Assert.Equal(Snoozed, RecurrenceCalculator.ScheduledOccurrence(t));
    }

    [Fact]
    public void A_snoozed_weekly_reminder_shows_its_day_and_time()
    {
        // Mondays and Wednesdays at 09:00; Wednesday 3 June's was snoozed.
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Wednesday } };

        Assert.Equal(new DateTime(2026, 6, 3, 9, 0, 0), RecurrenceCalculator.ScheduledOccurrence(SnoozedStandUp(rule)));
    }

    [Fact]
    public void A_snooze_past_midnight_shows_the_occurrence_it_came_from()
    {
        // A daily 23:55 reminder snoozed to 00:05 the next day.
        var t = new TriggerTimer
        {
            TriggerAt = new DateTime(2026, 6, 4, 0, 5, 0),
            RecurrenceAnchor = new DateTime(2026, 5, 1, 23, 55, 0),
            Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily },
        };

        Assert.Equal(new DateTime(2026, 6, 3, 23, 55, 0), RecurrenceCalculator.ScheduledOccurrence(t));
    }

    [Fact]
    public void A_snoozed_monthly_last_friday_shows_that_friday()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, MonthDayRule = MonthDayRule.RelativeDay, WeekOfMonth = WeekOfMonth.Last, RelativeDay = RelativeDay.Friday };
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 26, 9, 15, 0), RecurrenceAnchor = Anchor, Recurrence = rule };

        Assert.Equal(new DateTime(2026, 6, 26, 9, 0, 0), RecurrenceCalculator.ScheduledOccurrence(t));
    }

    // ---- What Save keeps ----

    [Fact]
    public void Renaming_a_snoozed_reminder_keeps_its_daily_time_and_its_snooze()
    {
        // The card's scenario: snoozed once, then only renamed.
        var t = SnoozedStandUp();
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, t.Recurrence, ruleChanged: false, shown, edited: shown, Now);

        Assert.Equal(Anchor, anchor);
        Assert.Equal(Snoozed, next);

        // After the snooze rings and is dismissed, the series goes on at 09:00, not 09:10:37.
        t.RecurrenceAnchor = anchor;
        t.TriggerAt = next!.Value;
        TriggerReconciler.AdvancePastFiring(t, Snoozed.AddMinutes(1));
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void Without_this_the_series_moved_to_the_snooze_time()
    {
        // What Save did before: the window showed TriggerAt, and Save made it the anchor.
        var t = SnoozedStandUp();
        t.RecurrenceAnchor = Snoozed;
        t.TriggerAt = Snoozed;

        TriggerReconciler.AdvancePastFiring(t, Snoozed.AddMinutes(1));

        Assert.Equal(new DateTime(2026, 6, 4, 9, 10, 37), t.TriggerAt);
    }

    [Fact]
    public void Editing_after_the_snooze_has_rung_keeps_the_pattern_and_moves_on()
    {
        var t = SnoozedStandUp();
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);
        var later = new DateTime(2026, 6, 3, 12, 0, 0);

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, t.Recurrence, ruleChanged: false, shown, shown, later);

        Assert.Equal(Anchor, anchor);
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), next);
    }

    [Fact]
    public void Changing_the_time_makes_it_the_new_start()
    {
        var t = SnoozedStandUp();
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);
        var edited = new DateTime(2026, 6, 4, 8, 30, 0);

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, t.Recurrence, ruleChanged: false, shown, edited, Now);

        Assert.Equal(edited, anchor);
        Assert.Equal(edited, next);
    }

    [Fact]
    public void Changing_the_repeat_starts_it_from_the_shown_time_which_keeps_the_patterns_time_of_day()
    {
        var t = SnoozedStandUp();
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);
        var weekdays = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
        };

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, weekdays, ruleChanged: true, shown, shown, Now);

        Assert.Equal(new DateTime(2026, 6, 3, 9, 0, 0), anchor);
        Assert.Equal(TimeSpan.FromHours(9), next!.Value.TimeOfDay);
    }

    [Fact]
    public void An_untouched_reminder_whose_series_has_ended_has_nothing_left()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 3, OccurrencesSoFar = 3 };
        var t = SnoozedStandUp(rule);
        t.TriggerAt = new DateTime(2026, 6, 1, 9, 0, 0);
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var (_, next) = TriggerReconciler.ScheduleAfterEdit(t, rule, ruleChanged: false, shown, shown, Now);

        Assert.Null(next);
    }

    [Fact]
    public void A_one_off_still_uses_the_time_in_the_window()
    {
        var at = new DateTime(2026, 6, 5, 15, 0, 0);
        var t = new TriggerTimer { TriggerAt = at, RecurrenceAnchor = at };

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, at, at, Now);

        Assert.Equal(at, anchor);
        Assert.Equal(at, next);
        Assert.Null(TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, at, Now.AddHours(-1), Now).Next);
    }
}
