using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

/// The Settings window (Tools > Settings…). Closing it hides it, so the one settings view is
/// made once and kept, as the Settings tab was; it goes for good when Pendulum exits.
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow() => InitializeComponent();

    private void SettingsWindow_Closing(object sender, CancelEventArgs e)
    {
        // Only Pendulum's own App keeps it for next time (a test's host closes it).
        if (Application.Current is not App { IsExiting: false })
            return;

        e.Cancel = true;
        Hide();
        Owner?.Activate();
    }
}
