using Pendulum.App.Resources;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.App.Services;

/// Formats a one-line "what's coming up next" summary, shared by the main window's
/// status bar and the tray icon's tooltip, using the same cached next reminder.
public static class ReminderStatus
{
    private static readonly NextReminderTracker Tracker = new(AppServices.Instance.Triggers);

    public static event Action Changed
    {
        add => Tracker.Changed += value;
        remove => Tracker.Changed -= value;
    }

    public static string GetNextReminderSummary() => FormatSummary(Tracker.Next, DateTime.Now);

    internal static string FormatSummary(TriggerTimer? next, DateTime now)
    {

        if (next is null)
            return Strings.Status_NoUpcoming;

        var remaining = next.TriggerAt - now;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return string.Format(Strings.Status_Next, next.Name, FormatDuration(remaining));
    }

    internal static TimeSpan? GetNextUpdateDelay(DateTime now) => GetNextUpdateDelay(Tracker.Next, now);

    /// Seconds only when the displayed duration includes them. Otherwise wake on the minute,
    /// or just as the duration drops below a day and starts showing seconds.
    internal static TimeSpan? GetNextUpdateDelay(TriggerTimer? next, DateTime now)
    {
        if (next is null || next.TriggerAt <= now)
            return null;

        var remaining = next.TriggerAt - now;
        if (remaining < TimeSpan.FromDays(1))
            return TimeSpan.FromTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond);

        var minute = TimeSpan.FromTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
        var secondsBegin = remaining - TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(1);
        return minute < secondsBegin ? minute : secondsBegin;
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalDays >= 1)
            return string.Format(Strings.Duration_DaysHoursMinutes, (int)t.TotalDays, t.Hours, t.Minutes);
        if (t.TotalHours >= 1)
            return string.Format(Strings.Duration_HoursMinutesSeconds, (int)t.TotalHours, t.Minutes, t.Seconds);
        if (t.TotalMinutes >= 1)
            return string.Format(Strings.Duration_MinutesSeconds, (int)t.TotalMinutes, t.Seconds);
        return string.Format(Strings.Duration_Seconds, t.Seconds);
    }
}
