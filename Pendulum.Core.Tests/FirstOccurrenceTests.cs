using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// A repeating reminder's first firing, the Outlook way: on the pattern, which may be after the
/// start. Wednesday 3 June 2026 09:00 is the start in most of these; "now" is before it.
public class FirstOccurrenceTests
{
    private static readonly DateTime Wednesday = new(2026, 6, 3, 9, 0, 0);
    private static readonly DateTime Before = new(2026, 6, 1, 8, 0, 0);

    private static RecurrenceRule Weekly(int interval = 1, params DayOfWeek[] days) =>
        new() { Frequency = RecurrenceFrequency.Weekly, Interval = interval, DaysOfWeek = days.ToList() };

    private static DateTime D(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0);

    // ---- Weekly ----

    [Fact]
    public void Starting_on_a_day_the_week_doesnt_repeat_on_first_fires_on_the_next_one_that_does()
    {
        Assert.Equal(D(2026, 6, 8), RecurrenceCalculator.FirstOccurrence(Weekly(1, DayOfWeek.Monday), Wednesday, Before));
    }

    [Fact]
    public void Starting_on_one_of_its_days_first_fires_at_the_start()
    {
        Assert.Equal(Wednesday, RecurrenceCalculator.FirstOccurrence(Weekly(1, DayOfWeek.Monday, DayOfWeek.Wednesday), Wednesday, Before));
    }

    [Fact]
    public void A_later_day_in_the_same_week_comes_first()
    {
        Assert.Equal(D(2026, 6, 5), RecurrenceCalculator.FirstOccurrence(Weekly(1, DayOfWeek.Monday, DayOfWeek.Friday), Wednesday, Before));
    }

    [Fact]
    public void Weekly_with_no_days_chosen_repeats_on_the_start_day_so_starts_at_the_start()
    {
        Assert.Equal(Wednesday, RecurrenceCalculator.FirstOccurrence(Weekly(), Wednesday, Before));
    }

    [Fact]
    public void Every_other_week_skips_to_the_next_week_it_runs_in()
    {
        // The start's own week has its Monday before the start, so the first is two weeks on.
        Assert.Equal(D(2026, 6, 15), RecurrenceCalculator.FirstOccurrence(Weekly(2, DayOfWeek.Monday), Wednesday, Before));
        Assert.Equal(D(2026, 6, 5), RecurrenceCalculator.FirstOccurrence(Weekly(2, DayOfWeek.Friday), Wednesday, Before));
    }

    // ---- Monthly and yearly ----

    [Fact]
    public void Monthly_on_a_later_day_fires_first_that_day_this_month()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 20 };

        Assert.Equal(D(2026, 6, 20), RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void Monthly_on_an_earlier_day_fires_first_next_month()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 1 };

        Assert.Equal(D(2026, 7, 1), RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void Monthly_on_the_start_day_fires_at_the_start()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 3 };

        Assert.Equal(Wednesday, RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void Monthly_on_the_31st_starting_on_the_30th_of_june_fits_since_june_has_30_days()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 31 };

        Assert.Equal(D(2026, 6, 30), RecurrenceCalculator.FirstOccurrence(rule, D(2026, 6, 30), Before));
    }

    [Fact]
    public void Yearly_on_a_later_date_this_year_fires_then()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly, Month = 12, DayOfMonth = 25 };

        Assert.Equal(D(2026, 12, 25), RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void Yearly_on_a_date_already_gone_this_year_fires_next_year()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly, Month = 1, DayOfMonth = 5 };

        Assert.Equal(D(2027, 1, 5), RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void Yearly_on_the_29th_of_february_from_a_leap_day_start_fits()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.Yearly, Month = 2, DayOfMonth = 29 };

        Assert.Equal(D(2028, 2, 29), RecurrenceCalculator.FirstOccurrence(rule, D(2028, 2, 29), Before));
        Assert.Equal(D(2027, 2, 28), RecurrenceCalculator.FirstOccurrence(rule, D(2027, 2, 28), Before));
    }

    [Fact]
    public void Daily_always_starts_at_the_start()
    {
        Assert.Equal(Wednesday, RecurrenceCalculator.FirstOccurrence(new RecurrenceRule { Frequency = RecurrenceFrequency.Daily }, Wednesday, Before));
    }

    // ---- Now and the end ----

    [Fact]
    public void A_start_in_the_past_goes_on_to_the_next_occurrence_on_the_pattern()
    {
        // Started Wednesday 3 June, Mondays only; it's now Tuesday 9 June: Monday 8th has gone, so the 15th.
        Assert.Equal(D(2026, 6, 15), RecurrenceCalculator.FirstOccurrence(Weekly(1, DayOfWeek.Monday), Wednesday, D(2026, 6, 9, 12)));
    }

    [Fact]
    public void A_start_in_the_past_that_fits_the_pattern_goes_on_to_the_next()
    {
        Assert.Equal(D(2026, 6, 10), RecurrenceCalculator.FirstOccurrence(Weekly(1, DayOfWeek.Wednesday), Wednesday, D(2026, 6, 3, 12)));
    }

    [Fact]
    public void An_end_date_before_the_first_occurrence_on_the_pattern_leaves_nothing()
    {
        var rule = Weekly(1, DayOfWeek.Monday);
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 6, 7);

        Assert.Null(RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void An_end_date_on_the_first_occurrence_keeps_it()
    {
        var rule = Weekly(1, DayOfWeek.Monday);
        rule.EndType = RecurrenceEndType.ByDate;
        rule.EndDate = new DateTime(2026, 6, 8);

        Assert.Equal(D(2026, 6, 8), RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void A_count_already_used_up_leaves_nothing()
    {
        var rule = Weekly(1, DayOfWeek.Monday);
        rule.EndType = RecurrenceEndType.AfterOccurrences;
        rule.OccurrenceCount = 3;
        rule.OccurrencesSoFar = 3;

        Assert.Null(RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
    }

    [Fact]
    public void No_frequency_behaves_as_a_one_off()
    {
        var rule = new RecurrenceRule { Frequency = RecurrenceFrequency.None };

        Assert.Equal(Wednesday, RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before));
        Assert.Null(RecurrenceCalculator.FirstOccurrence(rule, Wednesday, D(2026, 6, 4)));
    }

    // ---- And after the first, the series runs on the pattern ----

    [Fact]
    public void After_the_first_firing_the_series_keeps_to_its_days()
    {
        var rule = Weekly(1, DayOfWeek.Monday);
        var trigger = new TriggerTimer { RecurrenceAnchor = Wednesday, Recurrence = rule };
        trigger.TriggerAt = RecurrenceCalculator.FirstOccurrence(rule, Wednesday, Before)!.Value;

        TriggerReconciler.AdvancePastFiring(trigger, trigger.TriggerAt.AddMinutes(1));

        Assert.Equal(DayOfWeek.Monday, trigger.TriggerAt.DayOfWeek);
        Assert.Equal(D(2026, 6, 15), trigger.TriggerAt);
    }
}
