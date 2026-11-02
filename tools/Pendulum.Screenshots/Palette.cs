using System.Windows.Media;

namespace Pendulum.Screenshots;

/// The proposed palette for reminders and categories (the redesign's mock-ups): eight muted
/// colours spaced round the colour wheel from rose, each with a shade for the light theme and one
/// for the dark. Rose itself stays the brand's (the primary button, selection, today, focus), so
/// it isn't one of them.
///
/// Each shade is a solid colour, 3:1 or more against its theme's surface (a chip's stripe, a
/// swatch, a dot); chips and badges are a tint of it (<see cref="TintAlpha"/>) with the theme's
/// own text on top, 4.5:1 or more. The shades were chosen in OKLCH (hue fixed, chroma 0.07 to
/// 0.11, lightness varied) so that every pair stays apart for colour-blind eyes as well: the
/// mock-up sheet prints the figures.
internal static class Palette
{
    internal sealed record Swatch(string Token, string Name, double Hue, string Light, string Dark)
    {
        public Color Shade(bool dark) => Parse(dark ? Dark : Light);
    }

    /// In hue order, from rose round the wheel.
    public static readonly Swatch[] Colors =
    [
        new("terracotta", "Terracotta", 40, "#874128", "#C77C61"),
        new("ochre", "Ochre", 80, "#946A10", "#CEA153"),
        new("olive", "Olive", 115, "#81894F", "#C2CB8E"),
        new("sage", "Sage", 150, "#456F4E", "#89B591"),
        new("teal", "Teal", 195, "#269595", "#4CB3B2"),
        new("blue", "Blue", 250, "#4E77A1", "#6E96BF"),
        new("violet", "Violet", 295, "#614D92", "#BAA7F4"),
        new("plum", "Plum", 335, "#724869", "#E3B2D7"),
    ];

    /// The order new reminders take them in (the least used first, ties in this order): each next
    /// to colours far from it, so reminders made together are told apart.
    public static readonly string[] AssignOrder = ["teal", "terracotta", "blue", "ochre", "violet", "sage", "plum", "olive"];

    public static Swatch ByToken(string token) => Colors.First(c => c.Token == token);

    /// A chip's or badge's tint: the shade at this opacity over the surface.
    public static byte TintAlpha(bool dark) => dark ? (byte)0x4D : (byte)0x33;  // 30% and 20%

    /// The surfaces the colours sit on: the panels' fill over the window (opaque), and the
    /// theme's primary text over it.
    public static Color Surface(bool dark) => Parse(dark ? "#2D2D2D" : "#FBFBFB");
    public static Color Text(bool dark) => dark ? Parse("#FFFFFF") : Parse("#1C1C1C");

    public static Color Tint(Swatch swatch, bool dark) => Over(swatch.Shade(dark), TintAlpha(dark), Surface(dark));

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static Color Over(Color fore, byte alpha, Color back)
    {
        var a = alpha / 255.0;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromRgb(Mix(fore.R, back.R), Mix(fore.G, back.G), Mix(fore.B, back.B));
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // --- WCAG 2 contrast -------------------------------------------------------------------------

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double linear)
    {
        var c = Math.Clamp(linear, 0, 1);
        var v = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Round(v * 255);
    }

    public static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    public static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // --- Colour-blind simulation (Machado, Oliveira and Fernandes 2009, full severity) ----------

    internal enum Vision { Typical, Deuteranopia, Protanopia, Tritanopia }

    private static readonly Dictionary<Vision, double[,]> Matrices = new()
    {
        [Vision.Deuteranopia] = new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } },
        [Vision.Protanopia] = new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } },
        [Vision.Tritanopia] = new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } },
    };

    public static Color Simulate(Color c, Vision vision)
    {
        if (vision == Vision.Typical)
            return c;
        var m = Matrices[vision];
        double[] lin = [Linear(c.R), Linear(c.G), Linear(c.B)];
        byte Row(int i) => Encode(m[i, 0] * lin[0] + m[i, 1] * lin[1] + m[i, 2] * lin[2]);
        return Color.FromRgb(Row(0), Row(1), Row(2));
    }

    // --- CIEDE2000 colour difference ---------------------------------------------------------------

    private static (double L, double A, double B) Lab(Color c)
    {
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    public static double DeltaE(Color c1, Color c2)
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

    /// The closest two colours of a set as one kind of eye sees them: the smallest CIEDE2000
    /// difference, and which pair.
    public static (double DeltaE, int First, int Second) Closest(IReadOnlyList<Color> colors, Vision vision)
    {
        var simulated = colors.Select(c => Simulate(c, vision)).ToArray();
        var best = (double.MaxValue, 0, 0);
        for (var i = 0; i < simulated.Length; i++)
            for (var j = i + 1; j < simulated.Length; j++)
            {
                var d = DeltaE(simulated[i], simulated[j]);
                if (d < best.Item1)
                    best = (d, i, j);
            }
        return best;
    }
}
