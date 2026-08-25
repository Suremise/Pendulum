using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Wpf.Ui.Controls;

namespace Pendulum.App;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _statusTimer;
    private string? _updateReleaseVersion;

    public MainWindow()
    {
        InitializeComponent();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, __) => UpdateStatusText();
        _statusTimer.Start();
        UpdateStatusText();
    }

    private void UpdateStatusText() => StatusText.Text = ReminderStatus.GetNextReminderSummary();

    /// Shown in the status bar, left of the About button, once a background check finds a
    /// newer version on GitHub. Never a popup — just a small, persistent line that clears
    /// itself naturally once the user's actually on the newer build.
    public void ShowUpdateAvailable(string version)
    {
        _updateReleaseVersion = version;
        UpdateAvailableRun.Text = $"Version {version} available — ";
        UpdateAvailableText.Visibility = Visibility.Visible;
    }

    private void UpdateAvailableLink_Click(object sender, RoutedEventArgs e)
    {
        var uri = _updateReleaseVersion is null
            ? "https://github.com/Suremise/Pendulum/releases/latest"
            : $"https://github.com/Suremise/Pendulum/releases/tag/v{_updateReleaseVersion}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private void FluentWindow_Closing(object sender, CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.IsExiting)
            return;

        e.Cancel = true;
        Hide();
        app.NotifyFirstMinimizeToTray();
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
