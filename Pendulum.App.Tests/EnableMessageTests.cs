using System.Globalization;
using Pendulum.App.ViewModels;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

/// The message the reminders list shows after turning a reminder back on: built as text only,
/// never shown, so no window or dialog appears.
public sealed class EnableMessageTests
{
    private static readonly TriggerTimer PastDentist = new() { Name = "dentist", TriggerAt = new DateTime(2026, 8, 25, 14, 30, 0) };

    [Fact]
    public void A_past_one_off_shows_its_time_by_the_12_hour_setting()
    {
        var message = InCulture("en-GB", () =>
            TriggersViewModel.EnableMessage(TriggerReconciler.EnableOutcome.PastOneOff, PastDentist, use24Hour: false));

        Assert.Equal("\"dentist\" was due at Tue, 25 Aug 2026  2:30 pm, which has passed, so it stays off. Edit it to give it a new time.", message);
    }

    [Fact]
    public void A_past_one_off_shows_its_time_by_the_24_hour_setting()
    {
        var message = InCulture("en-GB", () =>
            TriggersViewModel.EnableMessage(TriggerReconciler.EnableOutcome.PastOneOff, PastDentist, use24Hour: true));

        Assert.Contains("Tue, 25 Aug 2026  14:30", message);
        Assert.DoesNotContain("pm", message);
    }

    [Fact]
    public void A_series_that_ended_names_the_reminder()
    {
        var message = InCulture("en-GB", () =>
            TriggersViewModel.EnableMessage(TriggerReconciler.EnableOutcome.SeriesEnded, PastDentist, use24Hour: true));

        Assert.StartsWith("\"dentist\" has no more times to go off", message);
    }

    [Theory]
    [InlineData(TriggerReconciler.EnableOutcome.Enabled)]
    [InlineData(TriggerReconciler.EnableOutcome.MovedOn)]
    public void Simply_going_back_on_says_nothing(TriggerReconciler.EnableOutcome outcome)
    {
        Assert.Null(TriggersViewModel.EnableMessage(outcome, PastDentist, use24Hour: true));
    }

    private static T InCulture<T>(string name, Func<T> work)
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return work();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
