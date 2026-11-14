using System.IO;
using System.Text.RegularExpressions;

namespace Pendulum.App.Tests;

/// Check the startup call without creating App (which would wake the installed copy) or
/// creating a tray icon and changing the test runner's process-wide scheduling policy.
public sealed class TrayPriorityTests
{
    [Fact]
    public void Creating_the_tray_icon_explicitly_leaves_efficiency_mode_off()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Pendulum.App", "Services", "TrayIcon.cs"));
        source = Regex.Replace(source, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
        var calls = Regex.Matches(source, @"\b_trayIcon\.ForceCreate\s*\(([^)]*)\)");

        var call = Assert.Single(calls.Cast<Match>());
        Assert.Equal("enablesEfficiencyMode:false", Regex.Replace(call.Groups[1].Value, @"\s+", ""));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pendulum.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Pendulum.sln not found above the test's folder.");
    }
}
