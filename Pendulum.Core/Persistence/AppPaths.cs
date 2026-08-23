namespace Pendulum.Core.Persistence;

public static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;
    public static string DataDirectory => Path.Combine(BaseDirectory, "Data");
    public static string SoundsDirectory => Path.Combine(BaseDirectory, "Sounds");
    public static string TriggersFile => Path.Combine(DataDirectory, "triggers.json");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SoundsDirectory);
    }
}
