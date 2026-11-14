using System.Windows;
using System.Windows.Controls;
using Pendulum.App.Resources;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

public sealed class TrayMenuTests
{
    [Fact]
    public void Open_and_exit_keep_their_labels_and_invoke_their_own_callbacks()
    {
        UiThread.Invoke(() =>
        {
            var opened = 0;
            var exited = 0;
            var menu = TrayIcon.BuildMenu(() => opened++, () => exited++);
            Assert.Equal(3, menu.Items.Count);
            var open = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal(Strings.Tray_Open, open.Header);
            Assert.IsType<Separator>(menu.Items[1]);
            var exit = Assert.IsType<MenuItem>(menu.Items[2]);
            Assert.Equal(Strings.Tray_Exit, exit.Header);
            open.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal((1, 0), (opened, exited));
            exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal((1, 1), (opened, exited));
        });
    }
}
