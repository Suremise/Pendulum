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
    public static void Save(string path, AppBackup backup) => JsonStore.Save(path, backup);

    public static AppBackup? Load(string path) => JsonStore.Load<AppBackup>(path);
}
