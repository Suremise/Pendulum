using System.IO;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class SettingsReactionsTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-reactions-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsReactionsTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        UiThread.Invoke(() => AppServices.Instance.FlushPendingSaves());
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Two_settings_panels_edit_values_without_app_side_effects()
    {
        UiThread.Invoke(() =>
        {
            var settings = new AppSettings { Theme = ThemeMode.Dark, LaunchOnWindowsStartup = false };
            AppServices.Instance.UseSettingsForTests(settings);
            var themes = new List<ThemeMode>();
            var startup = new List<bool>();
            AppThemeManager.ApplyOverride = themes.Add;
            StartupRegistration.SetEnabledOverride = startup.Add;
            try
            {
                var first = new SettingsPanel();
                var second = new SettingsPanel();
                settings.Theme = ThemeMode.Light;
                settings.LaunchOnWindowsStartup = true;
                Assert.Empty(themes);
                Assert.Empty(startup);
                Assert.Same(settings, ((SettingsViewModel)first.DataContext).Settings);
                Assert.Same(settings, ((SettingsViewModel)second.DataContext).Settings);
            }
            finally
            {
                AppThemeManager.ApplyOverride = null;
                StartupRegistration.SetEnabledOverride = null;
                AppServices.Instance.UseSettingsForTests(new AppSettings());
            }
        });
    }

    [Fact]
    public void App_wiring_applies_changes_once_and_disposal_detaches_it()
    {
        UiThread.Invoke(() =>
        {
            var settings = new AppSettings
            {
                Theme = ThemeMode.Dark,
                LaunchOnWindowsStartup = false,
                QuickAddHotkeyEnabled = true,
                QuickAddHotkeyGesture = "Ctrl+Alt+R",
            };
            AppServices.Instance.UseSettingsForTests(settings);
            var themes = new List<ThemeMode>();
            var startup = new List<bool>();
            var hotkeys = 0;
            AppThemeManager.ApplyOverride = themes.Add;
            StartupRegistration.SetEnabledOverride = startup.Add;
            try
            {
                using var reactions = new SettingsReactions(settings, AppThemeManager.Apply, StartupRegistration.SetEnabled, () => hotkeys++);
                _ = new SettingsPanel();
                _ = new SettingsPanel();
                Assert.Equal([ThemeMode.Dark], themes);
                Assert.Empty(startup); // Merely starting a copy must not change the Run entry.
                Assert.Equal(0, hotkeys); // Initial registration waits for App's window handle.

                settings.Theme = ThemeMode.Light;
                settings.LaunchOnWindowsStartup = true;
                settings.QuickAddHotkeyEnabled = false;
                settings.QuickAddHotkeyGesture = "Ctrl+Alt+Q";
                settings.Use24HourTime = !settings.Use24HourTime;
                Assert.Equal([ThemeMode.Dark, ThemeMode.Light], themes);
                Assert.Equal([true], startup);
                Assert.Equal(2, hotkeys);

                reactions.Dispose();
                settings.Theme = ThemeMode.Dark;
                settings.LaunchOnWindowsStartup = false;
                settings.QuickAddHotkeyEnabled = true;
                Assert.Equal(2, themes.Count);
                Assert.Single(startup);
                Assert.Equal(2, hotkeys);
            }
            finally
            {
                AppThemeManager.ApplyOverride = null;
                StartupRegistration.SetEnabledOverride = null;
                AppServices.Instance.UseSettingsForTests(new AppSettings());
            }
        });
    }

    [Fact]
    public void Restore_and_reset_keep_using_the_apps_settings_reactions()
    {
        UiThread.Invoke(() =>
        {
            var settings = new AppSettings { Theme = ThemeMode.Dark, LaunchOnWindowsStartup = false };
            AppServices.Instance.UseSettingsForTests(settings);
            var themes = new List<ThemeMode>();
            var startup = new List<bool>();
            using var reactions = new SettingsReactions(settings, themes.Add, startup.Add, () => { });
            var vm = new SettingsViewModel();

            vm.ApplySettingsFrom(new AppSettings { Theme = ThemeMode.Light, LaunchOnWindowsStartup = true });
            Assert.Equal([ThemeMode.Dark, ThemeMode.Light], themes);
            var defaults = new AppSettings();
            vm.ApplySettingsFrom(defaults);
            Assert.Equal([ThemeMode.Dark, ThemeMode.Light, defaults.Theme], themes);
            Assert.False(settings.LaunchOnWindowsStartup);
            Assert.Empty(startup); // Restore and reset preserve this copy's startup preference.
        });
    }
}
