using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Pendulum.App.Resources;
using Pendulum.App.Views;

namespace Pendulum.App.Tests;

/// The alert window's "waiting" line, built on the tests' UI thread and read back through its own
/// controls. It is never shown: nothing takes the foreground or focus from the user's apps.
[Collection(nameof(SettingsScreen))]
public sealed class AlertWindowTests
{
    private static T WithWindow<T>(Func<AlertWindow, T> read) =>
        UiThread.Invoke(() =>
        {
            var window = new AlertWindow("Dentist", "10:00");
            try { return read(window); }
            finally
            {
                window.Close();
                Assert.Empty(Application.Current.Windows);
                Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
            }
        });

    [Fact]
    public void An_unshown_alert_has_no_topmost_timer_and_close_keeps_it_stopped()
    {
        var timer = WithWindow(w =>
        {
            var timer = (DispatcherTimer)typeof(AlertWindow)
                .GetField("_topmostReinforcer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            Assert.False(w.IsVisible);
            Assert.False(timer.IsEnabled);
            return timer;
        });
        Assert.False(timer.IsEnabled);
    }

    [Fact]
    public void A_new_alert_doesnt_say_it_is_waiting_and_offers_no_dismiss_all()
    {
        var (waiting, dismissAll, snooze) = WithWindow(w => (w.WaitingText.Visibility, w.DismissAllButton.Visibility, w.SnoozeButton.Visibility));

        Assert.Equal(Visibility.Collapsed, waiting);
        Assert.Equal(Visibility.Collapsed, dismissAll);
        Assert.Equal(Visibility.Visible, snooze);
    }

    [Fact]
    public void A_silenced_alert_says_it_is_waiting_and_stops_saying_so_when_its_turn_comes()
    {
        var (whileWaiting, text, afterTurn) = WithWindow(w =>
        {
            w.SetWaiting(true);
            var shown = w.WaitingText.Visibility;
            var words = w.WaitingText.Text;
            w.SetWaiting(false);
            return (shown, words, w.WaitingText.Visibility);
        });

        Assert.Equal(Visibility.Visible, whileWaiting);
        Assert.Equal(Strings.Alert_Waiting, text);
        Assert.Equal(Visibility.Collapsed, afterTurn);
    }
}
