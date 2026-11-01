namespace Pendulum.Core.Startup;

/// Where the "launch on Windows startup" command line is kept: the app's is the registry's
/// HKCU Run value; tests use one of their own, so no test touches the real one.
public interface IStartupValueStore
{
    /// The command line Windows runs at sign-in, or null when there is none.
    string? Read();
    void Write(string commandLine);
    void Delete();
}

/// The "launch on Windows startup" entry, as seen from one copy of Pendulum. One entry serves
/// every copy on the PC, so a copy only ever replaces or removes it on the user's say-so, or
/// when it's already this copy's: a second copy (a portable one, a build being tried out) never
/// takes over or clears the entry an installed Pendulum made.
public sealed class StartupEntry
{
    private readonly IStartupValueStore _store;
    private readonly string _exePath;

    public StartupEntry(IStartupValueStore store, string exePath)
    {
        _store = store;
        _exePath = exePath;
    }

    /// Whether Windows starts any copy of Pendulum at sign-in.
    public bool IsEnabled => _store.Read() is not null;

    /// Whether the entry starts this copy.
    public bool PointsHere => _store.Read() is { } value && PointsAt(value, _exePath);

    /// A fresh install's first launch: register this copy, matching the setting's default, but
    /// only for an installed copy, and only if no other copy's entry is there already. Returns
    /// whether the entry now starts this copy, for the setting to show.
    public bool RegisterOnFirstLaunch(bool isInstalledCopy)
    {
        var current = _store.Read();
        if (isInstalledCopy && (current is null || PointsAt(current, _exePath)))
            _store.Write(CommandLine);

        return PointsHere;
    }

    /// The user turned the setting on or off. On makes the entry start this copy. Off removes it
    /// only if it's this copy's: another copy's entry is that copy's to turn off.
    public void SetEnabled(bool enabled)
    {
        if (enabled)
            _store.Write(CommandLine);
        else if (PointsHere)
            _store.Delete();
    }

    private string CommandLine => $"\"{_exePath}\"";

    /// Whether a Run command line starts the exe at <paramref name="exePath"/>: the quoted or bare
    /// path, compared as full paths, ignoring case, with any arguments after it ignored.
    public static bool PointsAt(string commandLine, string exePath)
    {
        var path = ExecutableOf(commandLine);
        if (path.Length == 0)
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// The executable a Run command line starts, or "" when it can't be told for sure: then no
    /// copy counts it as its own, so none replaces or removes it.
    private static string ExecutableOf(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.StartsWith('"'))
        {
            // Quoted: the whole quoted path, and only when the quote closes before an argument or the end.
            var end = text.IndexOf('"', 1);
            if (end < 0 || (end + 1 < text.Length && !char.IsWhiteSpace(text[end + 1])))
                return string.Empty;
            return text[1..end];
        }

        // Unquoted: up to the first ".exe" that ends a word (followed by a space or the end), the
        // one Windows runs, so a path with spaces still reads whole, and ".exe" inside a folder or
        // file name ("Pendulum.exe.old", "Pendulum.exe.backup\") doesn't end it early.
        for (var at = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(".exe", at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var after = at + 4;
            if (after == text.Length || char.IsWhiteSpace(text[after]))
                return text[..after];
        }

        return string.Empty;
    }
}
