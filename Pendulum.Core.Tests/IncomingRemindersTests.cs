using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// Reminders coming in from an import or a backup are caught up before they reach the engine, as
/// at startup, so ones already past their time don't all ring at once. Fixed clocks, no waits.
public sealed class IncomingRemindersTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// What import and restore do: catch up, then hand the list to the engine, which polls once.
    private static (List<TriggerTimer> CaughtUp, List<TriggerTimer> Fired) Bring(List<TriggerTimer> incoming)
    {
        var caughtUp = TriggerReconciler.CatchUpMissedTriggers(incoming, Now);
        var fired = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => Now, polls: false);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(incoming);
        engine.Start(null);
        engine.CheckDue();
        return (caughtUp, fired);
    }

    [Fact]
    public void An_imported_past_one_off_is_spent_and_doesnt_ring()
    {
        var t = new TriggerTimer { Name = "dentist", TriggerAt = Now.AddDays(-20), RecurrenceAnchor = Now.AddDays(-20) };

        var (caughtUp, fired) = Bring(new List<TriggerTimer> { t });

        Assert.Equal(new[] { t }, caughtUp);
        Assert.Empty(fired);
        Assert.True(t.HasFired);
        Assert.False(t.Enabled);
    }

    [Fact]
    public void An_imported_past_repeating_reminder_moves_to_its_next_time_and_doesnt_ring()
    {
        var t = new TriggerTimer
        {
            Name = "stand-up",
            TriggerAt = new DateTime(2026, 10, 1, 9, 0, 0),
            RecurrenceAnchor = new DateTime(2026, 9, 1, 9, 0, 0),
            Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 30 },
        };

        var (caughtUp, fired) = Bring(new List<TriggerTimer> { t });

        Assert.Equal(new[] { t }, caughtUp);
        Assert.Empty(fired);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), t.TriggerAt);
        Assert.False(t.HasFired);
        Assert.True(t.Enabled);
        Assert.Equal(30 + 32, t.Recurrence!.OccurrencesSoFar); // 1 Oct to 1 Nov, every day
    }

    [Fact]
    public void Only_the_ones_past_their_time_are_caught_up_and_future_ones_keep_theirs()
    {
        var past = new TriggerTimer { Name = "past", TriggerAt = Now.AddHours(-1), RecurrenceAnchor = Now.AddHours(-1) };
        var future = new TriggerTimer { Name = "future", TriggerAt = Now.AddHours(1), RecurrenceAnchor = Now.AddHours(1) };
        var spent = new TriggerTimer { Name = "spent", TriggerAt = Now.AddDays(-3), HasFired = true, Enabled = false };
        var disabled = new TriggerTimer { Name = "disabled", TriggerAt = Now.AddDays(-3), Enabled = false };

        var (caughtUp, fired) = Bring(new List<TriggerTimer> { past, future, spent, disabled });

        Assert.Equal(new[] { past }, caughtUp);
        Assert.Empty(fired);
        Assert.Equal(Now.AddHours(1), future.TriggerAt);
        Assert.False(future.HasFired);
        Assert.False(disabled.HasFired);
    }

    [Fact]
    public void A_reminder_exported_while_it_was_ringing_comes_in_scheduled()
    {
        var t = new TriggerTimer
        {
            Name = "ringing",
            TriggerAt = new DateTime(2026, 10, 31, 9, 0, 0),
            RecurrenceAnchor = new DateTime(2026, 10, 1, 9, 0, 0),
            Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily },
            HasFired = true,
        };

        var (caughtUp, fired) = Bring(new List<TriggerTimer> { t });

        Assert.Single(caughtUp);
        Assert.Empty(fired);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), t.TriggerAt);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v1.1.0")]
    public void Restoring_a_months_old_backup_rings_nothing_and_keeps_every_reminder(string release)
    {
        var path = _temp.File("backup.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", release, "backup.json"), path);
        var backup = BackupRepository.Load(path)!;

        var (caughtUp, fired) = Bring(backup.Triggers);

        Assert.Empty(fired);
        Assert.Equal(6, backup.Triggers.Count);
        Assert.Contains(caughtUp, t => t.Name == "Call the dentist");
        Assert.All(backup.Triggers.Where(t => t.Enabled && !t.HasFired), t => Assert.True(t.TriggerAt > Now));
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v1.1.0")]
    public void Importing_an_old_export_rings_nothing(string release)
    {
        var path = _temp.File("triggers.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", release, "triggers.json"), path);
        var imported = new TriggerRepository().LoadFrom(path);

        var (_, fired) = Bring(imported);

        Assert.Empty(fired);
        Assert.Equal(6, imported.Count);
    }

    [Fact]
    public void Without_the_catch_up_the_engine_would_ring_every_past_reminder()
    {
        // What import and restore did before: straight to the engine.
        var incoming = new List<TriggerTimer>
        {
            new() { Name = "a", TriggerAt = Now.AddDays(-2) },
            new() { Name = "b", TriggerAt = Now.AddDays(-1) },
        };
        var fired = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => Now, polls: false);
        engine.TriggerFired += fired.Add;
        engine.SetTriggers(incoming);
        engine.Start(null);

        engine.CheckDue();

        Assert.Equal(2, fired.Count);
    }

    [Fact]
    public void The_names_list_for_the_startup_notice_matches_the_triggers()
    {
        var list = new List<TriggerTimer>
        {
            new() { Name = "x", TriggerAt = Now.AddHours(-1) },
            new() { Name = "y", TriggerAt = Now.AddHours(1) },
        };

        Assert.Equal(new[] { "x" }, TriggerReconciler.CatchUpMissed(list, Now));
    }
}
