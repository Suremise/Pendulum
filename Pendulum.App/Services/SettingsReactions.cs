using System.ComponentModel;
using Pendulum.Core.Models;

namespace Pendulum.App.Services;

/// The running app's one subscription for settings that affect the system or app shell.
/// Views edit settings without owning these reactions. Disposing detaches the subscription.
internal sealed class SettingsReactions : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action<ThemeMode> _applyTheme;
    private readonly Action<bool> _setStartup;
    private readonly Action _applyHotkey;

    public SettingsReactions(AppSettings settings, Action<ThemeMode> applyTheme, Action<bool> setStartup, Action applyHotkey)
    {
        _settings = settings;
        _applyTheme = applyTheme;
        _setStartup = setStartup;
        _applyHotkey = applyHotkey;
        // Theme resources must exist before the app builds any views. Startup registration is
        // only for an explicit change (first-launch registration has its own ownership rules),
        // and the initial hotkey is applied by App once its main-window handle is ready.
        _applyTheme(settings.Theme);
        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme):
                _applyTheme(_settings.Theme);
                break;
            case nameof(AppSettings.LaunchOnWindowsStartup):
                _setStartup(_settings.LaunchOnWindowsStartup);
                break;
            case nameof(AppSettings.QuickAddHotkeyEnabled):
            case nameof(AppSettings.QuickAddHotkeyGesture):
                _applyHotkey();
                break;
        }
    }

    public void Dispose() => _settings.PropertyChanged -= OnSettingsChanged;
}
