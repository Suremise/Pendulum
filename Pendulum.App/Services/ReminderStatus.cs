using Pendulum.App.Resources;

namespace Pendulum.App.Services;

/// Formats a one-line "what's coming up next" summary, shared by the main window's
/// status bar and the tray icon's right-click menu.
public static class ReminderStatus
{
    public static string GetNextReminderSummary()
    {
        var next = AppServices.Instance.Triggers
            .Where(t => t.Enabled && !t.HasFired)
            .OrderBy(t => t.TriggerAt)
            .FirstOrDefault();

        if (next is null)
            return Strings.Status_NoUpcoming;

        var remaining = next.TriggerAt - DateTime.Now;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return string.Format(Strings.Status_Next, next.Name, FormatDuration(remaining));
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
