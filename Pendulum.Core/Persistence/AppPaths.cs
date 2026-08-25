namespace Pendulum.Core.Persistence;

public static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;
    public static string DataDirectory => Path.Combine(BaseDirectory, "Data");
    public static string SoundsDirectory => Path.Combine(BaseDirectory, "Sounds");
    public static string WhisperModelsDirectory => Path.Combine(DataDirectory, "WhisperModels");
    public static string PiperModelsDirectory => Path.Combine(DataDirectory, "PiperModels");
    public static string TriggersFile => Path.Combine(DataDirectory, "triggers.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// Lives next to the exe, not in Data\ — the installer deletes it on every run (fresh
    /// install or update-over-existing), so its absence on launch means "an install just
    /// completed," distinct from Data\ existing/not existing.
    public static string PostInstallMarkerFile => Path.Combine(BaseDirectory, ".postinstall");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SoundsDirectory);
        Directory.CreateDirectory(WhisperModelsDirectory);
        Directory.CreateDirectory(PiperModelsDirectory);
    }
}
