using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var versionText = version is null ? "Version 1.0" : $"Version {version.Major}.{version.Minor}.{version.Build}";

            var builtAt = File.GetLastWriteTime(assembly.Location);
            VersionText.Text = $"{versionText} · Built {builtAt:dd MMM yyyy, HH:mm}";
        }
        catch
        {
            // build timestamp is a nice-to-have; leave the default text if it can't be read.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
