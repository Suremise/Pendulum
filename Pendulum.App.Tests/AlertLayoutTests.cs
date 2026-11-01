using System.Windows;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// Where alerts go, as pure geometry: no alert window is made or shown.
public sealed class AlertLayoutTests
{
    // A 1920x1080 screen with a 40-pixel taskbar, and alerts 360 wide by 150 tall, as AlertWindow is.
    private static readonly Rect Screen = new(0, 0, 1920, 1040);
    private static readonly Size Alert = new(360, 150);

    private static IReadOnlyList<Point> Place(int count, Rect? area = null, Size? size = null) =>
        AlertLayout.Place(area ?? Screen, Enumerable.Repeat(size ?? Alert, count).ToList());

    private static Rect Bounds(Point p, Size? size = null) => new(p, size ?? Alert);

    /// Whether two alerts cover any of the same area (touching edges don't count).
    private static bool Overlaps(Rect a, Rect b)
    {
        var shared = Rect.Intersect(a, b);
        return !shared.IsEmpty && shared.Width > 0 && shared.Height > 0;
    }

    [Fact]
    public void One_alert_sits_in_the_bottom_right_corner()
    {
        var p = Assert.Single(Place(1));

        Assert.Equal(new Point(1920 - 24 - 360, 1040 - 24 - 150), p);
    }

    [Fact]
    public void Later_alerts_go_at_the_bottom_and_earlier_ones_move_up()
    {
        var p = Place(3);

        // Opened oldest first: the newest (last) is at the bottom.
        Assert.Equal(1040 - 24 - 150, p[2].Y);
        Assert.Equal(p[2].Y - 150 - 12, p[1].Y);
        Assert.Equal(p[1].Y - 150 - 12, p[0].Y);
        Assert.All(p, q => Assert.Equal(1920 - 24 - 360, q.X));
    }

    [Fact]
    public void When_a_column_is_full_the_next_alert_starts_a_column_to_the_left()
    {
        // (1040 - 48 + 12) / 162 = 6 fit in a column; the seventh newest starts a second column.
        var p = Place(7);

        Assert.Equal(1920 - 24 - 360, p[6].X);
        Assert.Equal(1920 - 24 - 360, p[1].X);
        Assert.Equal(1920 - 24 - 360 - 12 - 360, p[0].X);
        Assert.Equal(1040 - 24 - 150, p[0].Y); // at the bottom of its column
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(25)]
    [InlineData(100)]
    public void Every_alert_is_on_screen_however_many(int count)
    {
        var p = Place(count);

        Assert.Equal(count, p.Count);
        Assert.All(p, q => Assert.True(Screen.Contains(Bounds(q)), $"{q} is off screen"));
    }

    [Fact]
    public void Alerts_that_fit_dont_overlap()
    {
        // 6 per column, 5 columns across 1920 (5 x 372 = 1860): 30 fit.
        var p = Place(30);

        for (var i = 0; i < p.Count; i++)
            for (var j = i + 1; j < p.Count; j++)
                Assert.False(Overlaps(Bounds(p[i]), Bounds(p[j])), $"alerts {i} and {j} overlap");
    }

    [Fact]
    public void Beyond_what_fits_the_oldest_wait_behind_the_last_slot_and_the_newest_are_all_visible()
    {
        var p = Place(40);

        // The 30 newest have their own places, none overlapping; the 10 oldest are on screen, offset.
        var newest = p.Skip(10).Select(q => Bounds(q)).ToList();
        for (var i = 0; i < newest.Count; i++)
            for (var j = i + 1; j < newest.Count; j++)
                Assert.False(Overlaps(newest[i], newest[j]), $"alerts {i} and {j} overlap");
        Assert.All(p.Take(10), q => Assert.True(Screen.Contains(Bounds(q))));
    }

    [Fact]
    public void Dismissing_one_lets_the_next_waiting_alert_into_its_own_place()
    {
        var before = Place(31);
        var after = Place(30);

        // With 31, the oldest waits behind; with one dismissed, every alert has a place of its own.
        Assert.Equal(30, after.Select(q => (q.X, q.Y)).Distinct().Count());
        Assert.NotEqual(before[0], after[0]);
    }

    [Fact]
    public void On_a_second_monitor_alerts_go_in_its_work_area()
    {
        // A monitor to the right of the primary one, with its taskbar on top.
        var second = new Rect(1920, 40, 2560, 1400);

        var p = Place(10, second);

        Assert.All(p, q => Assert.True(second.Contains(Bounds(q))));
        Assert.Equal(1920 + 2560 - 24 - 360, p[^1].X);
        Assert.Equal(40 + 1400 - 24 - 150, p[^1].Y);
    }

    [Fact]
    public void A_monitor_left_of_the_primary_has_negative_coordinates_and_still_works()
    {
        var left = new Rect(-1280, 0, 1280, 984);

        Assert.All(Place(12, left), q => Assert.True(left.Contains(Bounds(q))));
    }

    [Fact]
    public void A_work_area_smaller_than_an_alert_still_shows_it_from_the_top_left()
    {
        var tiny = new Rect(0, 0, 300, 100);

        var p = Assert.Single(Place(1, tiny));

        Assert.Equal(new Point(0, 0), p);
    }

    [Fact]
    public void Alerts_of_different_heights_stack_by_their_own_heights()
    {
        var sizes = new List<Size> { new(360, 200), new(360, 120) };

        var p = AlertLayout.Place(Screen, sizes);

        Assert.Equal(1040 - 24 - 120, p[1].Y);
        Assert.Equal(p[1].Y - 12 - 200, p[0].Y);
    }

    [Fact]
    public void No_alerts_no_places()
    {
        Assert.Empty(AlertLayout.Place(Screen, Array.Empty<Size>()));
    }
}
