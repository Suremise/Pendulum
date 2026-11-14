using System.Text.Json;

namespace Pendulum.Core.Persistence;

public static class BackupRepository
{
    /// Writes a backup to a file the user chose: no .bak is left beside it.
    public static void Save(string path, AppBackup backup) => JsonStore.Save(path, backup, keepBackup: false);

    /// Reads a full backup without moving or changing it. Both settings and reminders are
    /// required so a settings file or reminders export cannot wipe the user's reminders.
    /// InvalidDataException means an invalid backup; file-access errors pass through.
    public static AppBackup Load(string path)
    {
        var backup = JsonStore.ReadUserFile<AppBackup>(path, root =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(nameof(AppBackup.Settings), out var settings)
            && PendulumFileShape.IsSettings(settings)
            && root.TryGetProperty(nameof(AppBackup.Triggers), out var triggers)
            && PendulumFileShape.IsReminderList(triggers));
        // Sounds, voices and models must be plain file names in Pendulum's own folders.
        DataFileName.Clean(backup.Triggers);
        if (backup.Settings is not null)
            DataFileName.Clean(backup.Settings);
        return backup;
    }
}
