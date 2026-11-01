using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

/// Pendulum's public links, shown in About.
public static class AboutLinks
{
    public const string Repository = "https://github.com/Suremise/Pendulum";
    public const string Releases = "https://github.com/Suremise/Pendulum/releases";
}

public partial class AboutWindow : FluentWindow
{
    private readonly AboutFacts _facts = AboutFacts.Gather();
    private DispatcherTimer? _copiedTimer;

    public AboutWindow()
    {
        InitializeComponent();
        FactsList.ItemsSource = _facts.Rows;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_facts.CopyText());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another app has the clipboard open; the user can press Copy again.
            return;
        }

        CopyButton.Content = Strings.About_Copied;
        _copiedTimer?.Stop();
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            CopyButton.Content = Strings.About_Copy;
        };
        _copiedTimer.Start();
    }

    private void ReleaseNotesButton_Click(object sender, RoutedEventArgs e) => OpenLink(AboutLinks.Releases);

    private void SourceCodeButton_Click(object sender, RoutedEventArgs e) => OpenLink(AboutLinks.Repository);

    private static void OpenLink(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    private void ThirdPartyNoticesButton_Click(object sender, RoutedEventArgs e) =>
        OpenTextFileNextToApp("THIRD-PARTY-NOTICES.md");

    private void LicenseButton_Click(object sender, RoutedEventArgs e) =>
        OpenTextFileNextToApp("LICENSE");

    private void OpenTextFileNextToApp(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show(this, string.Format(Strings.About_FileNotFound, fileName), Strings.App_Name, System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Launch Notepad directly rather than UseShellExecute (which opens whatever's
        // registered for the extension) — on a dev machine that's often VS Code, which throws up
        // its own "untrusted files" workspace-trust prompt for a file opened outside a workspace.
        // By its full path in System32, so no notepad.exe in the app's or current folder is run.
        var notepad = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe")); // not user-visible
        notepad.ArgumentList.Add(path);
        Process.Start(notepad);
    }
}
