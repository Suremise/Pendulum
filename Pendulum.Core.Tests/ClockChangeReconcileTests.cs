using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// A snooze across the UK's autumn fold (clocks back at 02:00 BST, 25 Oct 2026) through every
/// path that decides whether its time has come: startup catch-up (import and restore use it),
/// re-enabling, editing, and moving the series on afterwards. Fixed clocks and zones, no waits.
public class ClockChangeReconcileTests
{
    private static readonly TimeZoneInfo Uk = Zone("GMT Standard Time", "Europe/London");
    private static readonly TimeZoneInfo NewYork = Zone("Eastern Standard Time", "America/New_York");

    private static TimeZoneInfo Zone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new InvalidOperationException("Time zone missing on this machine.");
    }

    private static DateTime Utc(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);
    private static DateTime Wall(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0);

    /// A daily 01:50 reminder: on 25 Oct its 01:50 is BST (00:50 UTC), it rings, and is snoozed
    /// ten real minutes at 00:55 UTC (01:55 BST), to 01:05 UTC, which the clock reads 01:05 GMT.
    private static TriggerTimer SnoozedAcrossTheFold(RecurrenceRule? rule = null)
    {
        var t = new TriggerTimer
        {
            Name = "night meds",
            TriggerAt = Wall(25, 1, 50),
            RecurrenceAnchor = Wall(1, 1, 50),
            Recurrence = rule ?? new RecurrenceRule { Frequency = RecurrenceFrequency.Daily },
            HasFired = true,
        };
        TriggerReconciler.Snooze(t, Utc(25, 0, 55), 10, Uk);
        return t;
    }

    private static TriggerTimer Reload(TriggerTimer t) =>
        JsonSerializer.Deserialize<TriggerTimer>(JsonSerializer.Serialize(t))!;

    private static List<TriggerTimer> Engine(DateTime wall, DateTime utc, TriggerTimer t)
    {
        var fired = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => wall, polls: false, () => utc);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(new[] { t });
        engine.Start(null);
        engine.CheckDue();
        return fired;
    }

    // ---- 1. Startup, import, re-enable and edit go by the instant ----

    [Fact]
    public void A_restart_in_the_repeated_hour_keeps_a_pending_snooze_and_it_rings_on_time()
    {
        var t = Reload(SnoozedAcrossTheFold());

        // Restarted at 00:56 UTC: the clock reads 01:56 BST, past 01:05 on the wall.
        var missed = TriggerReconciler.CatchUpMissedTriggers(new[] { t }, Wall(25, 1, 56), Utc(25, 0, 56));

        Assert.Empty(missed);
        Assert.False(t.HasFired);
        Assert.Equal(Wall(25, 1, 5), t.TriggerAt);
        Assert.Equal(Utc(25, 1, 5), t.DueUtc);
        Assert.Empty(Engine(Wall(25, 1, 56), Utc(25, 0, 56), t));
        Assert.Single(Engine(Wall(25, 1, 5), Utc(25, 1, 5), t));
    }

    [Fact]
    public void A_pending_snoozed_one_off_isnt_marked_spent_at_a_restart_in_the_repeated_hour()
    {
        var t = new TriggerTimer { Name = "call back", TriggerAt = Wall(25, 1, 50), RecurrenceAnchor = Wall(25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(25, 0, 55), 10, Uk);
        t = Reload(t);

        var missed = TriggerReconciler.CatchUpMissedTriggers(new[] { t }, Wall(25, 1, 56), Utc(25, 0, 56));

        Assert.Empty(missed);
        Assert.True(t.Enabled);
        Assert.False(t.HasFired);
    }

    [Fact]
    public void A_snooze_that_has_really_passed_is_caught_up()
    {
        var t = Reload(SnoozedAcrossTheFold());

        var missed = TriggerReconciler.CatchUpMissedTriggers(new[] { t }, Wall(25, 1, 10), Utc(25, 1, 10));

        Assert.Single(missed);
        Assert.Equal(Wall(26, 1, 50), t.TriggerAt);
    }

    [Fact]
    public void After_a_zone_change_a_pending_snooze_keeps_its_instant()
    {
        // The laptop crosses to New York before restarting: its clock reads 20:56 on the 24th.
        var t = Reload(SnoozedAcrossTheFold());
        var nyWall = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(Utc(25, 0, 56), NewYork), DateTimeKind.Unspecified);

        var missed = TriggerReconciler.CatchUpMissedTriggers(new[] { t }, nyWall, Utc(25, 0, 56));

        Assert.Empty(missed);
        Assert.Empty(Engine(nyWall, Utc(25, 0, 56), t));
        Assert.Single(Engine(nyWall.AddMinutes(9), Utc(25, 1, 5), t));
    }

    [Fact]
    public void Re_enabling_a_pending_snooze_in_the_repeated_hour_keeps_it()
    {
        var t = SnoozedAcrossTheFold();
        t.Enabled = false;

        var outcome = TriggerReconciler.Enable(t, Wall(25, 1, 56), Utc(25, 0, 56));

        Assert.Equal(TriggerReconciler.EnableOutcome.Enabled, outcome);
        Assert.Equal(Wall(25, 1, 5), t.TriggerAt);
        Assert.Equal(Utc(25, 1, 5), t.DueUtc);
    }

    [Fact]
    public void Saving_an_untouched_edit_in_the_repeated_hour_keeps_the_snooze()
    {
        var t = SnoozedAcrossTheFold();
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var (anchor, next) = TriggerReconciler.ScheduleAfterEdit(t, t.Recurrence, ruleChanged: false, shown, shown, Wall(25, 1, 56), Utc(25, 0, 56));

        Assert.Equal(Wall(25, 1, 50), shown); // the window shows the occurrence, not the snooze
        Assert.Equal(Wall(1, 1, 50), anchor);
        Assert.Equal(Wall(25, 1, 5), next);
    }

    /// Saves the edit window's result the way the window does: the anchor, then the next time,
    /// then fired and enabled.
    private static void ApplyEdit(TriggerTimer t, (DateTime Anchor, DateTime? Next) result)
    {
        t.RecurrenceAnchor = result.Anchor;
        t.TriggerAt = result.Next!.Value;
        t.HasFired = false;
        t.Enabled = true;
    }

    [Fact]
    public void An_untouched_edit_of_an_in_n_minutes_reminder_in_the_repeated_hour_keeps_it_pending()
    {
        // "call back in 10 min" typed at 01:55 BST (00:55Z): due 01:05Z, 01:05 GMT on the clock.
        var parsed = Parsing.QuickAddParser.Parse("call back in 10 min", Utc(25, 0, 55), Uk);
        var t = new TriggerTimer { Name = parsed.Name, TriggerAt = parsed.When!.Value, RecurrenceAnchor = parsed.When.Value };
        t.SetDue(parsed.When.Value, parsed.DueUtc);
        t = Reload(t);
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        // Renamed at 00:56Z, when the clock reads 01:56 BST.
        var result = TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, shown, shown, Wall(25, 1, 56), Utc(25, 0, 56));
        t.Name = "call Sam back";
        ApplyEdit(t, result);

        Assert.Equal(Wall(25, 1, 5), result.Next);
        Assert.Equal(Utc(25, 1, 5), t.DueUtc);
        Assert.Empty(Engine(Wall(25, 1, 56), Utc(25, 0, 56), t));
        Assert.Single(Engine(Wall(25, 1, 5), Utc(25, 1, 5), t));
    }

    [Fact]
    public void An_untouched_edit_of_a_snoozed_one_off_in_the_repeated_hour_keeps_the_snooze()
    {
        var t = new TriggerTimer { Name = "call back", TriggerAt = Wall(25, 1, 50), RecurrenceAnchor = Wall(25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(25, 0, 55), 10, Uk);
        t = Reload(t);
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var result = TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, shown, shown, Wall(25, 1, 56), Utc(25, 0, 56));
        ApplyEdit(t, result);

        Assert.Equal(Wall(25, 1, 5), shown);
        Assert.Equal(Wall(25, 1, 5), result.Next);
        Assert.Equal(Utc(25, 1, 5), t.DueUtc);
        Assert.Single(Engine(Wall(25, 1, 5), Utc(25, 1, 5), t));
    }

    [Fact]
    public void An_untouched_edit_of_a_one_off_whose_time_really_passed_still_has_nothing_left()
    {
        var t = new TriggerTimer { Name = "call back", TriggerAt = Wall(25, 1, 50), RecurrenceAnchor = Wall(25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(25, 0, 55), 10, Uk);
        t.HasFired = true; // it rang at 01:05Z
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var (_, next) = TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, shown, shown, Wall(25, 1, 20), Utc(25, 1, 20));

        Assert.Null(next);
    }

    [Fact]
    public void Changing_a_pending_one_offs_time_sets_it_on_the_clock()
    {
        var parsed = Parsing.QuickAddParser.Parse("call back in 10 min", Utc(25, 0, 55), Uk);
        var t = new TriggerTimer { Name = parsed.Name };
        t.SetDue(parsed.When!.Value, parsed.DueUtc);
        var shown = RecurrenceCalculator.ScheduledOccurrence(t);

        var result = TriggerReconciler.ScheduleAfterEdit(t, null, ruleChanged: false, shown, Wall(25, 9, 0), Wall(25, 1, 56), Utc(25, 0, 56));
        ApplyEdit(t, result);

        Assert.Equal(Wall(25, 9, 0), t.TriggerAt);
        Assert.Null(t.DueUtc);
    }

    // ---- 2. Moving the series on from the occurrence the snooze was of ----

    [Fact]
    public void Ring_snooze_across_the_fold_ring_dismiss_goes_on_to_the_next_day()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 30, OccurrencesSoFar = 24 };
        var t = new TriggerTimer { Name = "night meds", TriggerAt = Wall(25, 1, 50), RecurrenceAnchor = Wall(1, 1, 50), Recurrence = rule };

        // 01:50 BST rings (25th counted: 25).
        Assert.Single(Engine(Wall(25, 1, 50), Utc(25, 0, 50), t));
        Assert.Equal(25, rule.OccurrencesSoFar);
        // Snoozed at 01:55 BST for ten minutes (gives the count back: 24).
        Assert.True(TriggerReconciler.Snooze(t, Wall(25, 1, 50), Utc(25, 0, 55), 10, Uk));
        Assert.Equal(24, rule.OccurrencesSoFar);
        Assert.Equal(Wall(25, 1, 5), t.TriggerAt);
        // Rings again at 01:05 GMT (25).
        Assert.Single(Engine(Wall(25, 1, 5), Utc(25, 1, 5), t));
        Assert.Equal(25, rule.OccurrencesSoFar);

        // Dismissed at 01:06 GMT.
        Assert.True(TriggerReconciler.Dismiss(t, Wall(25, 1, 5), Wall(25, 1, 6)));

        Assert.Equal(Wall(26, 1, 50), t.TriggerAt); // not 01:50 on the 25th again
        Assert.Equal(25, rule.OccurrencesSoFar);
        Assert.Null(t.DueUtc);
        Assert.Null(t.SnoozedOccurrence);
    }

    [Fact]
    public void Without_the_snoozed_occurrence_the_series_repeated_the_same_days_time()
    {
        // What moving on from the snooze's wall time did: 01:05 GMT reads earlier than 01:50 BST.
        var t = new TriggerTimer { TriggerAt = Wall(25, 1, 5), RecurrenceAnchor = Wall(1, 1, 50), Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily }, HasFired = true };

        TriggerReconciler.AdvancePastFiring(t, Wall(25, 1, 6));

        Assert.Equal(Wall(25, 1, 50), t.TriggerAt);
    }

    [Fact]
    public void A_snooze_of_a_snooze_keeps_the_occurrence_it_first_came_from()
    {
        var t = SnoozedAcrossTheFold();
        Assert.Single(Engine(Wall(25, 1, 5), Utc(25, 1, 5), t));

        TriggerReconciler.Snooze(t, Utc(25, 1, 6), 10, Uk);

        Assert.Equal(Wall(25, 1, 50), t.SnoozedOccurrence);
        Assert.Equal(Wall(25, 1, 16), t.TriggerAt);
    }

    [Fact]
    public void Enabling_a_lapsed_snooze_moves_on_from_its_occurrence()
    {
        var t = SnoozedAcrossTheFold();
        t.Enabled = false;

        TriggerReconciler.Enable(t, Wall(25, 1, 20), Utc(25, 1, 20));

        Assert.Equal(Wall(26, 1, 50), t.TriggerAt);
    }

    // ---- 3. Saved state doesn't depend on the order of the file's keys ----

    [Fact]
    public void A_snooze_loads_the_same_whatever_order_its_keys_are_in()
    {
        const string timeFirst = """{ "Name": "x", "TriggerAt": "2026-10-25T01:05:00", "DueUtc": "2026-10-25T01:05:00Z", "SnoozedOccurrence": "2026-10-25T01:50:00", "RecurrenceAnchor": "2026-10-01T01:50:00", "Recurrence": { "Frequency": 1 } }""";
        const string instantFirst = """{ "Name": "x", "DueUtc": "2026-10-25T01:05:00Z", "SnoozedOccurrence": "2026-10-25T01:50:00", "RecurrenceAnchor": "2026-10-01T01:50:00", "Recurrence": { "Frequency": 1 }, "TriggerAt": "2026-10-25T01:05:00" }""";

        foreach (var json in new[] { timeFirst, instantFirst })
        {
            var t = JsonSerializer.Deserialize<TriggerTimer>(json)!;
            Assert.Equal(Utc(25, 1, 5), t.DueUtc);
            Assert.Equal(Wall(25, 1, 50), t.SnoozedOccurrence);
            Assert.Equal(Wall(25, 1, 5), t.TriggerAt);
        }
    }

    [Fact]
    public void After_loading_a_change_to_the_time_still_clears_the_snooze()
    {
        var t = Reload(SnoozedAcrossTheFold());

        t.TriggerAt = Wall(26, 1, 50);

        Assert.Null(t.DueUtc);
        Assert.Null(t.SnoozedOccurrence);
    }

    [Fact]
    public void A_snooze_round_trips_through_a_file()
    {
        var t = Reload(SnoozedAcrossTheFold());

        Assert.Equal(Wall(25, 1, 5), t.TriggerAt);
        Assert.Equal(Utc(25, 1, 5), t.DueUtc);
        Assert.Equal(Wall(25, 1, 50), t.SnoozedOccurrence);
        Assert.Equal(DateTimeKind.Unspecified, t.SnoozedOccurrence!.Value.Kind);
    }
}
