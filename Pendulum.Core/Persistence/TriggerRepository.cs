using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

public class TriggerRepository
{
    public List<TriggerTimer> Load() =>
        JsonStore.Load<List<TriggerTimer>>(PortablePaths.TriggersFile) ?? new List<TriggerTimer>();

    public void Save(IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(PortablePaths.TriggersFile, timers.ToList());

    public List<TriggerTimer> LoadFrom(string path) =>
        JsonStore.Load<List<TriggerTimer>>(path) ?? new List<TriggerTimer>();

    public void SaveTo(string path, IEnumerable<TriggerTimer> timers) =>
        JsonStore.Save(path, timers.ToList());
}
