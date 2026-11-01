using System.Text.Json;
using Pendulum.Core.Models;

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
                throw new InvalidDataException("The file doesn't hold a list of Pendulum reminders.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The file isn't in Pendulum's format.", e);
        }

        return JsonStore.DeserializeUserJson<List<TriggerTimer>>(json)
            ?? throw new InvalidDataException("The file doesn't hold a list of Pendulum reminders.");
    }

    public void SaveTo(string path, IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(path, timers.ToList());
}
