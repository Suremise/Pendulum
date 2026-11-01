using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// A reminder whose alert was still open when Pendulum closed (Exit, a crash, a Windows restart):
/// the engine had marked it fired, and nothing moved it on. Fixed clocks, no waits.
public class RingingAtCloseTests
{
    private static readonly DateTime Rang = new(2026, 6, 3, 9, 0, 0);

    /// A daily 09:00 reminder as the engine leaves it once it has fired and is ringing.
    private static TriggerTimer RingingDaily(RecurrenceRule? rule = null)
    {
        rule ??= new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 5 };
        return new TriggerTimer
        {
            Name = "stand-up",
            TriggerAt = Rang,
            RecurrenceAnchor = new DateTime(2026, 5, 29, 9, 0, 0),
            Recurrence = rule,
            Enabled = true,
            HasFired = true,
        };
    }

    [Fact]
    public void A_repeating_reminder_that_was_ringing_comes_back_scheduled_for_its_next_time()
    {
        var t = RingingDaily();

        var missed = TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 3, 12, 0, 0));

        Assert.Equal(new[] { "stand-up" }, missed);
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
        Assert.Equal(5, t.Recurrence!.OccurrencesSoFar); // the ringing one was counted when it fired
    }

    [Fact]
    public void Closed_for_days_it_counts_the_occurrences_skipped_after_the_ringing_one()
    {
        var t = RingingDaily();

        TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 6, 12, 0, 0));

        // Rang on the 3rd (counted then), skipped the 4th, 5th and 6th: next is the 7th.
        Assert.Equal(new DateTime(2026, 6, 7, 9, 0, 0), t.TriggerAt);
        Assert.Equal(8, t.Recurrence!.OccurrencesSoFar);
    }

    [Fact]
    public void A_series_whose_ringing_firing_was_its_last_stays_fired()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 5, OccurrencesSoFar = 5 };
        var t = RingingDaily(rule);

        var missed = TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 3, 12, 0, 0));

        Assert.Empty(missed);
        Assert.True(t.HasFired);
        Assert.Equal(Rang, t.TriggerAt);
        Assert.Equal(5, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_counted_series_with_one_left_after_the_ringing_one_fires_that_one()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 6, OccurrencesSoFar = 5 };
        var t = RingingDaily(rule);

        TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 3, 12, 0, 0));

        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
    }

    [Fact]
    public void A_series_whose_end_date_has_passed_is_marked_spent()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.ByDate, EndDate = new DateTime(2026, 6, 3) };
        var t = RingingDaily(rule);

        TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 5, 12, 0, 0));

        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
        Assert.Equal(0, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_one_off_that_was_ringing_stays_fired()
    {
        var t = new TriggerTimer { Name = "call", TriggerAt = Rang, RecurrenceAnchor = Rang, Enabled = true, HasFired = true };

        var missed = TriggerReconciler.CatchUpMissed(new[] { t }, new DateTime(2026, 6, 3, 12, 0, 0));

        Assert.Empty(missed);
        Assert.True(t.HasFired);
        Assert.True(t.Enabled);
        Assert.Equal(Rang, t.TriggerAt);
    }

    [Fact]
    public void A_spent_or_disabled_repeating_reminder_is_left_alone()
    {
        // Spent: AdvancePastFiring marks it fired and disabled together.
        var spent = RingingDaily();
        spent.Enabled = false;
        // Disabled by the user, not fired.
        var disabled = RingingDaily();
        disabled.HasFired = false;
        disabled.Enabled = false;

        var missed = TriggerReconciler.CatchUpMissed(new[] { spent, disabled }, new DateTime(2026, 6, 10, 12, 0, 0));

        Assert.Empty(missed);
        Assert.Equal(Rang, spent.TriggerAt);
        Assert.True(spent.HasFired);
        Assert.Equal(Rang, disabled.TriggerAt);
        Assert.False(disabled.HasFired);
    }

    [Fact]
    public void Ringing_saved_to_disk_then_a_restart_fires_it_again_at_its_next_time()
    {
        // The engine fires it at 09:00; Pendulum closes with the alert open, saving it as it was.
        var now = Rang;
        var t = new TriggerTimer { Name = "stand-up", TriggerAt = Rang, RecurrenceAnchor = Rang, Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };
        var fired = new List<TriggerTimer>();
        using (var engine = new TimerEngine(() => now, polls: false))
        {
            engine.TriggerFired += fired.Add;
            engine.SetTriggers(new[] { t });
            engine.Start(null);
            engine.CheckDue();
        }
        Assert.Single(fired);
        var saved = JsonSerializer.Serialize(new List<TriggerTimer> { t });

        // The next start, at 12:00: the saved file loads, the catch-up runs, the engine runs on.
        var reloaded = JsonSerializer.Deserialize<List<TriggerTimer>>(saved)!;
        now = new DateTime(2026, 6, 3, 12, 0, 0);
        TriggerReconciler.CatchUpMissed(reloaded, now);
        fired.Clear();
        using var restarted = new TimerEngine(() => now, polls: false);
        restarted.TriggerFired += fired.Add;
        restarted.SetTriggers(reloaded);
        restarted.Start(null);

        now = new DateTime(2026, 6, 4, 8, 59, 59);
        restarted.CheckDue();
        Assert.Empty(fired);

        now = new DateTime(2026, 6, 4, 9, 0, 0);
        restarted.CheckDue();
        Assert.Equal("stand-up", Assert.Single(fired).Name);
    }
}
