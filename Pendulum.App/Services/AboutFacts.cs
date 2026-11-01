using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pendulum.App.Services;

/// One row of About's table.
public sealed record AboutFact(string Label, string Value);

/// What About says of this copy of Pendulum: its version, when it was built, the .NET runtime
/// and Windows. The same facts go on the clipboard with About's Copy, for a bug report.
public sealed class AboutFacts
{
    private const string Unknown = "Unknown";

    public required string Version { get; init; }
    public string? Built { get; init; }
    public string? DotNet { get; init; }
    public string? Windows { get; init; }

    public IReadOnlyList<AboutFact> Rows =>
    [
        new("Version", Version),
        new("Built", Built ?? Unknown),
        new(".NET", DotNet ?? Unknown),
        new("Windows", Windows ?? Unknown),
    ];

    /// "Pendulum 1.1.0", then the table's other rows, one a line.
    public string CopyText()
    {
        var lines = new List<string> { $"Pendulum {Version}" };
        lines.AddRange(Rows.Skip(1).Select(row => $"{row.Label}: {row.Value}"));
        return string.Join("\r\n", lines) + "\r\n";
    }

    public static AboutFacts Gather()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;

        return new AboutFacts
        {
            Version = version is null ? Unknown : $"{version.Major}.{version.Minor}.{version.Build}",
            Built = BuiltAt(assembly),
            DotNet = Environment.Version.ToString(),
            Windows = WindowsVersion(),
        };
    }

    private static string? BuiltAt(Assembly assembly)
    {
        try
        {
            return File.GetLastWriteTime(assembly.Location).ToString("dd MMM yyyy, HH:mm");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// "Windows 11 24H2 (build 26200.6899)": Windows 11 from build 22000 on, 10 before it.
    private static string? WindowsVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var release = key?.GetValue("DisplayVersion") as string;
            var revision = key?.GetValue("UBR") is int ubr ? ubr : (int?)null;
            return FormatWindows(Environment.OSVersion.Version.Build, release, revision);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return FormatWindows(Environment.OSVersion.Version.Build, null, null);
        }
    }

    public static string FormatWindows(int build, string? release, int? revision)
    {
        var major = build >= 22000 ? "11" : "10";
        var buildText = revision is null ? build.ToString() : $"{build}.{revision}";
        return string.IsNullOrWhiteSpace(release)
            ? $"Windows {major} (build {buildText})"
            : $"Windows {major} {release} (build {buildText})";
    }
}
