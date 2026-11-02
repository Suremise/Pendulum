using Pendulum.App.Resources;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// The tray icon's tooltip text: the next reminder, then the Quick Reminder shortcut while it's
/// held, in whole lines within what a tray tooltip holds. Text only: no icon, no window.
public sealed class TrayTooltipTests
{
    private static readonly string Status = "Next: Dentist in 2h 5m";

    [Fact]
    public void With_the_shortcut_held_it_has_a_line_of_its_own()
    {
        var tip = TrayTooltip.Build(Status, "Ctrl+Alt+R");

        Assert.Equal(
            string.Format(Strings.Tray_ToolTipWithStatus, Status) + "\n" + string.Format(Strings.Tray_ToolTipShortcut, "Ctrl+Alt+R"),
            tip);
        Assert.EndsWith("\nQuick Reminder: Ctrl+Alt+R", tip);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Off_or_not_held_there_is_no_shortcut_line(string? shortcut)
    {
        var tip = TrayTooltip.Build(Status, shortcut);

        Assert.Equal(string.Format(Strings.Tray_ToolTipWithStatus, Status), tip);
        Assert.DoesNotContain('\n', tip);
    }

    [Fact]
    public void A_next_reminder_too_long_for_the_tooltip_gives_way_to_the_name_and_the_shortcut_still_shows()
    {
        var longStatus = "Next: " + new string('x', 200);

        var tip = TrayTooltip.Build(longStatus, "Ctrl+Alt+R");

        Assert.Equal(Strings.App_Name + "\n" + string.Format(Strings.Tray_ToolTipShortcut, "Ctrl+Alt+R"), tip);
        Assert.True(tip.Length <= TrayTooltip.MaxLength);
    }

    [Fact]
    public void A_line_that_would_go_over_is_left_out_whole_never_cut()
    {
        var first = new string('a', 120);

        Assert.Equal(first, TrayTooltip.FitLines(first, "Pendulum", ["Quick Reminder: Ctrl+Alt+R", "x"], 127));
    }

    [Fact]
    public void A_line_that_fits_exactly_is_kept()
    {
        var first = new string('a', 100);
        var line = new string('b', 26); // 100 + the line break + 26 = 127

        var tip = TrayTooltip.FitLines(first, "Pendulum", [line], 127);

        Assert.Equal(first + "\n" + line, tip);
        Assert.Equal(127, tip.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(105)]
    [InlineData(126)]
    [InlineData(300)]
    public void Whatever_the_reminder_the_tooltip_stays_within_the_limit(int length)
    {
        var tip = TrayTooltip.Build(new string('n', length), "Ctrl+Shift+Alt+F12");

        Assert.True(tip.Length <= TrayTooltip.MaxLength, $"{tip.Length} characters");
        Assert.All(tip.Split('\n'), line => Assert.False(string.IsNullOrEmpty(line)));
    }
}
