using System.Reflection;
using System.Text.Json;
using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

/// Tells Pendulum's own reminders and settings from other JSON before a file the user chose is
/// used, so another app's file can't be imported as reminders named "Timer" or restored over the
/// user's real ones. Every file Pendulum has written since 1.0.0 passes.
internal static class PendulumFileShape
{
    private static readonly HashSet<string> SettingNames = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// An array whose every element is a reminder: an object with a string Name and a date
    /// TriggerAt (both saved for every reminder since 1.0.0), and a Recurrence that, if there,
    /// is an object or null. An empty array passes: an export with no reminders.
    public static bool IsReminderList(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array && element.EnumerateArray().All(IsReminder);

    /// An object naming at least one of Pendulum's settings (a backup saves all of them).
    public static bool IsSettings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Any(p => SettingNames.Contains(p.Name));

    private static bool IsReminder(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty(nameof(TriggerTimer.Name), out var name) || name.ValueKind != JsonValueKind.String)
            return false;

        if (!element.TryGetProperty(nameof(TriggerTimer.TriggerAt), out var at)
            || at.ValueKind != JsonValueKind.String || !at.TryGetDateTime(out _))
            return false;

        return !element.TryGetProperty(nameof(TriggerTimer.Recurrence), out var recurrence)
            || recurrence.ValueKind is JsonValueKind.Object or JsonValueKind.Null;
    }
}
