using System.ComponentModel;
using System.IO;
using Microsoft.Win32;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// Restore and Reset as the Settings screen runs them (after its confirmations): every setting,
/// one save, and Start with Windows left alone. No window is shown; files stay in the test's temp
/// folder, and the Run key is checked unchanged.
[Collection(nameof(SettingsScreen))]
public sealed class RestoreResetTests : IDisposable
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.1.0", "settings.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly object? _runValueBefore = ReadRunValue();

    public RestoreResetTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Nothing reached the desktop or the registry: no window, and the Run key is as it was.
        UiThread.Invoke(() => Assert.Empty(System.Windows.Application.Current.Windows));
        Assert.Equal(_runValueBefore, ReadRunValue());
    }

    [Fact]
    public void Restoring_settings_copies_every_one_saves_once_and_leaves_Start_with_Windows() => UiThread.Invoke(() =>
    {
        var current = new AppSettings { LaunchOnWindowsStartup = false };
        var vm = Ready(current);
        var restored = new SettingsRepository(FixturePath).Load();
        var savedMidway = WatchForSavesMidway(current);

        vm.ApplySettingsFrom(restored);

        Assert.False(savedMidway());
        foreach (var p in AppSettings.CopiedSettings)
            Assert.True(Equals(p.GetValue(restored), p.GetValue(current)), p.Name);
        Assert.Equal(30, current.AutoDeleteSpentAfterDays);
        Assert.False(current.LaunchOnWindowsStartup);

        // Saved once, at the end, with everything in it.
        var onDisk = new SettingsRepository(AppPaths.SettingsFile).Load();
        Assert.Equal(30, onDisk.AutoDeleteSpentAfterDays);
        Assert.Equal("Ctrl+Alt+P", onDisk.QuickAddHotkeyGesture);
        Assert.False(onDisk.LaunchOnWindowsStartup);
    });

    [Fact]
    public void Reset_gives_defaults_for_every_setting_but_leaves_Start_with_Windows() => UiThread.Invoke(() =>
    {
        var current = new SettingsRepository(FixturePath).Load();
        current.LaunchOnWindowsStartup = false;
        var vm = Ready(current);
        var savedMidway = WatchForSavesMidway(current);

        vm.ApplySettingsFrom(new AppSettings());

        Assert.False(savedMidway());
        var defaults = new AppSettings();
        foreach (var p in AppSettings.CopiedSettings)
            Assert.True(Equals(p.GetValue(defaults), p.GetValue(current)), p.Name);
        Assert.False(current.LaunchOnWindowsStartup);  // the default is on: Reset doesn't turn it on
        Assert.Equal(0, new SettingsRepository(AppPaths.SettingsFile).Load().AutoDeleteSpentAfterDays);
    });

    private static SettingsViewModel Ready(AppSettings settings)
    {
        AppServices.Instance.UseSettingsForTests(settings, saveOnChange: true);
        var vm = new SettingsViewModel();
        // Building the screen can pick a voice or model when none is set, which saves: start clean.
        File.Delete(AppPaths.SettingsFile);
        return vm;
    }

    /// True afterwards if settings.json was written while the settings were still changing.
    private static Func<bool> WatchForSavesMidway(AppSettings settings)
    {
        var saved = false;
        PropertyChangedEventHandler check = (_, _) => saved |= File.Exists(AppPaths.SettingsFile);
        settings.PropertyChanged += check;
        return () =>
        {
            settings.PropertyChanged -= check;
            return saved;
        };
    }

    private static object? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue("Pendulum");
    }
}
