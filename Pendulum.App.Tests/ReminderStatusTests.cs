using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

public sealed class ReminderStatusTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 15, 250);
    private static TriggerTimer Reminder(TimeSpan remaining) => new() { Name = "Dentist", TriggerAt = Now + remaining };

    [Theory]
    [InlineData(5, "5s")]
    [InlineData(65, "1m 5s")]
    [InlineData(3665, "1h 1m 5s")]
    [InlineData(90065, "1d 1h 1m")]
    public void Countdown_wording_stays_the_same(int seconds, string duration)
    {
        var reminder = Reminder(TimeSpan.FromSeconds(seconds));
        Assert.Equal(string.Format(Strings.Status_Next, reminder.Name, duration), ReminderStatus.FormatSummary(reminder, Now));
    }

    [Fact]
    public void No_reminder_or_an_overdue_reminder_needs_no_clock_ticks()
    {
        Assert.Null(ReminderStatus.GetNextUpdateDelay(null, Now));
        Assert.Equal(Strings.Status_NoUpcoming, ReminderStatus.FormatSummary(null, Now));
        var overdue = Reminder(TimeSpan.FromSeconds(-1));
        Assert.Null(ReminderStatus.GetNextUpdateDelay(overdue, Now));
        Assert.Equal(string.Format(Strings.Status_Next, overdue.Name, string.Format(Strings.Duration_Seconds, 0)),
            ReminderStatus.FormatSummary(overdue, Now));
    }

    [Fact]
    public void A_countdown_with_seconds_refreshes_on_the_next_second()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(750), ReminderStatus.GetNextUpdateDelay(Reminder(TimeSpan.FromHours(23)), Now));
    }

    [Fact]
    public void A_countdown_without_seconds_refreshes_on_the_next_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(44.75), ReminderStatus.GetNextUpdateDelay(Reminder(TimeSpan.FromDays(2)), Now));
    }

    [Fact]
    public void Crossing_below_a_day_starts_seconds_without_waiting_for_the_minute()
    {
        var reminder = Reminder(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(5));
        var delay = ReminderStatus.GetNextUpdateDelay(reminder, Now)!.Value;
        Assert.Equal(TimeSpan.FromMilliseconds(5001), delay);
        Assert.Equal(TimeSpan.FromMilliseconds(749), ReminderStatus.GetNextUpdateDelay(reminder, Now + delay));
    }
}
