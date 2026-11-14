using System.Text.Json;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

public sealed class NoneRecurrenceTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0);

    [Fact]
    public void Changing_a_rule_frequency_notifies_its_derived_type_and_detaches_old_rules()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None };
        var reminder = new TriggerTimer { Recurrence = rule };
        var changed = new List<string?>();
        reminder.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        rule.Frequency = RecurrenceFrequency.Daily;
        Assert.True(reminder.IsScheduled);
        Assert.Same(rule, reminder.RepeatRule);
        Assert.Contains(nameof(TriggerTimer.IsScheduled), changed);
        Assert.Contains(nameof(TriggerTimer.RepeatRule), changed);

        reminder.Recurrence = new() { Frequency = RecurrenceFrequency.None };
        changed.Clear();
        rule.Frequency = RecurrenceFrequency.Weekly;
        Assert.Empty(changed);
        Assert.False(reminder.IsScheduled);
        Assert.Null(reminder.RepeatRule);
    }

    [Fact]
    public void Derived_repeat_and_auto_resolve_helpers_are_not_saved()
    {
        var reminder = new TriggerTimer { Recurrence = new() { Frequency = RecurrenceFrequency.None, OccurrencesSoFar = 7 } };
        var json = JsonSerializer.SerializeToElement(reminder);
        Assert.False(json.TryGetProperty(nameof(TriggerTimer.RepeatRule), out _));
        var restored = json.Deserialize<TriggerTimer>()!;
        Assert.NotNull(restored.Recurrence);
        Assert.Equal(RecurrenceFrequency.None, restored.Recurrence.Frequency);
        Assert.Equal(7, restored.Recurrence.OccurrencesSoFar);
        Assert.Null(restored.RepeatRule);

        var settings = new AppSettings { RepeatAlertUntilDismissed = false, AutoResolveMinutes = 5 };
        Assert.Equal(TimeSpan.FromMinutes(5), settings.AutoResolveDelay);
        Assert.False(JsonSerializer.SerializeToElement(settings).TryGetProperty(nameof(AppSettings.AutoResolveDelay), out _));
    }

    [Fact]
    public void A_no_frequency_rule_ignores_repeat_end_conditions_in_the_calendar()
    {
        var reminder = new TriggerTimer
        {
            TriggerAt = Now,
            Recurrence = new() { Frequency = RecurrenceFrequency.None, EndType = RecurrenceEndType.ByDate, EndDate = Now.AddDays(-1) },
        };
        Assert.Equal(new[] { Now }, RecurrenceCalculator.GetOccurrencesInRange(reminder, Now.AddDays(-1), Now.AddDays(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rule_with_no_frequency_fires_and_dismisses_like_a_one_off(bool hasRule)
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None };
        var reminder = new TriggerTimer { TriggerAt = Now, Recurrence = hasRule ? rule : null };
        var alerts = new List<TriggerTimer>();
        using var engine = new TimerEngine(() => Now, polls: false);
        engine.SetTriggers([reminder]);
        engine.TriggerFired += alerts.Add;
        engine.Start(null);
        engine.CheckDue();
        Assert.Same(reminder, Assert.Single(alerts));
        Assert.True(reminder.HasFired);
        Assert.True(TriggerReconciler.Dismiss(reminder, Now, Now.AddMinutes(1)));
        Assert.True(reminder.HasFired);
        Assert.False(reminder.Enabled);
        Assert.Equal(Now, reminder.TriggerAt);
        Assert.Equal(0, rule.OccurrencesSoFar);
    }

    [Fact]
    public void Catching_up_a_rule_with_no_frequency_does_not_count_an_occurrence()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None };
        var reminder = new TriggerTimer { TriggerAt = Now, Recurrence = rule };
        Assert.Single(TriggerReconciler.CatchUpMissed([reminder], Now.AddDays(1)));
        Assert.True(reminder.HasFired);
        Assert.False(reminder.Enabled);
        Assert.Equal(0, rule.OccurrencesSoFar);
    }

    [Fact]
    public void Snoozing_a_rule_with_no_frequency_keeps_its_saved_count_untouched()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None, OccurrencesSoFar = 7 };
        var reminder = new TriggerTimer { TriggerAt = Now, Recurrence = rule, HasFired = true };
        TriggerReconciler.Snooze(reminder, Now, 5, TimeZoneInfo.Utc);
        Assert.False(reminder.HasFired);
        Assert.Null(reminder.SnoozedOccurrence);
        Assert.Equal(7, rule.OccurrencesSoFar);
    }

    [Fact]
    public void A_rule_with_no_frequency_is_fixed_in_the_list_and_filter()
    {
        var reminder = new TriggerTimer { Recurrence = new() { Frequency = RecurrenceFrequency.None } };
        var fixedFilter = new ReminderFilter(new() { ReminderFilterTypeFixed = true });
        var scheduledFilter = new ReminderFilter(new() { ReminderFilterTypeScheduled = true });
        Assert.False(reminder.IsScheduled);
        Assert.True(fixedFilter.Matches(reminder));
        Assert.False(scheduledFilter.Matches(reminder));
    }
}
