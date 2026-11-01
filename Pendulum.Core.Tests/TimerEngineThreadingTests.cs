using System.ComponentModel;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// Where and when the engine changes reminders: only on the context Start was called on (the UI
/// thread, in the app), never before Start, and one reminder's failure never costs another its
/// alert. Fixed clocks, polled by hand, no waits.
public class TimerEngineThreadingTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 9, 0, 0);

    /// A context that keeps what's posted to it until the test runs it, as a busy UI thread would.
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int Pending => _posted.Count;

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out var work))
                work.Callback(work.State);
        }
    }

    private static TimerEngine Engine(params TriggerTimer[] triggers)
    {
        var engine = new TimerEngine(() => Now, polls: false);
        engine.SetTriggers(triggers);
        return engine;
    }

    [Fact]
    public void Nothing_fires_before_start()
    {
        var t = new TriggerTimer { TriggerAt = Now };
        var fired = new List<TriggerTimer>();
        using var engine = Engine(t);
        engine.TriggerFired += fired.Add;

        engine.CheckDue();

        Assert.Empty(fired);
        Assert.False(t.HasFired);

        engine.Start(null);
        engine.CheckDue();

        Assert.Single(fired);
    }

    [Fact]
    public void The_poll_changes_nothing_itself_it_posts_the_firing_to_starts_context()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Daily, OccurrencesSoFar = 2 };
        var t = new TriggerTimer { TriggerAt = Now, RecurrenceAnchor = Now, Recurrence = rule };
        var changes = new List<string?>();
        t.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var fired = new List<TriggerTimer>();
        var context = new QueuedContext();
        using var engine = Engine(t);
        engine.TriggerFired += fired.Add;
        engine.Start(context);

        engine.CheckDue();

        // On the poll's thread: nothing marked, counted, saved or fired.
        Assert.Empty(changes);
        Assert.False(t.HasFired);
        Assert.Equal(2, rule.OccurrencesSoFar);
        Assert.Empty(fired);
        Assert.Equal(1, context.Pending);

        context.RunAll();

        Assert.True(t.HasFired);
        Assert.Equal(3, rule.OccurrencesSoFar);
        Assert.Contains(nameof(TriggerTimer.HasFired), changes);
        Assert.Equal(new[] { t }, fired);
    }

    [Fact]
    public void Two_polls_before_the_context_runs_fire_the_reminder_once()
    {
        var t = new TriggerTimer { TriggerAt = Now };
        var fired = new List<TriggerTimer>();
        var context = new QueuedContext();
        using var engine = Engine(t);
        engine.TriggerFired += fired.Add;
        engine.Start(context);

        engine.CheckDue();
        engine.CheckDue();
        context.RunAll();

        Assert.Single(fired);
    }

    [Fact]
    public void Nothing_due_posts_nothing()
    {
        var context = new QueuedContext();
        using var engine = Engine(new TriggerTimer { TriggerAt = Now.AddMinutes(5) });
        engine.Start(context);

        engine.CheckDue();

        Assert.Equal(0, context.Pending);
    }

    [Fact]
    public void A_save_that_throws_for_one_reminder_still_lets_it_and_the_others_go_off()
    {
        // The app saves the list when a reminder's HasFired changes: here that save fails for "b".
        var a = new TriggerTimer { Name = "a", TriggerAt = Now };
        var b = new TriggerTimer { Name = "b", TriggerAt = Now };
        var c = new TriggerTimer { Name = "c", TriggerAt = Now };
        b.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TriggerTimer.HasFired))
                throw new IOException("triggers.json.tmp is in use");
        };
        var fired = new List<string>();
        var failures = new List<Exception>();
        using var engine = Engine(a, b, c);
        engine.TriggerFired += t => fired.Add(t.Name);
        engine.TickFailed += failures.Add;
        engine.Start(null);

        engine.CheckDue();

        Assert.Equal(new[] { "a", "b", "c" }, fired);
        Assert.True(b.HasFired);
        Assert.IsType<IOException>(Assert.Single(failures));
    }

    [Fact]
    public void A_handler_that_throws_for_one_reminder_doesnt_stop_the_later_ones()
    {
        var a = new TriggerTimer { Name = "a", TriggerAt = Now };
        var b = new TriggerTimer { Name = "b", TriggerAt = Now };
        var c = new TriggerTimer { Name = "c", TriggerAt = Now };
        var fired = new List<string>();
        var failures = new List<Exception>();
        using var engine = Engine(a, b, c);
        engine.TriggerFired += t =>
        {
            if (t == a) throw new InvalidOperationException("Collection was modified");
            fired.Add(t.Name);
        };
        engine.TickFailed += failures.Add;
        engine.Start(null);

        engine.CheckDue();

        Assert.Equal(new[] { "b", "c" }, fired);
        Assert.Single(failures);
        Assert.All(new[] { a, b, c }, t => Assert.True(t.HasFired));
    }

    [Fact]
    public void A_failing_failure_report_still_doesnt_escape_the_poll()
    {
        var t = new TriggerTimer { TriggerAt = Now };
        using var engine = Engine(t);
        engine.TriggerFired += _ => throw new InvalidOperationException("handler");
        engine.TickFailed += _ => throw new InvalidOperationException("reporter");
        engine.Start(null);

        var thrown = Record.Exception(engine.CheckDue);

        Assert.Null(thrown);
        Assert.True(t.HasFired);
    }

    [Fact]
    public void A_reminder_removed_before_the_posted_firing_runs_doesnt_go_off()
    {
        var t = new TriggerTimer { TriggerAt = Now };
        var fired = new List<TriggerTimer>();
        var context = new QueuedContext();
        using var engine = Engine(t);
        engine.TriggerFired += fired.Add;
        engine.Start(context);

        engine.CheckDue();
        engine.SetTriggers(Array.Empty<TriggerTimer>());
        context.RunAll();

        Assert.Empty(fired);
        Assert.False(t.HasFired);
    }

    [Fact]
    public void Starting_twice_keeps_the_first_context()
    {
        var first = new QueuedContext();
        var second = new QueuedContext();
        using var engine = Engine(new TriggerTimer { TriggerAt = Now });
        engine.Start(first);
        engine.Start(second);

        engine.CheckDue();

        Assert.Equal(1, first.Pending);
        Assert.Equal(0, second.Pending);
    }
}
