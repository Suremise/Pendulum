using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using static Pendulum.Core.Engine.TriggerReconciler;

namespace Pendulum.Core.Tests;

/// Turning a disabled reminder back on never rings it for a time that went by while it was off.
/// Fixed clocks, polled by hand, no waits.
public class EnableReminderTests
{
    // A daily 07:00 alarm, turned off on 20 October for a holiday, back on 1 November at 18:00.
    private static readonly DateTime BackOn = new(2026, 11, 1, 18, 0, 0);

    private static TriggerTimer HolidayAlarm(RecurrenceRule? rule = null) => new()
    {
        Name = "alarm",
        TriggerAt = new DateTime(2026, 10, 20, 7, 0, 0),
        RecurrenceAnchor = new DateTime(2026, 9, 1, 7, 0, 0),
        Recurrence = rule ?? new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 49 },
        Enabled = false,
    };

    private static List<TriggerTimer> PollAt(DateTime now, params TriggerTimer[] triggers)
    {
        var fired = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => now, polls: false);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(triggers);
        engine.Start(null);
        engine.CheckDue();
        return fired;
    }

    [Fact]
    public void A_repeating_reminder_back_on_moves_to_its_next_time_and_doesnt_ring_now()
    {
        var t = HolidayAlarm();

        var outcome = Enable(t, BackOn);

        Assert.Equal(EnableOutcome.MovedOn, outcome);
        Assert.True(t.Enabled);
        Assert.Equal(new DateTime(2026, 11, 2, 7, 0, 0), t.TriggerAt);
        Assert.Empty(PollAt(BackOn, t));
        Assert.Single(PollAt(new DateTime(2026, 11, 2, 7, 0, 0), t));
    }

    [Fact]
    public void The_days_it_was_off_arent_counted_as_occurrences()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 60, OccurrencesSoFar = 49 };
        var t = HolidayAlarm(rule);

        Enable(t, BackOn);

        Assert.Equal(49, rule.OccurrencesSoFar);
    }

    [Fact]
    public void Without_this_the_engine_rang_the_stale_time_at_once()
    {
        // What the list toggle did before: Enabled = true, nothing else.
        var t = HolidayAlarm();
        t.Enabled = true;

        Assert.Single(PollAt(BackOn, t));
    }

    [Fact]
    public void A_weekly_reminder_back_on_moves_to_its_next_day_on_the_pattern()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Thursday } };
        var t = HolidayAlarm(rule);

        Enable(t, BackOn); // Sunday 1 November

        Assert.Equal(new DateTime(2026, 11, 2, 7, 0, 0), t.TriggerAt); // Monday
    }

    [Fact]
    public void A_reminder_whose_time_is_still_ahead_is_just_turned_on()
    {
        var at = BackOn.AddHours(2);
        var t = new TriggerTimer { Name = "later", TriggerAt = at, RecurrenceAnchor = at, Enabled = false };

        Assert.Equal(EnableOutcome.Enabled, Enable(t, BackOn));
        Assert.True(t.Enabled);
        Assert.Equal(at, t.TriggerAt);
    }

    [Fact]
    public void A_repeating_reminder_still_ahead_keeps_its_time()
    {
        var t = HolidayAlarm();
        t.TriggerAt = new DateTime(2026, 11, 3, 7, 0, 0);

        Assert.Equal(EnableOutcome.Enabled, Enable(t, BackOn));
        Assert.Equal(new DateTime(2026, 11, 3, 7, 0, 0), t.TriggerAt);
    }

    [Fact]
    public void A_one_off_in_the_past_stays_off_and_unchanged()
    {
        var at = new DateTime(2026, 10, 25, 9, 0, 0);
        var t = new TriggerTimer { Name = "dentist", TriggerAt = at, RecurrenceAnchor = at, Enabled = false };

        Assert.Equal(EnableOutcome.PastOneOff, Enable(t, BackOn));
        Assert.False(t.Enabled);
        Assert.False(t.HasFired);
        Assert.Equal(at, t.TriggerAt);
        Assert.Empty(PollAt(BackOn, t));
    }

    [Fact]
    public void A_series_whose_end_date_passed_while_it_was_off_is_spent_not_enabled()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.ByDate, EndDate = new DateTime(2026, 10, 31) };
        var t = HolidayAlarm(rule);

        Assert.Equal(EnableOutcome.SeriesEnded, Enable(t, BackOn));
        Assert.False(t.Enabled);
        Assert.True(t.HasFired);
        Assert.Empty(PollAt(BackOn, t));
    }

    [Fact]
    public void A_series_whose_count_was_used_up_is_spent_not_enabled()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 49, OccurrencesSoFar = 49 };
        var t = HolidayAlarm(rule);

        Assert.Equal(EnableOutcome.SeriesEnded, Enable(t, BackOn));
        Assert.False(t.Enabled);
    }

    [Fact]
    public void The_time_moves_before_the_reminder_is_enabled()
    {
        // Each change is saved and handed to the engine as it happens: the reminder must never
        // be enabled while its stale time is still set.
        var t = HolidayAlarm();
        var seen = new List<(string? Property, bool Enabled, DateTime At)>();
        t.PropertyChanged += (_, e) => seen.Add((e.PropertyName, t.Enabled, t.TriggerAt));

        Enable(t, BackOn);

        Assert.DoesNotContain(seen, s => s.Enabled && s.At <= BackOn);
        Assert.Contains(seen, s => s.Property == nameof(TriggerTimer.Enabled) && s.Enabled);
    }
}
