using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Pendulum.Core.Models;
// WPF has its own System.Windows.ThemeMode since .NET 9: the settings' one is meant here.
using ThemeMode = Pendulum.Core.Models.ThemeMode;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Pendulum.App.Services;

/// Applies the app's Light/Dark/System theme setting and Pendulum's colours, each with a shade for
/// each theme (assets/README.md, "The app's accent" and "The palette"). This is the one place the
/// app's colours are written down: the views take every colour from a brush set here or from
/// WPF-UI's theme (ThemeColourTests fails on a colour written anywhere else).
///
/// - **Rose**, the icon's colour, is the brand's alone: primary buttons, check boxes, the
///   selection (the selected tab and Settings section), today in the calendar, focus and links.
/// - **The palette**: eight muted colours round the colour wheel from rose, for everything else
///   that colour tells apart: the Reminders list's badges, its on/off toggles, the calendar's
///   chips. Each has a solid shade and a see-through tint.
/// - Everything else is the theme's own neutrals.
internal static class AppThemeManager
{
    /// The resource key of the brush links are drawn in (App.xaml's Hyperlink style).
    public const string LinkBrushKey = "PendulumLinkBrush";  // not user-visible: resource keys

    /// The accent itself (the selected tab's bar, the selected Settings section's bar, today in
    /// the calendar), and a soft tint of it (their backgrounds).
    public const string AccentBrushKey = "PendulumAccentBrush";  // not user-visible
    public const string AccentSoftBrushKey = "PendulumAccentSoftBrush";  // not user-visible

    /// Accent-coloured text (the calendar's "+N more").
    public const string AccentTextBrushKey = "PendulumAccentTextBrush";  // not user-visible

    /// The calendar's reminder chips and the Reminders list's Upcoming badge (and the Filters
    /// button while filters are on): the tint and the shade of <see cref="UpcomingColor"/>.
    public const string ChipBrushKey = "PendulumChipBrush";  // not user-visible
    public const string ChipStrokeBrushKey = "PendulumChipStrokeBrush";  // not user-visible

    /// An opaque surface for popups that hold the panel's own content (the calendar's "+N more"
    /// list, the column filters): the panel's background, solid, so what's behind doesn't show
    /// through.
    public const string PopupBrushKey = "PendulumPopupBrush";  // not user-visible

    /// The Reminders list's badges: Fixed and Disabled in neutral greys; the alert types each a
    /// tint from the palette.
    public const string BadgeFixedBrushKey = "PendulumBadgeFixedBrush";  // not user-visible
    public const string BadgeDisabledBrushKey = "PendulumBadgeDisabledBrush";  // not user-visible
    public const string BadgeSoundOnlyBrushKey = "PendulumBadgeSoundOnlyBrush";  // not user-visible
    public const string BadgeSoundAndSpeechBrushKey = "PendulumBadgeSoundAndSpeechBrush";  // not user-visible
    public const string BadgeSpeechOnlyBrushKey = "PendulumBadgeSpeechOnlyBrush";  // not user-visible

    /// A reminder's on/off toggle in the Reminders list, when on: the shade of
    /// <see cref="ToggleOnColor"/>, its tick in the theme's text-on-accent colour.
    public const string ToggleOnBrushKey = "PendulumToggleOnBrush";  // not user-visible

    /// A spent reminder's row in the Reminders list: a shade quieter than the others.
    public const string SpentRowBrushKey = "PendulumSpentRowBrush";  // not user-visible

    /// Text that says something went wrong (Quick Reminder's messages, "Disabled" in the
    /// editor): Windows' own critical red for each theme.
    public const string ErrorTextBrushKey = "PendulumErrorTextBrush";  // not user-visible

    /// The alert popup's own look, the same in both themes: a plum face (the icon's face colour)
    /// with a rose frame, its words in white and pale rose.
    public const string AlertFaceBrushKey = "PendulumAlertFaceBrush";  // not user-visible
    public const string AlertFrameBrushKey = "PendulumAlertFrameBrush";  // not user-visible
    public const string AlertLabelBrushKey = "PendulumAlertLabelBrush";  // not user-visible
    public const string AlertTitleBrushKey = "PendulumAlertTitleBrush";  // not user-visible
    public const string AlertSubtitleBrushKey = "PendulumAlertSubtitleBrush";  // not user-visible

    /// A palette colour's brushes: its solid shade, and its tint.
    public static string PaletteBrushKey(string name) => $"PendulumPalette{name}Brush"; // not user-visible
    public static string PaletteTintBrushKey(string name) => $"PendulumPalette{name}TintBrush"; // not user-visible

    /// The accent for one theme. WPF-UI 3 uses `Fill` for primary buttons, toggles and checked
    /// boxes, `Hover` for a primary button under the pointer and its other accent fills, and
    /// `Text` for accent-coloured text; `System` is the selection highlight. On light, labels on
    /// the fill are white; on dark, black.
    internal sealed record Accent(string System, string Fill, string Hover, string Text, string Link);

    // WCAG 2 contrast, checked against the themes' backgrounds (light #FAFAFA to #EEEEEE, dark
    // #202020 to #323232):
    // light: white on Fill 5.2:1 and on Hover 7.2:1; Fill on the backgrounds 4.5:1 or more;
    //        Text and Link 6.2:1 or more.
    // dark:  black on Fill 7.3:1 and on Hover 9.8:1; Fill on the backgrounds 4.5:1 or more;
    //        Text and Link 6.0:1 or more.
    internal static readonly Accent OnLight = new(System: "#D23A66", Fill: "#C7305E", Hover: "#A3234B", Text: "#A3234B", Link: "#A3234B");
    internal static readonly Accent OnDark = new(System: "#F0567F", Fill: "#F56A8F", Hover: "#FF8FB0", Text: "#FF8FB0", Link: "#FF8FB0");

    /// One of the palette's colours: a name, and its shade on the light and on the dark theme.
    internal sealed record PaletteColor(string Name, string Light, string Dark)
    {
        public string Shade(bool dark) => dark ? Dark : Light;
    }

    /// The palette, in hue order from rose round the wheel. Each shade is 3:1 or more against its
    /// theme's surfaces; its tint, with the theme's text on it, 4.5:1 or more; and no two are
    /// close for colour-blind eyes either (ThemeColourTests checks all three; assets/README.md has
    /// the figures).
    internal static readonly PaletteColor[] Palette =
    [
        new("Terracotta", "#874128", "#C77C61"),  // not user-visible: the names are keys
        new("Ochre", "#946A10", "#CEA153"),  // not user-visible: the names are keys
        new("Olive", "#81894F", "#C2CB8E"),  // not user-visible: the names are keys
        new("Sage", "#456F4E", "#89B591"),  // not user-visible: the names are keys
        new("Teal", "#269595", "#4CB3B2"),  // not user-visible: the names are keys
        new("Blue", "#4E77A1", "#6E96BF"),  // not user-visible: the names are keys
        new("Violet", "#614D92", "#BAA7F4"),  // not user-visible: the names are keys
        new("Plum", "#724869", "#E3B2D7"),  // not user-visible: the names are keys
    ];

    /// A tint is its shade at this opacity: 20% on light, 30% on dark.
    internal static byte TintAlpha(bool dark) => dark ? (byte)0x4D : (byte)0x33;

    // What each thing that colour tells apart takes from the palette.
    internal const string UpcomingColor = "Teal";  // not user-visible: palette names
    internal const string ToggleOnColor = "Teal";  // not user-visible
    internal const string SoundOnlyColor = "Blue";  // not user-visible
    internal const string SoundAndSpeechColor = "Ochre";  // not user-visible
    internal const string SpeechOnlyColor = "Plum";  // not user-visible

    // A spent row: a little of the theme's shadow over the list (light #0A000000, dark #1F000000),
    // so the secondary text on it keeps AA contrast.
    internal const string SpentRowOnLight = "#0A000000";
    internal const string SpentRowOnDark = "#1F000000";

    // The calendar's background (ControlFillColorDefault over the window), made opaque.
    internal const string PopupOnLight = "#FBFBFB";
    internal const string PopupOnDark = "#2D2D2D";

    /// Fixed and Disabled, neutral greys (#AARRGGBB): the theme's primary text on each, at the
    /// worst of the panel's backgrounds, light 10.7:1 or more, dark 6.2:1 or more.
    internal sealed record Greys(string Fixed, string Disabled);

    internal static readonly Greys GreysOnLight = new(Fixed: "#0F000000", Disabled: "#24000000");
    internal static readonly Greys GreysOnDark = new(Fixed: "#1AFFFFFF", Disabled: "#33FFFFFF");

    // Windows' critical red (WPF-UI's SystemFillColorCritical): light 5.6:1 on the window, dark
    // 7.9:1.
    internal const string ErrorTextOnLight = "#C42B1C";
    internal const string ErrorTextOnDark = "#FF99A4";

    // The alert: "PENDULUM" and "waiting" in pale rose (8.6:1 on the face), the subtitle 12.0:1.
    internal const string AlertFace = "#2A1720";
    internal const string AlertFrame = "#8E3A58";
    internal const string AlertLabel = "#E3A9BD";
    internal const string AlertTitle = "#FFFFFF";
    internal const string AlertSubtitle = "#EBD3DC";

    public static void Apply(ThemeMode mode)
    {
        var theme = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => IsSystemInDarkMode() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };
        var dark = theme == ApplicationTheme.Dark;
        var accent = dark ? OnDark : OnLight;

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
        SetBrush(AccentSoftBrushKey, WithAlpha(accent.Fill, 0x29));
        SetBrush(AccentTextBrushKey, Parse(accent.Text));

        foreach (var color in Palette)
        {
            SetBrush(PaletteBrushKey(color.Name), Parse(color.Shade(dark)));
            SetBrush(PaletteTintBrushKey(color.Name), WithAlpha(color.Shade(dark), TintAlpha(dark)));
        }

        SetBrush(ChipBrushKey, Tint(UpcomingColor, dark));
        SetBrush(ChipStrokeBrushKey, Shade(UpcomingColor, dark));
        SetBrush(ToggleOnBrushKey, Shade(ToggleOnColor, dark));
        SetBrush(BadgeSoundOnlyBrushKey, Tint(SoundOnlyColor, dark));
        SetBrush(BadgeSoundAndSpeechBrushKey, Tint(SoundAndSpeechColor, dark));
        SetBrush(BadgeSpeechOnlyBrushKey, Tint(SpeechOnlyColor, dark));

        var greys = dark ? GreysOnDark : GreysOnLight;
        SetBrush(BadgeFixedBrushKey, Parse(greys.Fixed));
        SetBrush(BadgeDisabledBrushKey, Parse(greys.Disabled));
        SetBrush(SpentRowBrushKey, Parse(dark ? SpentRowOnDark : SpentRowOnLight));
        SetBrush(PopupBrushKey, Parse(dark ? PopupOnDark : PopupOnLight));
        SetBrush(ErrorTextBrushKey, Parse(dark ? ErrorTextOnDark : ErrorTextOnLight));

        SetBrush(AlertFaceBrushKey, Parse(AlertFace));
        SetBrush(AlertFrameBrushKey, Parse(AlertFrame));
        SetBrush(AlertLabelBrushKey, Parse(AlertLabel));
        SetBrush(AlertTitleBrushKey, Parse(AlertTitle));
        SetBrush(AlertSubtitleBrushKey, Parse(AlertSubtitle));
    }

    internal static PaletteColor PaletteColorNamed(string name) => Palette.First(c => c.Name == name);

    private static Color Shade(string name, bool dark) => Parse(PaletteColorNamed(name).Shade(dark));

    private static Color Tint(string name, bool dark) => WithAlpha(PaletteColorNamed(name).Shade(dark), TintAlpha(dark));

    private static Color WithAlpha(string hex, byte alpha)
    {
        var color = Parse(hex);
        color.A = alpha;
        return color;
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

    internal static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

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
