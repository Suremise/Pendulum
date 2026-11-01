using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// The engine on a clock the test sets, polled by the test: nothing waits for real time.
public class TimerEngineTests
{
    private DateTime _now = new(2026, 6, 3, 9, 0, 0);
    private readonly List<TriggerTimer> _fired = new();

    private TimerEngine Engine(params TriggerTimer[] triggers)
    {
        var engine = new TimerEngine(() => _now, polls: false);
        engine.TriggerFired += _fired.Add;
        engine.SetTriggers(triggers);
        engine.Start(null);
        return engine;
    }

    [Fact]
    public void Fires_a_reminder_once_its_time_comes()
    {
        var t = new TriggerTimer { Name = "tea", TriggerAt = _now.AddMinutes(1) };
        using var engine = Engine(t);

        engine.CheckDue();
        Assert.Empty(_fired);

        _now = _now.AddMinutes(1);
        engine.CheckDue();

        Assert.Equal(new[] { t }, _fired);
        Assert.True(t.HasFired);
    }

    [Fact]
    public void Fires_each_reminder_only_once()
    {
        var t = new TriggerTimer { TriggerAt = _now };
        using var engine = Engine(t);

        engine.CheckDue();
        engine.CheckDue();
        _now = _now.AddHours(1);
        engine.CheckDue();

        Assert.Single(_fired);
    }

    [Fact]
    public void Doesnt_fire_disabled_or_spent_reminders()
    {
        var disabled = new TriggerTimer { TriggerAt = _now, Enabled = false };
        var spent = new TriggerTimer { TriggerAt = _now, HasFired = true };
        using var engine = Engine(disabled, spent);

        engine.CheckDue();

        Assert.Empty(_fired);
    }

    [Fact]
    public void Counts_each_firing_of_a_recurring_reminder()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 4 };
        var t = new TriggerTimer { TriggerAt = _now, RecurrenceAnchor = _now, Recurrence = rule };
        using var engine = Engine(t);

        engine.CheckDue();

        Assert.Equal(5, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_dismissed_daily_reminder_fires_again_the_next_day_and_not_before()
    {
        var t = new TriggerTimer { TriggerAt = _now, RecurrenceAnchor = _now, Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };
        using var engine = Engine(t);

        engine.CheckDue();
        TriggerReconciler.AdvancePastFiring(t, _now.AddSeconds(30));

        _now = new DateTime(2026, 6, 4, 8, 59, 59);
        engine.CheckDue();
        Assert.Single(_fired);

        _now = new DateTime(2026, 6, 4, 9, 0, 0);
        engine.CheckDue();
        Assert.Equal(2, _fired.Count);
    }

    [Fact]
    public void The_repeated_hour_when_the_clocks_go_back_doesnt_fire_a_daily_reminder_twice()
    {
        // UK clocks go back at 02:00 on 25 October 2026, so 01:30 comes round twice.
        _now = new DateTime(2026, 10, 25, 1, 30, 0);
        var t = new TriggerTimer { TriggerAt = _now, RecurrenceAnchor = new DateTime(2026, 10, 1, 1, 30, 0), Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } };
        using var engine = Engine(t);

        engine.CheckDue();
        TriggerReconciler.AdvancePastFiring(t, new DateTime(2026, 10, 25, 1, 31, 0));
        _now = new DateTime(2026, 10, 25, 1, 59, 59);
        engine.CheckDue();
        _now = new DateTime(2026, 10, 25, 1, 30, 0); // the clock reads 01:30 again
        engine.CheckDue();

        Assert.Single(_fired);
        Assert.Equal(new DateTime(2026, 10, 26, 1, 30, 0), t.TriggerAt);
    }

    [Fact]
    public void A_time_skipped_when_the_clocks_go_forward_fires_at_the_first_tick_after()
    {
        // 01:30 doesn't exist on 29 March 2026 in the UK: the clock goes from 00:59:59 to 02:00:00.
        var t = new TriggerTimer { TriggerAt = new DateTime(2026, 3, 29, 1, 30, 0) };
        _now = new DateTime(2026, 3, 29, 0, 59, 59);
        using var engine = Engine(t);

        engine.CheckDue();
        Assert.Empty(_fired);

        _now = new DateTime(2026, 3, 29, 2, 0, 0);
        engine.CheckDue();
        Assert.Single(_fired);
    }

    [Fact]
    public void A_failing_handler_is_reported_and_the_other_reminders_still_fire()
    {
        var first = new TriggerTimer { Name = "first", TriggerAt = _now };
        var second = new TriggerTimer { Name = "second", TriggerAt = _now };
        var failures = new List<Exception>();
        using var engine = new TimerEngine(() => _now, polls: false);
        engine.TriggerFired += t => { if (t == first) throw new InvalidOperationException("boom"); _fired.Add(t); };
        engine.TickFailed += failures.Add;
        engine.SetTriggers(new[] { first, second });
        engine.Start(null);

        engine.CheckDue();

        Assert.Equal(new[] { second }, _fired);
        Assert.Single(failures);
        Assert.True(first.HasFired);
    }

    [Fact]
    public void Replacing_the_list_stops_reminders_that_were_removed()
    {
        var removed = new TriggerTimer { TriggerAt = _now.AddMinutes(1) };
        var kept = new TriggerTimer { TriggerAt = _now.AddMinutes(1) };
        using var engine = Engine(removed, kept);

        engine.SetTriggers(new[] { kept });
        _now = _now.AddMinutes(1);
        engine.CheckDue();

        Assert.Equal(new[] { kept }, _fired);
    }
}
