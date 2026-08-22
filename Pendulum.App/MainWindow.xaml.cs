using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Wpf.Ui.Controls;

namespace Pendulum.App;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, __) => UpdateStatusText();
        _statusTimer.Start();
        UpdateStatusText();
    }

    private void UpdateStatusText() => StatusText.Text = ReminderStatus.GetNextReminderSummary();

    private void FluentWindow_Closing(object sender, CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.IsExiting)
            return;

        e.Cancel = true;
        Hide();
    }

    private void FluentWindow_Closed(object sender, EventArgs e)
    {
        var app = (App)Application.Current;
        if (app.IsExiting)
            Application.Current.Shutdown();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }
}
