using System.Text.Json;
using Pendulum.Core.Models;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Persistence;

public class TriggerRepository
{
    public List<TriggerTimer> Load() =>
        JsonStore.Load<List<TriggerTimer>>(AppPaths.TriggersFile) ?? new List<TriggerTimer>();

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

    public void SaveTo(string path, IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(path, timers.ToList());
}
