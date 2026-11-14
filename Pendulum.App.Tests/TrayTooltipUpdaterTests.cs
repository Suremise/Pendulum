using Pendulum.App.Services;

namespace Pendulum.App.Tests;

public sealed class TrayTooltipUpdaterTests
{
    [Fact]
    public void An_unchanged_tooltip_does_not_call_the_shell_again()
    {
        var calls = new List<string>();
        var updater = new TrayTooltipUpdater(calls.Add, _ => Assert.Fail("Unexpected shell failure"));

        updater.Update("first");
        updater.Update("first");
        updater.Update("second");

        Assert.Equal(["first", "second"], calls);
    }

    [Fact]
    public void A_shell_failure_is_logged_once_and_the_next_change_is_tried()
    {
        var calls = new List<string>();
        var failures = new List<Exception>();
        var failure = new InvalidOperationException("Explorer unavailable");
        var updater = new TrayTooltipUpdater(text =>
        {
            calls.Add(text);
            if (text == "first") throw failure;
        }, failures.Add);

        updater.Update("first");
        updater.Update("first");
        updater.Update("second");

        Assert.Same(failure, Assert.Single(failures));
        Assert.Equal(["first", "second"], calls);
    }
}
