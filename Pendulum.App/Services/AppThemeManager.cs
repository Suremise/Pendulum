using System.Windows.Media;
using Microsoft.Win32;
using Pendulum.Core.Models;
using Wpf.Ui.Appearance;

namespace Pendulum.App.Services;

/// Applies the app's Light/Dark/System theme setting and re-asserts the custom
/// purple accent color, since switching the base WPF-UI theme can otherwise
/// reset accent brushes back to the Windows default.
internal static class AppThemeManager
{
    public static void Apply(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => IsSystemInDarkMode() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(theme);

        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#7C6AE0")!,
            (Color)ColorConverter.ConvertFromString("#8F7FF0")!,
            (Color)ColorConverter.ConvertFromString("#6552D0")!,
            (Color)ColorConverter.ConvertFromString("#4C3BAE")!);
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }
}
