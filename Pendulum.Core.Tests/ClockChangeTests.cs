using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Parsing;

namespace Pendulum.Core.Tests;

/// Spans from now (a snooze, "in 2 hours") across the UK's clock changes in 2026, with the zone
/// given, not the machine's: the clocks go forward at 01:00 GMT on 29 March and back at 02:00 BST
/// on 25 October. And reminder times saved as one kind. Fixed clocks, no waits.
public class ClockChangeTests
{
    private static readonly TimeZoneInfo Uk = Zone("GMT Standard Time", "Europe/London");

    private static TimeZoneInfo Zone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new InvalidOperationException("No UK time zone on this machine.");
    }

    private static DateTime Utc(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static DateTime Wall(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0);

    // ---- Snooze ----

    [Fact]
    public void Snoozing_ten_minutes_as_the_clocks_go_back_rings_ten_real_minutes_later()
    {
        // 01:55 BST is 00:55 UTC; ten minutes on is 01:05 UTC, which the clock reads as 01:05 GMT.
        var t = new TriggerTimer { TriggerAt = Wall(10, 25, 1, 50), HasFired = true };

        TriggerReconciler.Snooze(t, Utc(10, 25, 0, 55), 10, Uk);

        Assert.Equal(Utc(10, 25, 1, 5), t.DueUtc);
        Assert.Equal(Wall(10, 25, 1, 5), t.TriggerAt);
        Assert.Equal(DateTimeKind.Unspecified, t.TriggerAt.Kind);
        Assert.False(t.HasFired);
    }

    [Fact]
    public void Snoozing_ten_minutes_as_the_clocks_go_forward_rings_ten_real_minutes_later()
    {
        // 00:55 GMT; ten minutes on is 01:05 UTC, which the clock reads as 02:05 BST.
        var t = new TriggerTimer { TriggerAt = Wall(3, 29, 0, 50), HasFired = true };

        TriggerReconciler.Snooze(t, Utc(3, 29, 0, 55), 10, Uk);

        Assert.Equal(Utc(3, 29, 1, 5), t.DueUtc);
        Assert.Equal(Wall(3, 29, 2, 5), t.TriggerAt);
    }

    [Fact]
    public void A_snooze_on_an_ordinary_day_is_the_same_as_before()
    {
        var t = new TriggerTimer { TriggerAt = Wall(6, 3, 9, 0), HasFired = true };

        TriggerReconciler.Snooze(t, Utc(6, 3, 8, 0), 5, Uk); // 09:00 BST

        Assert.Equal(Wall(6, 3, 9, 5), t.TriggerAt);
    }

    // ---- "in N min/hours" ----

    [Fact]
    public void In_two_hours_as_the_clocks_go_back_is_two_real_hours()
    {
        // 00:30 BST is 23:30 UTC the day before; two hours on is 01:30 UTC, 01:30 GMT on the clock.
        var result = QuickAddParser.Parse("call back in 2 hours", Utc(10, 24, 23, 30), Uk);

        Assert.Equal("call back", result.Name);
        Assert.Equal(Utc(10, 25, 1, 30), result.DueUtc);
        Assert.Equal(Wall(10, 25, 1, 30), result.When);
    }

    [Fact]
    public void In_two_hours_as_the_clocks_go_forward_is_two_real_hours()
    {
        // 00:30 GMT; two hours on is 02:30 UTC, 03:30 BST on the clock.
        var result = QuickAddParser.Parse("call back in 2 hours", Utc(3, 29, 0, 30), Uk);

        Assert.Equal(Utc(3, 29, 2, 30), result.DueUtc);
        Assert.Equal(Wall(3, 29, 3, 30), result.When);
    }

    [Fact]
    public void A_time_on_the_clock_has_no_real_instant_and_is_a_wall_clock_time()
    {
        var result = QuickAddParser.Parse("call mom 3pm", new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Local), Uk);

        Assert.Null(result.DueUtc);
        Assert.Equal(Wall(6, 3, 15, 0), result.When);
        Assert.Equal(DateTimeKind.Unspecified, result.When!.Value.Kind);
    }

    // ---- The engine goes by the real instant ----

    [Fact]
    public void A_snooze_into_the_repeated_hour_doesnt_ring_early_on_the_first_pass()
    {
        // Snoozed at 01:55 BST to 01:05 GMT. A minute later (00:56 UTC) the clock reads 01:56 BST,
        // which is past 01:05 on the wall, but the snooze has nine real minutes to go.
        var t = new TriggerTimer { TriggerAt = Wall(10, 25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(10, 25, 0, 55), 10, Uk);
        var fired = new List<TriggerTimer>();
        var wall = Wall(10, 25, 1, 56);
        var utc = Utc(10, 25, 0, 56);
        using var engine = new TimerEngine(() => wall, polls: false, () => utc);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(new[] { t });
        engine.Start(null);

        engine.CheckDue();
        Assert.Empty(fired);

        (wall, utc) = (Wall(10, 25, 1, 5), Utc(10, 25, 1, 5));
        engine.CheckDue();
        Assert.Single(fired);
    }

    [Fact]
    public void A_snooze_across_the_spring_gap_rings_at_its_real_time()
    {
        var t = new TriggerTimer { TriggerAt = Wall(3, 29, 0, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(3, 29, 0, 55), 10, Uk);
        var fired = new List<TriggerTimer>();
        var wall = Wall(3, 29, 2, 4);
        var utc = Utc(3, 29, 1, 4);
        using var engine = new TimerEngine(() => wall, polls: false, () => utc);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(new[] { t });
        engine.Start(null);

        engine.CheckDue();
        Assert.Empty(fired);

        (wall, utc) = (Wall(3, 29, 2, 5), Utc(3, 29, 1, 5));
        engine.CheckDue();
        Assert.Single(fired);
    }

    [Fact]
    public void A_time_set_on_the_clock_goes_by_the_clock_whatever_the_utc_clock_says()
    {
        // A daily 09:00 reminder keeps its clock time; the UTC clock doesn't come into it.
        var t = new TriggerTimer { TriggerAt = Wall(6, 3, 9, 0) };
        var fired = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => Wall(6, 3, 9, 0), polls: false, () => Utc(1, 1, 0, 0));
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(new[] { t });
        engine.Start(null);

        engine.CheckDue();

        Assert.Single(fired);
    }

    [Fact]
    public void Setting_the_time_any_other_way_drops_the_snoozes_instant()
    {
        var t = new TriggerTimer { TriggerAt = Wall(10, 25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(10, 25, 0, 55), 10, Uk);
        Assert.NotNull(t.DueUtc);

        t.TriggerAt = Wall(10, 26, 9, 0); // moved on, edited, caught up...

        Assert.Null(t.DueUtc);
    }

    // ---- One kind of saved time ----

    [Fact]
    public void A_local_and_a_wall_clock_time_save_and_load_as_the_same_kind()
    {
        var local = new TriggerTimer { TriggerAt = new DateTime(2026, 6, 3, 15, 0, 0, DateTimeKind.Local), RecurrenceAnchor = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Local) };
        var wall = new TriggerTimer { TriggerAt = Wall(6, 3, 15, 0), RecurrenceAnchor = Wall(6, 1, 9, 0) };

        var localJson = JsonSerializer.Serialize(local);
        var wallJson = JsonSerializer.Serialize(wall);
        var localBack = JsonSerializer.Deserialize<TriggerTimer>(localJson)!;
        var wallBack = JsonSerializer.Deserialize<TriggerTimer>(wallJson)!;

        Assert.Contains("\"TriggerAt\":\"2026-06-03T15:00:00\"", localJson);
        Assert.Contains("\"RecurrenceAnchor\":\"2026-06-01T09:00:00\"", localJson);
        Assert.Contains("\"TriggerAt\":\"2026-06-03T15:00:00\"", wallJson);
        Assert.Equal(DateTimeKind.Unspecified, localBack.TriggerAt.Kind);
        Assert.Equal(DateTimeKind.Unspecified, wallBack.TriggerAt.Kind);
        Assert.Equal(localBack.TriggerAt, wallBack.TriggerAt);
        Assert.Equal(DateTimeKind.Unspecified, localBack.RecurrenceAnchor.Kind);
    }

    [Fact]
    public void A_time_saved_with_an_offset_by_an_earlier_version_loads_as_this_pcs_clock_time()
    {
        // Earlier versions saved a snooze's or a Quick Reminder's time with this PC's offset.
        var local = new DateTime(2026, 6, 3, 15, 0, 0, DateTimeKind.Local);
        var withOffset = JsonSerializer.Serialize(local); // "2026-06-03T15:00:00+01:00", or this PC's offset
        var json = $$"""{ "Name": "x", "TriggerAt": {{withOffset}}, "RecurrenceAnchor": {{withOffset}} }""";

        var t = JsonSerializer.Deserialize<TriggerTimer>(json)!;

        Assert.Equal(Wall(6, 3, 15, 0), t.TriggerAt);
        Assert.Equal(DateTimeKind.Unspecified, t.TriggerAt.Kind);
        Assert.Equal(Wall(6, 3, 15, 0), t.RecurrenceAnchor);
        Assert.Null(t.DueUtc);
    }

    [Fact]
    public void A_snoozes_instant_saves_and_loads_as_utc()
    {
        var t = new TriggerTimer { TriggerAt = Wall(10, 25, 1, 50), HasFired = true };
        TriggerReconciler.Snooze(t, Utc(10, 25, 0, 55), 10, Uk);

        var back = JsonSerializer.Deserialize<TriggerTimer>(JsonSerializer.Serialize(t))!;

        Assert.Equal(Utc(10, 25, 1, 5), back.DueUtc);
        Assert.Equal(DateTimeKind.Utc, back.DueUtc!.Value.Kind);
        Assert.Equal(Wall(10, 25, 1, 5), back.TriggerAt);
    }

    // ---- The helper ----

    [Fact]
    public void A_wall_time_in_the_spring_gap_counts_as_just_after_it()
    {
        Assert.Equal(Utc(3, 29, 1, 0), WallClock.ToUtc(Wall(3, 29, 1, 30), Uk));
    }
}
