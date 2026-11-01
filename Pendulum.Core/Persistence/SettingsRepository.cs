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

    public AppSettings Load() => LoadWithOutcome().Value ?? new AppSettings();

    /// The settings and how loading them went (set aside, or recovered from the save before).
    /// Throws StoreUnreadableException when the file can't be read at all.
    public StoreLoad<AppSettings> LoadWithOutcome() => JsonStore.LoadWithOutcome<AppSettings>(Path);

    public void Save(AppSettings settings) =>
        JsonStore.Save(Path, settings);
}
