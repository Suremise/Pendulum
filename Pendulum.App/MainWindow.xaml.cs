using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Services;
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

        // The shortcuts the menu shows beside its items.
        InputBindings.Add(new KeyBinding(new RelayCommand(NewReminder), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(OpenSettings), Key.OemComma, ModifierKeys.Control));
    }

    // ---- The menu bar ----

    private TriggersViewModel Reminders => (TriggersViewModel)TriggersPanelControl.DataContext;

    /// File > New reminder… (Ctrl+N): on the Reminders tab, the new reminder's editor.
    internal void NewReminder()
    {
        MainTabControl.SelectedItem = FixedTriggersTab;
        Reminders.AddCommand.Execute(null);
    }

    private void NewReminder_Click(object sender, RoutedEventArgs e) => NewReminder();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = FixedTriggersTab;
        Reminders.ImportCommand.Execute(null);
    }

    private void Export_Click(object sender, RoutedEventArgs e) => Reminders.ExportCommand.Execute(null);

    /// File > Exit: quits Pendulum, as the tray's Exit does (closing the window keeps it in the tray).
    private void Exit_Click(object sender, RoutedEventArgs e) => (Application.Current as App)?.ExitApplication();

    private void ViewReminders_Click(object sender, RoutedEventArgs e) => MainTabControl.SelectedItem = FixedTriggersTab;

    private void ViewCalendar_Click(object sender, RoutedEventArgs e) => MainTabControl.SelectedItem = CalendarTab;

    private SettingsWindow? _settingsWindow;

    /// The one Settings window, made the first time it's asked for and kept (closing hides it).
    internal SettingsWindow SettingsWindow => _settingsWindow ??= new SettingsWindow();

    /// Tools > Settings… (Ctrl+,): the Settings window, over this one and centred on it. (Its
    /// owner is set here, as it opens: WPF takes only a window that has been shown as an owner.)
    internal void OpenSettings()
    {
        var settings = SettingsWindow;
        settings.Owner ??= this;
        if (settings.IsVisible)
            settings.Activate();
        else
            settings.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    /// Help > Check for updates…: asks GitHub now and says what it found.
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var running = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var runningText = running.ToString(3);
        CheckForUpdatesMenuItem.IsEnabled = false;
        try
        {
            var result = await UpdateCheckService.CheckNowAsync(AppServices.Instance.Settings, running, CancellationToken.None);
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.Newer:
                    ShowUpdateAvailable(result.Version!);
                    if (System.Windows.MessageBox.Show(this, string.Format(Strings.Update_Available, result.Version, runningText), Strings.App_Name,
                            System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Information) == System.Windows.MessageBoxResult.Yes)
                        OpenReleasePage();
                    break;
                case UpdateCheckOutcome.UpToDate:
                    System.Windows.MessageBox.Show(this, string.Format(Strings.Update_UpToDate, runningText), Strings.App_Name,
                        System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                default:
                    System.Windows.MessageBox.Show(this, Strings.Update_Unreachable, Strings.App_Name,
                        System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        finally
        {
            CheckForUpdatesMenuItem.IsEnabled = true;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void UpdateStatusText() => StatusText.Text = ReminderStatus.GetNextReminderSummary();

    /// Shown at the right of the status bar once a check finds a newer version on GitHub. Never a popup — just a small, persistent line that clears
    /// itself naturally once the user's actually on the newer build.
    public void ShowUpdateAvailable(string version)
    {
        _updateReleaseVersion = version;
        UpdateAvailableRun.Text = string.Format(Strings.Main_UpdateAvailable, version);
        UpdateAvailableText.Visibility = Visibility.Visible;
    }

    private void UpdateAvailableLink_Click(object sender, RoutedEventArgs e) => OpenReleasePage();

    private void OpenReleasePage()
    {
        var uri = _updateReleaseVersion is null
            ? "https://github.com/Suremise/Pendulum/releases/latest"
            : $"https://github.com/Suremise/Pendulum/releases/tag/v{_updateReleaseVersion}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    /// Opens where the window was last left (WindowPlacement keeps it on a screen), maximised if
    /// it was. Before the window is shown or has a handle; with nothing saved, the default size,
    /// centred.
    internal void RestorePlacement()
    {
        var s = AppServices.Instance.Settings;
        if (s.MainWindowLeft is not { } left || s.MainWindowTop is not { } top
            || s.MainWindowWidth is not { } width || s.MainWindowHeight is not { } height
            || width <= 0 || height <= 0)
            return;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var areas = Win32Interop.MonitorWorkAreas()
            .Select(a => (Rect: new Rect(a.Left / scale, a.Top / scale, (a.Right - a.Left) / scale, (a.Bottom - a.Top) / scale), a.Primary))
            .ToList();
        if (areas.Count == 0)
            return;

        var primary = areas.FirstOrDefault(a => a.Primary).Rect;
        if (primary.IsEmpty)
            primary = areas[0].Rect;
        var placed = WindowPlacement.Fit(new Rect(left, top, Math.Max(width, MinWidth), Math.Max(height, MinHeight)),
            areas.Select(a => a.Rect).ToList(), primary);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placed.Left;
        Top = placed.Top;
        Width = placed.Width;
        Height = placed.Height;
        if (s.MainWindowMaximized)
            WindowState = WindowState.Maximized;
    }

    // The state a minimised window goes back to: a minimised window's own WindowState can't say
    // whether it was maximised before.
    private WindowState _stateBeforeMinimized = WindowState.Normal;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == WindowStateProperty && e.NewValue is WindowState state && state != WindowState.Minimized)
            _stateBeforeMinimized = state;
    }

    /// Whether the window opens maximised next time: it's maximised now, or it's minimised from
    /// maximised.
    internal bool MaximizedToRemember =>
        WindowState == WindowState.Maximized
        || (WindowState == WindowState.Minimized && _stateBeforeMinimized == WindowState.Maximized);

    /// Remembers the window's normal bounds (its size before it was maximised or minimised) and
    /// whether it's maximised (or minimised from maximised), in one save. Nothing until it has
    /// been shown.
    internal void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.Left))
            return;

        SavePlacement(bounds, MaximizedToRemember);
    }

    /// Saves these normal bounds and whether the window was maximised, in one save.
    internal static void SavePlacement(Rect bounds, bool maximized)
    {
        AppServices.Instance.ChangeSettingsTogether(() =>
        {
            var s = AppServices.Instance.Settings;
            s.MainWindowLeft = bounds.Left;
            s.MainWindowTop = bounds.Top;
            s.MainWindowWidth = bounds.Width;
            s.MainWindowHeight = bounds.Height;
            s.MainWindowMaximized = maximized;
        });
    }

    private void FluentWindow_Closing(object sender, CancelEventArgs e)
    {
        if (IsVisible)
            SavePlacement();
        // Only Pendulum's own App hides the window to the tray (a test's host closes it).
        if (Application.Current is not App app || app.IsExiting)
            return;

        e.Cancel = true;
        Hide();
        app.NotifyFirstMinimizeToTray();
    }

    private void FluentWindow_Closed(object sender, EventArgs e)
    {
        if (Application.Current is App { IsExiting: true } app)
            app.Shutdown();
    }
}
