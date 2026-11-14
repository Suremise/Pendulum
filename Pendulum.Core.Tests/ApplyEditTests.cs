using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.Core.Tests;

public sealed class ApplyEditTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 10, 0, 0);
    private static TriggerEditDraft Draft(TriggerTimer target, DateTime at) => new()
    {
        Name = " edited ",
        ShownAt = RecurrenceCalculator.ScheduledOccurrence(target),
        ScheduledAt = at,
        Recurrence = target.RepeatRule,
        Mode = AlertMode.SoundAndSpeech,
        SoundFileName = "bell.wav",
        Phrase = "Dentist",
        Volume = 65,
        Enabled = true,
        Color = "teal",
    };

    [Fact]
    public void A_past_one_off_is_saved_spent_and_off()
    {
        var target = new TriggerTimer { TriggerAt = Now.AddHours(1) };
        TriggerReconciler.ApplyEdit(target, Draft(target, Now.AddHours(-1)), Now);
        Assert.Equal("edited", target.Name);
        Assert.Equal(Now.AddHours(-1), target.TriggerAt);
        Assert.True(target.HasFired);
        Assert.False(target.Enabled);
        Assert.Equal(AlertMode.SoundAndSpeech, target.Mode);
        Assert.Equal("bell.wav", target.SoundFileName);
        Assert.Equal("Dentist", target.Phrase);
        Assert.Equal(65, target.Volume);
    }

    [Theory]
    [InlineData("future-colour", false, "future-colour")]
    [InlineData("rose", false, "rose")]
    [InlineData("rose", true, "teal")]
    [InlineData(null, false, "teal")]
    public void Colour_is_kept_until_chosen_or_missing(string? saved, bool chosen, string expected)
    {
        var target = new TriggerTimer { TriggerAt = Now.AddHours(1), Color = saved };
        TriggerReconciler.ApplyEdit(target, Draft(target, target.TriggerAt) with { ColorChosen = chosen }, Now);
        Assert.Equal(expected, target.Color);
    }

    [Fact]
    public void An_unchanged_snooze_keeps_its_instant_occurrence_and_series_anchor()
    {
        var occurrence = Now.AddHours(-1);
        var target = new TriggerTimer { Recurrence = new RecurrenceRule(), RecurrenceAnchor = occurrence };
        var due = DateTime.SpecifyKind(Now.AddMinutes(5), DateTimeKind.Utc);
        target.SetDue(Now.AddMinutes(5), due, occurrence);
        TriggerReconciler.ApplyEdit(target, Draft(target, occurrence), Now, DateTime.SpecifyKind(Now, DateTimeKind.Utc));
        Assert.Equal(Now.AddMinutes(5), target.TriggerAt);
        Assert.Equal(due, target.DueUtc);
        Assert.Equal(occurrence, target.SnoozedOccurrence);
        Assert.Equal(occurrence, target.RecurrenceAnchor);
        Assert.False(target.HasFired);
    }

    [Fact]
    public void A_changed_repeat_starts_on_its_pattern_and_keeps_the_users_disabled_choice()
    {
        var target = new TriggerTimer { TriggerAt = Now.AddHours(1), Recurrence = new RecurrenceRule() };
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = [DayOfWeek.Friday] };
        var start = Now.Date.AddDays(1).AddHours(9);
        TriggerReconciler.ApplyEdit(target, Draft(target, start) with { Recurrence = rule, Enabled = false }, Now);
        Assert.Same(rule, target.Recurrence);
        Assert.Equal(start, target.RecurrenceAnchor);
        Assert.Equal(new DateTime(2026, 6, 5, 9, 0, 0), target.TriggerAt);
        Assert.False(target.HasFired);
        Assert.False(target.Enabled);
    }

    [Fact]
    public void An_edit_is_one_store_update_and_a_removed_target_is_ignored()
    {
        var target = new TriggerTimer { TriggerAt = Now.AddHours(1) };
        using var store = new ReminderStore([target]);
        var changes = 0;
        store.RemindersChanged += (_, _) => changes++;
        var draft = Draft(target, Now.AddHours(2));
        Assert.True(store.Update(target, () => TriggerReconciler.ApplyEdit(target, draft, Now)));
        Assert.Equal(1, changes);
        store.Remove(target);
        Assert.False(store.Update(target, () => TriggerReconciler.ApplyEdit(target, draft with { Name = "stale" }, Now)));
        Assert.Equal("edited", target.Name);
    }
}
