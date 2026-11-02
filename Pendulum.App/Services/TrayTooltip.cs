using Pendulum.App.Resources;

namespace Pendulum.App.Services;

/// The tray icon's tooltip: "Pendulum - <next reminder>", then the Quick Reminder shortcut on a
/// line of its own while it's on and held, so it's to hand without opening Settings.
internal static class TrayTooltip
{
    /// The most a tray tooltip holds (NOTIFYICONDATA's szTip, 128 UTF-16 units with its end).
    public const int MaxLength = 127;

    /// <param name="status">The next reminder, as the status bar says it.</param>
    /// <param name="shortcut">The Quick Reminder shortcut held now, or null when it's off or
    /// couldn't be registered.</param>
    public static string Build(string status, string? shortcut)
    {
        var lines = string.IsNullOrWhiteSpace(shortcut)
            ? []
            : new[] { string.Format(Strings.Tray_ToolTipShortcut, shortcut) };
        return FitLines(string.Format(Strings.Tray_ToolTipWithStatus, status), Strings.App_Name, lines, MaxLength);
    }

    /// <paramref name="first"/>, then each of <paramref name="lines"/> on a line of its own while
    /// the whole stays within <paramref name="max"/>. No line is ever cut: a first line too long
    /// on its own gives way to <paramref name="name"/>, and a line that doesn't fit is left out
    /// with every one after it.
    internal static string FitLines(string first, string name, IEnumerable<string> lines, int max)
    {
        var text = first.Length <= max ? first : name;
        foreach (var line in lines)
        {
            if (text.Length + 1 + line.Length > max)
                break;
            text += "\n" + line;
        }
        return text;
    }
}
