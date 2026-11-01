using System.Text;
using System.Text.Json;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Persistence;

/// What happened when one of Pendulum's own data files was loaded.
public enum StoreOutcome
{
    /// There was no file (a first run): the caller uses its defaults.
    Missing,
    Loaded,
    /// The file couldn't be read as JSON, so it was set aside and the copy from the save before
    /// it (the .bak) was used.
    RecoveredFromBackup,
    /// The file couldn't be read as JSON and there was no good earlier copy: it was set aside and
    /// the caller uses its defaults.
    SetAside,
}

/// A loaded data file: its value (null for Missing and SetAside), what happened, and where a
/// damaged file was set aside.
public sealed record StoreLoad<T>(T? Value, StoreOutcome Outcome, string? SetAsidePath) where T : class;

/// One of Pendulum's data files couldn't be read at all (another program has it open, say).
/// Nothing was changed: the caller must stop rather than start empty and save over it.
public sealed class StoreUnreadableException(string path, Exception inner)
    : IOException(string.Format(CoreStrings.File_Unreadable, path), inner)
{
    public string Path { get; } = path;
}

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// How long a read that fails with an I/O error waits before each retry (antivirus or a
    /// backup tool can hold a file for a moment at sign-in). Tests set it to nothing.
    internal static TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

    /// The copy kept from the save before the last one, beside the file.
    public static string BackupPath(string path) => path + ".bak";

    /// Loads one of Pendulum's own data files, or null when there's none or it was set aside.
    /// Throws StoreUnreadableException when it can't be read at all. LoadWithOutcome says which.
    public static T? Load<T>(string path) where T : class => LoadWithOutcome<T>(path).Value;

    /// Loads one of Pendulum's own data files (settings.json, triggers.json):
    /// - a file that isn't valid JSON (an empty one left by a power cut, say) is set aside as
    ///   {name}.corrupt-{time}, unchanged, and the .bak from the save before is used if it's good;
    ///   a damaged .bak is kept aside the same way, never ignored;
    /// - a file (or its .bak) that can't be read at all is retried, then StoreUnreadableException
    ///   is thrown, with nothing moved or changed.
    public static StoreLoad<T> LoadWithOutcome<T>(string path) where T : class
    {
        RemoveLeftoverTempFiles(path);

        var backupPath = BackupPath(path);
        if (!File.Exists(path))
        {
            // A save that stopped between keeping the .bak and putting the new file in place.
            if (!File.Exists(backupPath))
                return new StoreLoad<T>(null, StoreOutcome.Missing, null);
            var fromBackup = TryDeserialize<T>(ReadWithRetries(backupPath));
            return fromBackup is null
                ? new StoreLoad<T>(null, StoreOutcome.SetAside, SetAside(backupPath))
                : new StoreLoad<T>(fromBackup, StoreOutcome.RecoveredFromBackup, null);
        }

        var json = ReadWithRetries(path);
        if (TryDeserialize<T>(json) is { } value)
            return new StoreLoad<T>(value, StoreOutcome.Loaded, null);

        // Read the .bak before moving anything: if it can't be read at all, this throws with both
        // files still in place.
        var backupJson = File.Exists(backupPath) ? ReadWithRetries(backupPath) : null;
        var setAside = SetAside(path);
        var backup = backupJson is null ? null : TryDeserialize<T>(backupJson);
        if (backup is not null)
            return new StoreLoad<T>(backup, StoreOutcome.RecoveredFromBackup, setAside);

        // Both damaged: the .bak is kept aside too, so no later save replaces it.
        if (backupJson is not null)
            SetAside(backupPath);
        return new StoreLoad<T>(null, StoreOutcome.SetAside, setAside);
    }

    /// Reads a file the user chose (a backup to restore, reminders to import). Unlike Load, a file
    /// it can't read is never moved or changed: it's the user's, wherever they keep it. Throws
    /// InvalidDataException when the file isn't JSON of the expected shape.
    public static T? ReadUserFile<T>(string path) where T : class =>
        DeserializeUserJson<T>(File.ReadAllText(path));

    /// The text of a file the user chose, read as <typeparamref name="T"/>: InvalidDataException
    /// when it isn't JSON of that shape.
    internal static T? DeserializeUserJson<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(CoreStrings.File_NotPendulumFormat, e);
        }
    }

    /// Saves durably: the JSON goes to a temp file of its own, flushed to the disk, which then
    /// takes the file's place in one step. With keepBackup (Pendulum's own data files), the file
    /// it replaces is kept as the .bak, so a save that a power cut damages still leaves the one
    /// before it. A file the user chose (an export) gets no .bak beside it.
    public static void Save<T>(string path, T value, bool keepBackup = true)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options));
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (keepBackup && File.Exists(path))
                File.Replace(tempPath, path, BackupPath(path), ignoreMetadataErrors: true);
            else
                File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static string ReadWithRetries(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= RetryDelays.Length)
                    throw new StoreUnreadableException(path, e);
                Thread.Sleep(RetryDelays[attempt]);
            }
        }
    }

    /// The value, or null when the text isn't JSON of the type (or is JSON null).
    private static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// Moves a damaged file aside, unchanged. If it can't be moved, nothing changes and the
    /// caller stops: running on without moving it would let the next save overwrite it.
    private static string SetAside(string path)
    {
        var setAside = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        for (var n = 2; File.Exists(setAside); n++)
            setAside = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{n}";
        try
        {
            File.Move(path, setAside, overwrite: false);
            return setAside;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new StoreUnreadableException(path, e);
        }
    }

    /// Temp files a save left behind when it was cut off: they're never read.
    private static void RemoveLeftoverTempFiles(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        foreach (var temp in Directory.EnumerateFiles(dir, Path.GetFileName(path) + ".*.tmp"))
            TryDelete(temp);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
