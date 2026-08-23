using System.IO;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Services;

/// Best-effort crash/error logging so failures are never completely silent.
/// Never throws itself, even if the Data directory is unavailable.
public static class CrashLog
{
    public static void Write(string context, Exception ex)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var path = Path.Combine(AppPaths.DataDirectory, "crash.log");
            var entry = $"{DateTime.Now:O} [{context}] {ex}\n---\n";
            File.AppendAllText(path, entry);
        }
        catch
        {
            // logging must never itself crash the app.
        }
    }
}
