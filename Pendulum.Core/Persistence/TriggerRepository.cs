using System.Text.Json;
using Pendulum.Core.Models;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Persistence;

public class TriggerRepository
{
    private readonly string? _path;
    public TriggerRepository() { }
    public TriggerRepository(string path) => _path = path;
    public string FilePath => _path ?? AppPaths.TriggersFile;

    /// The reminders and how loading them went (set aside, or recovered from the save before).
    /// Throws StoreUnreadableException when the file can't be read at all.
    public StoreLoad<List<TriggerTimer>> LoadWithOutcome() => JsonStore.LoadWithOutcome<List<TriggerTimer>>(FilePath);

    public void Save(IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(FilePath, timers.ToList());

    /// Reads reminders from a file the user chose to import. The file is never moved or changed.
    /// A sound that isn't a plain file name in Sounds\ is dropped: that reminder plays none.
    /// Throws InvalidDataException when it doesn't hold a list of Pendulum reminders.
    public List<TriggerTimer> LoadFrom(string path)
    {
        var reminders = JsonStore.ReadUserFile<List<TriggerTimer>>(path, CheckShape);
        DataFileName.Clean(reminders);
        return reminders;

        static bool CheckShape(JsonElement root) => PendulumFileShape.IsReminderList(root)
            ? true : throw new InvalidDataException(CoreStrings.File_NotReminderList);
    }

    /// Exports to a file the user chose: no .bak is left beside it.
    public void SaveTo(string path, IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(path, timers.ToList(), keepBackup: false);
}
