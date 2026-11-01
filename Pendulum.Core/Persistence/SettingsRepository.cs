using Pendulum.Core.Models;

namespace Pendulum.Core.Persistence;

public class SettingsRepository
{
    private readonly string? _path;

    public SettingsRepository()
    {
    }

    /// For tests: a settings file somewhere other than the app's own Data folder.
    internal SettingsRepository(string path) => _path = path;

    private string Path => _path ?? AppPaths.SettingsFile;

    public AppSettings Load() =>
        JsonStore.Load<AppSettings>(Path) ?? new AppSettings();

    public void Save(AppSettings settings) =>
        JsonStore.Save(Path, settings);
}
