using System.IO;
using Microsoft.Win32;
using Pendulum.Core.Startup;

namespace Pendulum.App.Services;

/// Opt-in "launch on Windows startup" toggle. Writes a Registry Run value pointing at the exe's
/// current path — the one deliberate registry touch the app makes; everything else lives in its
/// own Data\ folder. StartupEntry decides when this copy may change it.
public static class StartupRegistration
{
    private static StartupEntry Entry => new(new RunValueStore(), Environment.ProcessPath ?? string.Empty);

    /// The user turned the setting on or off.
    public static void SetEnabled(bool enabled)
    {
        if (!string.IsNullOrEmpty(Environment.ProcessPath))
            Entry.SetEnabled(enabled);
    }

    /// A first launch: registers an installed copy unless another copy's entry is already there.
    /// Returns whether Windows now starts this copy.
    public static bool RegisterOnFirstLaunch() =>
        !string.IsNullOrEmpty(Environment.ProcessPath) && Entry.RegisterOnFirstLaunch(IsInstalledCopy());

    public static bool IsCurrentlyEnabled() => Entry.IsEnabled;

    /// The installer puts its uninstaller (unins000.exe) beside the exe; a build run from bin\, a
    /// portable copy or a test folder has none.
    private static bool IsInstalledCopy()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        return dir is not null && Directory.EnumerateFiles(dir, "unins*.exe").Any();
    }

    private sealed class RunValueStore : IStartupValueStore
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Pendulum";  // not user-visible: the Run key's value name

        public string? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }

        public void Write(string commandLine)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, commandLine);
        }

        public void Delete()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
