using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Pendulum.Core.Models;
// WPF has its own System.Windows.ThemeMode since .NET 9: the settings' one is meant here.
using ThemeMode = Pendulum.Core.Models.ThemeMode;
using Wpf.Ui.Appearance;

namespace Pendulum.App.Services;

/// Applies the app's Light/Dark/System theme setting and re-asserts the custom
/// purple accent color, since switching the base WPF-UI theme can otherwise
/// reset accent brushes back to the Windows default.
internal static class AppThemeManager
{
    /// The resource key of the brush links are drawn in (App.xaml's Hyperlink style).
    public const string LinkBrushKey = "PendulumLinkBrush";

    // WPF's default link blue is 2.5:1 on the dark theme's cards. These keep links at 4.5:1 or
    // more on every surface of their theme: #A99CF5 is 5.3:1 on dark cards (#323232) and more on
    // the window; #4C3BAE is 7.1:1 on light ones (#EEEEEE) and more on the window.
    private const string LinkOnDark = "#A99CF5";
    private const string LinkOnLight = "#4C3BAE";

    public static void Apply(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => IsSystemInDarkMode() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(theme);

        var link = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme == ApplicationTheme.Dark ? LinkOnDark : LinkOnLight)!);
        link.Freeze();
        Application.Current.Resources[LinkBrushKey] = link;

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
