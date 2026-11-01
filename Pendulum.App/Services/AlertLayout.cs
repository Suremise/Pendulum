using System.Windows;

namespace Pendulum.App.Services;

/// Where open alerts go: a pure function of the work area and the alerts' sizes, so it's tested
/// without showing a window.
///
/// The newest alert sits in the bottom-right corner and earlier ones stack upward above it. When
/// the next would cross the top of the work area, a new column starts to the left. If even the
/// columns run out, the oldest alerts wait in the last place there's room for, a little offset,
/// still on screen: each one dismissed makes room, and the next moves into view. Every alert is
/// always inside the work area, so every one can be reached.
internal static class AlertLayout
{
    public const double Margin = 24;
    public const double Gap = 12;
    private const double OverflowOffset = 6;

    /// The top-left corner for each alert, in the same order as <paramref name="sizes"/>, which is
    /// the order they opened in (oldest first).
    public static IReadOnlyList<Point> Place(Rect workArea, IReadOnlyList<Size> sizes)
    {
        var positions = new Point[sizes.Count];
        var minLeft = workArea.Left + Margin;
        var minTop = workArea.Top + Margin;
        var maxBottom = workArea.Bottom - Margin;

        var right = workArea.Right - Margin;
        var bottom = maxBottom;
        var columnWidth = 0.0;
        var placedInColumn = 0;
        Point? lastSlot = null;
        var overflow = 0;

        for (var i = sizes.Count - 1; i >= 0; i--)
        {
            var (width, height) = (sizes[i].Width, sizes[i].Height);

            // No room above the last one in this column: start the next column to the left.
            if (placedInColumn > 0 && bottom - height < minTop)
            {
                right -= columnWidth + Gap;
                bottom = maxBottom;
                columnWidth = 0;
                placedInColumn = 0;
            }

            var left = right - width;
            if (lastSlot is { } slot && left < minLeft)
            {
                // Out of columns: wait in the last slot, offset a little so it shows behind.
                overflow++;
                positions[i] = Clamp(new Point(slot.X - overflow * OverflowOffset, slot.Y - overflow * OverflowOffset), width, height, workArea);
                continue;
            }

            var top = bottom - height;
            positions[i] = Clamp(new Point(left, top), width, height, workArea);
            lastSlot = positions[i];
            bottom = top - Gap;
            columnWidth = Math.Max(columnWidth, width);
            placedInColumn++;
        }

        return positions;
    }

    /// Kept inside the work area (by its margin where it fits, and at least at its top-left edge).
    private static Point Clamp(Point p, double width, double height, Rect workArea)
    {
        var x = Math.Min(p.X, workArea.Right - Margin - width);
        var y = Math.Min(p.Y, workArea.Bottom - Margin - height);
        x = Math.Max(x, workArea.Left + Math.Min(Margin, Math.Max(0, (workArea.Width - width) / 2)));
        y = Math.Max(y, workArea.Top + Math.Min(Margin, Math.Max(0, (workArea.Height - height) / 2)));
        return new Point(x, y);
    }
}
