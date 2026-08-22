using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

public class SettingsRepository
{
    public AppSettings Load() =>
        JsonStore.Load<AppSettings>(PortablePaths.SettingsFile) ?? new AppSettings();

    public void Save(AppSettings settings) =>
        JsonStore.Save(PortablePaths.SettingsFile, settings);
}
