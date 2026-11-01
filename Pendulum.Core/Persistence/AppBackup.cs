using System.Text.Json;
using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

/// A full snapshot of the app's user data — settings and reminders together —
/// for moving the whole app to another computer. Distinct from a reminders-only
/// export, which only ever contains a List&lt;TriggerTimer&gt;.
public sealed class AppBackup
{
    public AppSettings? Settings { get; set; }
    public List<TriggerTimer> Triggers { get; set; } = new();
}

public static class BackupRepository
{
    /// Writes a backup to a file the user chose: no .bak is left beside it.
    public static void Save(string path, AppBackup backup) => JsonStore.Save(path, backup, keepBackup: false);

    /// Reads a backup the user chose to restore, or null when the file isn't one. The file is
    /// never moved or changed. A backup names both its settings and its reminders (every backup
    /// since 1.0.0 does), so another JSON file, such as a settings.json or a reminders export,
    /// can't be taken for an empty backup and wipe the reminders.
    public static AppBackup? Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty(nameof(AppBackup.Settings), out var settings)
                    || !PendulumFileShape.IsSettings(settings)
                    || !root.TryGetProperty(nameof(AppBackup.Triggers), out var triggers)
                    || !PendulumFileShape.IsReminderList(triggers))
                    return null;
            }

            var backup = JsonStore.DeserializeUserJson<AppBackup>(json);
            if (backup is not null)
            {
                // A backup can come from anywhere: sounds, voices and models must be plain file
                // names in Pendulum's own folders.
                DataFileName.Clean(backup.Triggers);
                if (backup.Settings is not null)
                    DataFileName.Clean(backup.Settings);
            }

            return backup;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
