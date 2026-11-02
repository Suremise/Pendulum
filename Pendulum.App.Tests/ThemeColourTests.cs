using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Pendulum.App.Services;
using Xunit;

namespace Pendulum.App.Tests;

/// The app's colours: all of them come from the theme (AppThemeManager or WPF-UI's own brushes),
/// and the theme's hold their contrast in light and dark, in every state the views draw them in.
/// WCAG 2: text 4.5:1, controls and shapes 3:1.
public class ThemeColourTests
{
    // --- Every colour from the theme ------------------------------------------------------------

    private const string ThemeFile = "Services/AppThemeManager.cs"; // the one place colours are written

    /// A line that names a colour but shows none says so (text that's only measured).
    private const string NotAColour = "// not a colour";

    private static readonly Regex[] ColourPatterns =
    [
        new(@"[""']#[0-9A-Fa-f]{3,8}[""']"),                                         // "#RRGGBB" anywhere
        new(@"\b(?:Colors|Brushes|SystemColors)\.(?!Transparent\b)[A-Z]\w*"),      // Brushes.Red
        new(@"\bColor\.From(?:Argb|Rgb|ScRgb|AValues|Values)\("),                  // Color.FromArgb(...), FromRgb, FromScRgb, FromValues
        new(@"\b(?:Color|Foreground|Background|BorderBrush|Fill|Stroke)=""(?!\{)(?!Transparent"")[^""]+"""), // Foreground="White"
        new(@"Property=""(?:Color|Foreground|Background|BorderBrush|Fill|Stroke)""\s+Value=""(?!\{)(?!Transparent"")[^""]+"""),
    ];

    [Fact]
    public void No_colour_is_written_outside_the_theme()
    {
        var found = new List<string>();
        foreach (var file in SourceFiles())
        {
            var name = Path.GetRelativePath(AppSource, file).Replace('\\', '/');
            if (name == ThemeFile)
                continue;
            var text = File.ReadAllText(file);
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                text = Regex.Replace(text, "<!--.*?-->", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // A shadow is black in both themes: not a colour of the theme's.
                if (line.Contains(NotAColour, StringComparison.Ordinal) || line.Contains("DropShadowEffect", StringComparison.Ordinal))
                    continue;
                if (ColourPatterns.Any(p => p.IsMatch(line)))
                    found.Add($"{name}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.True(found.Count == 0,
            "Colours written outside AppThemeManager (use a theme brush, or add one there):\n" + string.Join("\n", found));
    }

    /// Each way of writing a colour is caught, and theme brushes aren't.
    [Theory]
    [InlineData("var b = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));", true)]
    [InlineData("var c = Color.FromRgb(255, 0, 0);", true)]
    [InlineData("var c = Color.FromScRgb(1f, 1f, 0f, 0f);", true)]
    [InlineData("var c = Color.FromValues(values, profile);", true)]
    [InlineData("text.Foreground = Brushes.Red;", true)]
    [InlineData("var c = Colors.White;", true)]
    [InlineData("var b = SystemColors.HighlightBrush;", true)]
    [InlineData("var c = ColorConverter.ConvertFromString(\"#FF0000\");", true)]
    [InlineData("<TextBlock Foreground=\"White\"/>", true)]
    [InlineData("<Border Background=\"#FF2A1720\"/>", true)]
    [InlineData("<Setter Property=\"Background\" Value=\"Red\"/>", true)]
    [InlineData("<TextBlock Foreground=\"{DynamicResource TextFillColorPrimaryBrush}\"/>", false)]
    [InlineData("<Border Background=\"Transparent\"/>", false)]
    [InlineData("<Setter Property=\"Background\" Value=\"{DynamicResource PendulumChipBrush}\"/>", false)]
    [InlineData("var b = Brushes.Transparent;", false)]
    [InlineData("var c = ColorOf(reminder);", false)]
    public void The_colour_check_catches_each_way_of_writing_a_colour(string line, bool caught) =>
        Assert.Equal(caught, ColourPatterns.Any(p => p.IsMatch(line)));

    // --- The surfaces and text the colours sit on -------------------------------------------------

    // The themes' backgrounds, from the window to a row under the pointer and a card.
    private static readonly string[] LightSurfaces = ["#FFFFFF", "#FBFBFB", "#F3F3F3", "#EEEEEE"];
    private static readonly string[] DarkSurfaces = ["#202020", "#2D2D2D", "#323232", "#3A3A3A"];

    private static string[] Surfaces(bool dark) => dark ? DarkSurfaces : LightSurfaces;

    // WPF-UI's primary text (light #E4000000, dark white) and text on an accent fill (light white,
    // dark black, as the accent sets them).
    private static Color PrimaryText(bool dark, Color surface) => dark ? Colors.White : Over(Rgb("#E4000000"), surface);
    private static Color TextOnFill(bool dark) => dark ? Colors.Black : Colors.White;

    public static TheoryData<bool> Themes => new() { false, true };

    // --- The palette -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Each_palette_shade_stands_out_from_the_surfaces(bool dark)
    {
        foreach (var color in AppThemeManager.Palette)
            foreach (var surface in Surfaces(dark))
                AssertContrast(Rgb(color.Shade(dark)), Rgb(surface), 3.0, $"{color.Name} on {surface}");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_on_each_palette_tint_is_readable(bool dark)
    {
        foreach (var color in AppThemeManager.Palette)
            foreach (var hex in Surfaces(dark))
            {
                var surface = Rgb(hex);
                var tint = Over(WithAlpha(Rgb(color.Shade(dark)), AppThemeManager.TintAlpha(dark)), surface);
                AssertContrast(PrimaryText(dark, tint), tint, 4.5, $"text on the {color.Name} tint over {hex}");
            }
    }

    /// The palette's colours stay apart for colour-blind eyes too: no two shades closer than
    /// CIEDE2000 9 as deuteranopia, protanopia or tritanopia sees them (Machado et al. 2009, full
    /// strength), nor for typical vision. About 10 reads as clearly different side by side.
    [Theory]
    [MemberData(nameof(Themes))]
    public void The_palette_is_told_apart_with_each_kind_of_colour_blindness(bool dark)
    {
        var shades = AppThemeManager.Palette.Select(c => Rgb(c.Shade(dark))).ToArray();
        foreach (var vision in Enum.GetValues<Vision>())
        {
            var seen = shades.Select(c => Simulate(c, vision)).ToArray();
            for (var i = 0; i < seen.Length; i++)
                for (var j = i + 1; j < seen.Length; j++)
                {
                    var difference = DeltaE(seen[i], seen[j]);
                    Assert.True(difference >= 9,
                        $"{AppThemeManager.Palette[i].Name} and {AppThemeManager.Palette[j].Name} are {difference:0.0} apart with {vision} ({(dark ? "dark" : "light")})");
                }
        }
    }

    [Fact]
    public void Rose_is_not_one_of_the_palette()
    {
        foreach (var dark in new[] { false, true })
        {
            var rose = Rgb((dark ? AppThemeManager.OnDark : AppThemeManager.OnLight).Fill);
            foreach (var color in AppThemeManager.Palette)
                Assert.True(DeltaE(rose, Rgb(color.Shade(dark))) >= 15, $"{color.Name} is too near the brand's rose");
        }
    }

    // --- The Reminders list ----------------------------------------------------------------------

    /// The on/off toggle, on: its fill stands out from the row, and its tick from the fill, at
    /// rest, under the pointer and pressed (a layer of the theme's text colour over the fill, at
    /// 10% and 20%, as the template draws them).
    [Theory]
    [MemberData(nameof(Themes))]
    public void The_toggle_when_on_keeps_its_contrast_in_every_state(bool dark)
    {
        var fill = Rgb(AppThemeManager.PaletteColorNamed(AppThemeManager.ToggleOnColor).Shade(dark));
        foreach (var hex in Surfaces(dark))
        {
            var surface = Rgb(hex);
            foreach (var (state, opacity) in new[] { ("at rest", 0.0), ("hovered", 0.1), ("pressed", 0.2) })
            {
                var drawn = Over(WithAlpha(PrimaryText(dark, surface), (byte)Math.Round(255 * opacity)), fill);
                AssertContrast(drawn, surface, 3.0, $"the toggle {state} on {hex}");
                AssertContrast(TextOnFill(dark), drawn, 3.0, $"the toggle's tick {state}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_on_the_grey_badges_and_spent_rows_is_readable(bool dark)
    {
        var greys = dark ? AppThemeManager.GreysOnDark : AppThemeManager.GreysOnLight;
        foreach (var hex in Surfaces(dark))
        {
            var surface = Rgb(hex);
            foreach (var (what, grey) in new[] { ("Fixed", greys.Fixed), ("Disabled", greys.Disabled) })
            {
                var badge = Over(Rgb(grey), surface);
                AssertContrast(PrimaryText(dark, badge), badge, 4.5, $"text on {what} over {hex}");
            }
        }
    }

    // --- Messages and the alert --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Themes))]
    public void Error_text_is_readable(bool dark)
    {
        var error = Rgb(dark ? AppThemeManager.ErrorTextOnDark : AppThemeManager.ErrorTextOnLight);
        foreach (var surface in Surfaces(dark))
            AssertContrast(error, Rgb(surface), 4.5, $"error text on {surface}");
    }

    [Fact]
    public void The_alert_s_words_are_readable_on_its_face()
    {
        var face = Rgb(AppThemeManager.AlertFace);
        AssertContrast(Rgb(AppThemeManager.AlertTitle), face, 4.5, "the alert's title");
        AssertContrast(Rgb(AppThemeManager.AlertSubtitle), face, 4.5, "the alert's time");
        AssertContrast(Rgb(AppThemeManager.AlertLabel), face, 4.5, "the alert's label");
    }

    // --- WCAG 2 contrast ---------------------------------------------------------------------------

    private static void AssertContrast(Color fore, Color back, double minimum, string what)
    {
        var ratio = Contrast(fore, back);
        Assert.True(ratio >= minimum, $"{what}: {ratio:0.00}:1, under {minimum}:1");
    }

    private static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// A colour, maybe see-through, drawn over an opaque one.
    private static Color Over(Color fore, Color back)
    {
        var a = fore.A / 255.0;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromRgb(Mix(fore.R, back.R), Mix(fore.G, back.G), Mix(fore.B, back.B));
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double linear)
    {
        var c = Math.Clamp(linear, 0, 1);
        return (byte)Math.Round((c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055) * 255);
    }

    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // --- Colour-blind simulation (Machado, Oliveira and Fernandes 2009) and CIEDE2000 ------------

    public enum Vision { Typical, Deuteranopia, Protanopia, Tritanopia }

    private static readonly Dictionary<Vision, double[,]> Matrices = new()
    {
        [Vision.Deuteranopia] = new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } },
        [Vision.Protanopia] = new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } },
        [Vision.Tritanopia] = new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } },
    };

    private static Color Simulate(Color c, Vision vision)
    {
        if (vision == Vision.Typical)
            return c;
        var m = Matrices[vision];
        double[] lin = [Linear(c.R), Linear(c.G), Linear(c.B)];
        byte Row(int i) => Encode(m[i, 0] * lin[0] + m[i, 1] * lin[1] + m[i, 2] * lin[2]);
        return Color.FromRgb(Row(0), Row(1), Row(2));
    }

    private static (double L, double A, double B) Lab(Color c)
    {
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static double DeltaE(Color c1, Color c2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        static double Deg(double r) => r * 180 / Math.PI;
        var (l1, a1, b1) = Lab(c1);
        var (l2, a2, b2) = Lab(c2);
        var cBar = (Math.Sqrt(a1 * a1 + b1 * b1) + Math.Sqrt(a2 * a2 + b2 * b2)) / 2;
        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(cBar, 7) / (Math.Pow(cBar, 7) + Math.Pow(25, 7))));
        double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
        double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
        double h1p = (Deg(Math.Atan2(b1, a1p)) + 360) % 360, h2p = (Deg(Math.Atan2(b2, a2p)) + 360) % 360;
        var dLp = l2 - l1;
        var dCp = c2p - c1p;
        var dhp = c1p * c2p == 0 ? 0 : h2p - h1p > 180 ? h2p - h1p - 360 : h2p - h1p < -180 ? h2p - h1p + 360 : h2p - h1p;
        var dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dhp / 2));
        var lBarP = (l1 + l2) / 2;
        var cBarP = (c1p + c2p) / 2;
        var hBarP = c1p * c2p == 0 ? h1p + h2p
            : Math.Abs(h1p - h2p) > 180 ? (h1p + h2p < 360 ? (h1p + h2p + 360) / 2 : (h1p + h2p - 360) / 2)
            : (h1p + h2p) / 2;
        var t = 1 - 0.17 * Math.Cos(Rad(hBarP - 30)) + 0.24 * Math.Cos(Rad(2 * hBarP))
                + 0.32 * Math.Cos(Rad(3 * hBarP + 6)) - 0.20 * Math.Cos(Rad(4 * hBarP - 63));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBarP - 275) / 25, 2));
        var rc = 2 * Math.Sqrt(Math.Pow(cBarP, 7) / (Math.Pow(cBarP, 7) + Math.Pow(25, 7)));
        var sl = 1 + 0.015 * Math.Pow(lBarP - 50, 2) / Math.Sqrt(20 + Math.Pow(lBarP - 50, 2));
        var sc = 1 + 0.045 * cBarP;
        var sh = 1 + 0.015 * cBarP * t;
        var rt = -Math.Sin(Rad(2 * dTheta)) * rc;
        return Math.Sqrt(Math.Pow(dLp / sl, 2) + Math.Pow(dCp / sc, 2) + Math.Pow(dHp / sh, 2) + rt * (dCp / sc) * (dHp / sh));
    }

    // --- The source --------------------------------------------------------------------------------

    private static string AppSource => Path.Combine(RepoRoot(), "Pendulum.App");

    private static IEnumerable<string> SourceFiles() =>
        new[] { "*.xaml", "*.cs" }.SelectMany(p => Directory.EnumerateFiles(AppSource, p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pendulum.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Pendulum.sln not found above the test's folder.");
    }
}
