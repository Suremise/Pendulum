using System.Text.Json;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Persistence;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception)
        {
            // A malformed file here would otherwise throw uncaught up through app startup and
            // take the whole app down over one bad file. Quarantine it instead of overwriting it
            // silently, and let the caller fall back to its default (fresh settings/empty list).
            QuarantineCorruptFile(path);
            return null;
        }
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

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(value, Options);

        // Write to a temp file and rename into place rather than writing the live path directly —
        // File.Move with overwrite is effectively atomic on the same volume, so a crash or power
        // loss mid-write can only ever corrupt the temp file, never the one callers actually read.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            var quarantinePath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, quarantinePath, overwrite: true);
        }
        catch
        {
            // best-effort — if even this fails, Load still returns null rather than throwing.
        }
    }
}
