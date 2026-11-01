using Pendulum.Core.Engine;

namespace Pendulum.Core.Tests;

/// Which open alert makes a sound: the decisions only, with no audio and no windows.
public class AlertQueueTests
{
    private sealed record Alert(string Name);

    private static readonly Alert Dentist = new("dentist");
    private static readonly Alert Bins = new("bins");
    private static readonly Alert Meds = new("meds");

    [Fact]
    public void The_first_alert_rings_and_silences_nothing()
    {
        var queue = new AlertQueue<Alert>();

        var change = queue.Open(Dentist);

        Assert.Null(change.Silence);
        Assert.Same(Dentist, change.Ring);
        Assert.False(change.TurnCame);
        Assert.Same(Dentist, queue.Ringing);
    }

    [Fact]
    public void A_newer_alert_rings_and_the_one_ringing_falls_silent()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);

        var change = queue.Open(Bins);

        Assert.Same(Dentist, change.Silence);
        Assert.Same(Bins, change.Ring);
        Assert.Same(Bins, queue.Ringing);
        Assert.Equal(new[] { Dentist }, queue.Waiting);
    }

    [Fact]
    public void Only_ever_one_rings_however_many_are_open()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);
        queue.Open(Bins);
        queue.Open(Meds);

        Assert.Same(Meds, queue.Ringing);
        Assert.Equal(new[] { Bins, Dentist }, queue.Waiting);
        Assert.Equal(new[] { Meds, Bins, Dentist }, queue.NewestFirst);
    }

    [Fact]
    public void Dismissing_the_ringing_one_hands_the_sound_to_the_next_newest_whose_turn_has_come()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);
        queue.Open(Bins);
        queue.Open(Meds);

        var change = queue.Close(Meds);

        Assert.Null(change.Silence);
        Assert.Same(Bins, change.Ring);
        Assert.True(change.TurnCame);
        Assert.Same(Bins, queue.Ringing);
    }

    [Fact]
    public void Working_down_the_queue_rings_each_in_turn_newest_first()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);
        queue.Open(Bins);
        queue.Open(Meds);

        Assert.Same(Bins, queue.Close(Meds).Ring);
        Assert.Same(Dentist, queue.Close(Bins).Ring);
        var last = queue.Close(Dentist);

        Assert.Null(last.Ring);
        Assert.False(last.TurnCame);
        Assert.Null(queue.Ringing);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Closing_a_waiting_alert_changes_nothing_for_the_ringing_one()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);
        queue.Open(Bins);

        var change = queue.Close(Dentist);

        Assert.Null(change.Silence);
        Assert.Null(change.Ring);
        Assert.Same(Bins, queue.Ringing);
        Assert.Empty(queue.Waiting);
    }

    [Fact]
    public void A_new_alert_while_a_waiting_one_has_taken_over_rings_and_silences_that_one()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);
        queue.Open(Bins);
        queue.Close(Bins); // Dentist's turn

        var change = queue.Open(Meds);

        Assert.Same(Dentist, change.Silence);
        Assert.Same(Meds, change.Ring);
        Assert.False(change.TurnCame);
    }

    [Fact]
    public void Closing_or_opening_the_same_alert_twice_is_harmless()
    {
        var queue = new AlertQueue<Alert>();
        queue.Open(Dentist);

        Assert.Equal(default, queue.Open(Dentist));
        queue.Close(Dentist);
        Assert.Equal(default, queue.Close(Dentist));
        Assert.Equal(0, queue.Count);
    }
}
