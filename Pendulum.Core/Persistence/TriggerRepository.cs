using System.Text.Json;
using Pendulum.Core.Models;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Persistence;

public class TriggerRepository
{
    public List<TriggerTimer> Load() => LoadWithOutcome().Value ?? new List<TriggerTimer>();

    /// The reminders and how loading them went (set aside, or recovered from the save before).
    /// Throws StoreUnreadableException when the file can't be read at all.
    public StoreLoad<List<TriggerTimer>> LoadWithOutcome() => JsonStore.LoadWithOutcome<List<TriggerTimer>>(AppPaths.TriggersFile);

    public void Save(IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(AppPaths.TriggersFile, timers.ToList());

    /// Reads reminders from a file the user chose to import. The file is never moved or changed.
    /// Throws InvalidDataException when it doesn't hold a list of Pendulum reminders.
    public List<TriggerTimer> LoadFrom(string path)
    {
        var json = File.ReadAllText(path);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!PendulumFileShape.IsReminderList(doc.RootElement))
                throw new InvalidDataException(CoreStrings.File_NotReminderList);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(CoreStrings.File_NotPendulumFormat, e);
        }

        return JsonStore.DeserializeUserJson<List<TriggerTimer>>(json)
            ?? throw new InvalidDataException(CoreStrings.File_NotReminderList);
    }

    /// Exports to a file the user chose: no .bak is left beside it.
    public void SaveTo(string path, IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(path, timers.ToList(), keepBackup: false);
}
