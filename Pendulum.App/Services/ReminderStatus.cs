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
            return "No upcoming reminders";

        var remaining = next.TriggerAt - DateTime.Now;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return $"Next Reminder: {next.Name} in {FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalDays >= 1)
            return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }
}
