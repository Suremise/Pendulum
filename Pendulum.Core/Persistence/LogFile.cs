namespace Pendulum.Core.Persistence;

/// A text log that can't grow for ever: once an entry would take it past its limit, the log so
/// far becomes "<name>.old" (replacing the one before) and a new log starts. So at most about
/// twice the limit is ever kept, and the newest entries are always there.
public static class LogFile
{
    /// The size a log may reach before it rolls over: plenty for the crash log's occasional entry.
    public const long DefaultLimitBytes = 1024 * 1024;

    /// The file a log rolls over into.
    public static string OldPath(string path) => path + ".old"; // not user-visible: a file name

    /// Adds <paramref name="entry"/> to the log at <paramref name="path"/>, rolling it over first
    /// when the entry would take it past <paramref name="limitBytes"/>.
    public static void Append(string path, string entry, long limitBytes = DefaultLimitBytes)
    {
        var file = new FileInfo(path);
        if (file.Exists && file.Length > 0 && file.Length + System.Text.Encoding.UTF8.GetByteCount(entry) > limitBytes)
            File.Move(path, OldPath(path), overwrite: true);

        File.AppendAllText(path, entry);
    }
}
