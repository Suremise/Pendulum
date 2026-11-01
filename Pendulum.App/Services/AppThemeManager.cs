using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Pendulum.Core.Models;
// WPF has its own System.Windows.ThemeMode since .NET 9: the settings' one is meant here.
using ThemeMode = Pendulum.Core.Models.ThemeMode;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Pendulum.App.Services;

/// Applies the app's Light/Dark/System theme setting and Pendulum's rose accent, the colour of
/// its icon (assets/README.md, Palette), with a set of shades for each theme.
internal static class AppThemeManager
{
    /// The resource key of the brush links are drawn in (App.xaml's Hyperlink style).
    public const string LinkBrushKey = "PendulumLinkBrush";  // not user-visible: resource keys

    /// The accent itself (the selected tab, the panel's frame, today in the calendar, sort
    /// arrows), and a soft tint of it (the selected tab's background).
    public const string AccentBrushKey = "PendulumAccentBrush";  // not user-visible
    public const string AccentSoftBrushKey = "PendulumAccentSoftBrush";  // not user-visible

    /// The accent for one theme. WPF-UI 3 uses `Fill` for primary buttons, toggles and checked
    /// boxes, `Hover` for a primary button under the pointer and its other accent fills, and
    /// `Text` for accent-coloured text; `System` is the selection highlight. On light, labels on
    /// the fill are white; on dark, black.
    private sealed record Accent(string System, string Fill, string Hover, string Text, string Link);

    // WCAG 2 contrast, checked against the themes' backgrounds (light #FAFAFA to #EEEEEE, dark
    // #202020 to #323232):
    // light: white on Fill 5.2:1 and on Hover 7.2:1; Fill on the backgrounds 4.5:1 or more;
    //        Text and Link 6.2:1 or more.
    // dark:  black on Fill 7.3:1 and on Hover 9.8:1; Fill on the backgrounds 4.5:1 or more;
    //        Text and Link 6.0:1 or more.
    private static readonly Accent OnLight = new(System: "#D23A66", Fill: "#C7305E", Hover: "#A3234B", Text: "#A3234B", Link: "#A3234B");
    private static readonly Accent OnDark = new(System: "#F0567F", Fill: "#F56A8F", Hover: "#FF8FB0", Text: "#FF8FB0", Link: "#FF8FB0");

    public static void Apply(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => IsSystemInDarkMode() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        var accent = theme == ApplicationTheme.Dark ? OnDark : OnLight;

        // The accent goes on twice. The theme's dictionary takes some brushes (a primary
        // button's fill) from the accent colours as it loads, so they're set before it; WPF-UI
        // sets others by the theme (labels on the fill), so they're set again after it. And
        // updateAccent: false keeps WPF-UI from putting Windows' own accent in between: with
        // both mistakes, a theme switch while the app ran left buttons in Windows' blue or the
        // other theme's shade.
        ApplyAccent(accent);
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
        ApplyAccent(accent);

        SetBrush(LinkBrushKey, Parse(accent.Link));
        SetBrush(AccentBrushKey, Parse(accent.Fill));
        var soft = Parse(accent.Fill);
        soft.A = 0x29;
        SetBrush(AccentSoftBrushKey, soft);
    }

    // WPF-UI's order: the system accent, then primary (fills), secondary (hover and the other
    // fills), tertiary (text).
    private static void ApplyAccent(Accent accent) =>
        ApplicationAccentColorManager.Apply(Parse(accent.System), Parse(accent.Fill), Parse(accent.Hover), Parse(accent.Text));

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;  // not user-visible: a registry value
        }
        catch
        {
            return true;
        }
    }
}
