using System.Windows;

namespace Pendulum.App.Services;

/// Where the main window opens: where it was last left, unless that's no longer on a screen (a
/// monitor unplugged, the resolution lowered), when it opens centred on the main screen. A pure
/// function of the saved bounds and the screens' work areas, so it's tested without a window.
internal static class WindowPlacement
{
    /// How much of the title bar must be on a screen for the window to be reachable there: enough
    /// to see it and drag it.
    public const double TitleBarHeight = 32;
    public const double MinVisibleWidth = 120;

    /// The bounds to open at. <paramref name="saved"/> is kept when enough of its title bar is on
    /// one of <paramref name="workAreas"/>, shrunk (and moved in) only where it's larger than that
    /// screen. Otherwise the window is centred on <paramref name="primary"/>, no larger than it.
    public static Rect Fit(Rect saved, IReadOnlyList<Rect> workAreas, Rect primary)
    {
        var titleBar = new Rect(saved.X, saved.Y, saved.Width, TitleBarHeight);
        var home = workAreas
            .Select(area => (Area: area, Shown: Overlap(titleBar, area)))
            .Where(a => a.Shown.Width >= Math.Min(MinVisibleWidth, saved.Width) && a.Shown.Height >= TitleBarHeight / 2)
            .OrderByDescending(a => a.Shown.Width * a.Shown.Height)
            .Select(a => (Rect?)a.Area)
            .FirstOrDefault();

        if (home is { } area)
        {
            var width = Math.Min(saved.Width, area.Width);
            var height = Math.Min(saved.Height, area.Height);
            // Larger than its screen: shrunk to it, and moved in so it all shows.
            var x = width < saved.Width ? area.X : saved.X;
            var y = height < saved.Height ? area.Y : saved.Y;
            return new Rect(x, y, width, height);
        }

        var w = Math.Min(saved.Width, primary.Width);
        var h = Math.Min(saved.Height, primary.Height);
        return new Rect(primary.X + (primary.Width - w) / 2, primary.Y + (primary.Height - h) / 2, w, h);
    }

    private static Rect Overlap(Rect a, Rect b)
    {
        var overlap = Rect.Intersect(a, b);
        return overlap.IsEmpty ? new Rect() : overlap;
    }
}
