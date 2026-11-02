using System.Windows;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// Where the main window opens, from its saved bounds and the screens' work areas: no window.
public sealed class WindowPlacementTests
{
    // A 1920x1080 main screen with a 48 px taskbar, and a 1280x1024 one to its right.
    private static readonly Rect Primary = new(0, 0, 1920, 1032);
    private static readonly Rect Second = new(1920, 0, 1280, 984);
    private static readonly Rect[] Screens = [Primary, Second];

    [Fact]
    public void Where_it_was_left_on_a_screen_it_opens_there()
    {
        var saved = new Rect(200, 150, 1100, 700);
        Assert.Equal(saved, WindowPlacement.Fit(saved, Screens, Primary));
    }

    [Fact]
    public void Left_on_the_second_screen_it_opens_there()
    {
        var saved = new Rect(2100, 100, 1000, 700);
        Assert.Equal(saved, WindowPlacement.Fit(saved, Screens, Primary));
    }

    [Fact]
    public void Partly_off_the_edge_but_with_its_title_bar_reachable_it_stays_where_it_was()
    {
        var saved = new Rect(-600, 300, 1000, 700);   // 400 px of its title bar on the main screen
        Assert.Equal(saved, WindowPlacement.Fit(saved, [Primary], Primary));
    }

    [Theory]
    [InlineData(4000, 200)]     // on a screen that's been unplugged
    [InlineData(-3000, 200)]    // far off to the left
    [InlineData(300, -500)]     // title bar above the top of every screen
    [InlineData(300, 1020)]     // only the very top edge left showing at the bottom
    public void Off_every_screen_it_comes_back_centred_on_the_main_one(double left, double top)
    {
        var placed = WindowPlacement.Fit(new Rect(left, top, 1000, 700), Screens, Primary);

        Assert.Equal(new Rect(460, 166, 1000, 700), placed);
    }

    [Fact]
    public void Larger_than_its_screen_it_shrinks_to_fit_it()
    {
        var placed = WindowPlacement.Fit(new Rect(1950, 0, 1600, 1200), Screens, Primary);

        Assert.Equal(new Rect(1920, 0, 1280, 984), placed);
    }

    [Fact]
    public void Off_screen_and_larger_than_the_main_screen_it_comes_back_no_larger_than_it()
    {
        var placed = WindowPlacement.Fit(new Rect(9000, 9000, 2500, 1500), Screens, Primary);

        Assert.Equal(Primary, placed);
    }
}
