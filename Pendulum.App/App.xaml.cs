using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Services;

namespace Pendulum.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private TrayIcon? _trayIcon;
    private HotkeyManager? _hotkeyManager;
    private HotkeyBinding? _hotkey;
    private SettingsReactions? _settingsReactions;
    private QuickAddWindow? _quickAddWindow;
    internal bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterErrorHandlers();
        try
        {
            _singleInstance = new SingleInstance(() => Dispatcher.BeginInvoke(ShowMainWindow));
            if (!_singleInstance.IsPrimary)
            {
                Shutdown();
                return;
            }
            var missed = InitializeServices();
            SubscribeToSystemEvents();
            _settingsReactions = new SettingsReactions(
                AppServices.Instance.Settings, AppThemeManager.Apply, StartupRegistration.SetEnabled,
                () => ApplyHotkeySetting(atStartup: false));
            _trayIcon = new TrayIcon(ShowMainWindow, ExitApplication, () => _hotkey?.Active);
            var window = CreateMainWindow();
            RegisterQuickReminderHotkey(window);
            ShowStartupNotices(missed);
            if (AppServices.Instance.Settings.CheckForUpdatesOnStartup)
                _ = CheckForUpdateAsync(window);
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnStartup", ex);
            MessageBox.Show(
                string.Format(Strings.App_FailedToStart, ex.Message),
                Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void RegisterErrorHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                CrashLog.Write("AppDomain.UnhandledException", ex);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                string.Format(Strings.App_UnexpectedError, args.Exception.Message),
                Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private List<string> InitializeServices()
    {
        var missed = AppServices.Instance.Initialize();
        AppServices.Instance.Engine.TriggerFired += OnTriggerFired;
        AppServices.Instance.Engine.TickFailed += ex => CrashLog.Write("TimerEngine.Tick", ex);
        // Start on the UI thread with handlers in place: reminder changes/saves stay on it.
        AppServices.Instance.Engine.Start();
        return missed;
    }

    private static void SubscribeToSystemEvents()
    {
        // Modern .NET always gives SystemEvents its own broadcast thread. Subscribe off the
        // dispatcher too: its event registration captures SynchronizationContext and would
        // otherwise synchronously send these callbacks back to our UI thread.
        Task.Run(() =>
        {
            Microsoft.Win32.SystemEvents.TimeChanged += OnSystemTimeChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }).GetAwaiter().GetResult();
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow();
        MainWindow = window;
        window.RestorePlacement();
        // Create the handle for hotkeys without showing a start-minimized window. First launch
        // and the first start after installing still show it, so the install is visibly clear.
        new WindowInteropHelper(window).EnsureHandle();
        if (!AppServices.Instance.Settings.StartMinimized || AppServices.Instance.IsFirstLaunch || AppServices.Instance.JustInstalledOrUpdated)
            window.Show();
        return window;
    }

    private void RegisterQuickReminderHotkey(MainWindow window)
    {
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.Attach(window);
        // WM_HOTKEY arrives on this UI thread with foreground activation permission.
        _hotkeyManager.Pressed += ShowQuickAdd;
        _hotkey = new HotkeyBinding(_hotkeyManager);
        ApplyHotkeySetting(atStartup: true);
    }

    private void ShowStartupNotices(List<string> missed)
    {
        if (missed.Count > 0)
            _trayIcon?.NotifyMissedReminders(missed);
        if (AppServices.Instance.DataFileNotices.Count > 0)
            ShowDataFileNotices(AppServices.Instance.DataFileNotices);
    }

    /// Holds the Quick Reminder shortcut the settings name, and says so when another app has it:
    /// a new one that's taken gives way to the one before (and Settings shows that one again); a
    /// saved one that's taken at startup is kept in Settings, with no shortcut until it's free or
    /// changed, and a notice from the tray.
    private void ApplyHotkeySetting(bool atStartup)
    {
        if (_hotkey is null)
            return;

        var settings = AppServices.Instance.Settings;
        var wanted = settings.QuickAddHotkeyGesture;
        var outcome = _hotkey.Apply(settings.QuickAddHotkeyEnabled, wanted);

        // Kept the one before: Settings shows it again (applying it then is a no-op).
        if (outcome is HotkeyOutcome.TakenKeptPrevious or HotkeyOutcome.InvalidKeptPrevious)
            settings.QuickAddHotkeyGesture = _hotkey.Active!;

        // The tooltip names the shortcut held now: changed, kept, turned off or none at all.
        _trayIcon?.RefreshTooltip();

        var message = outcome switch
        {
            HotkeyOutcome.TakenKeptPrevious => string.Format(Strings.Hotkey_TakenKeptPrevious, wanted, _hotkey.Active),
            HotkeyOutcome.InvalidKeptPrevious => string.Format(Strings.Hotkey_InvalidKeptPrevious, _hotkey.Active),
            HotkeyOutcome.TakenNone => string.Format(Strings.Hotkey_TakenNone, wanted),
            HotkeyOutcome.InvalidNone => Strings.Hotkey_InvalidNone,
            _ => null,
        };
        if (message is null)
            return;

        // At startup (or sign-in) from the tray, which takes no focus; from Settings, where the
        // user just chose it, as a message.
        if (atStartup)
            _trayIcon?.ShowNotification(message);
        else
            MessageBox.Show(MainWindow!, message, Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowQuickAdd()
    {
        if (IsExiting)
            return;

        if (_quickAddWindow is not null)
        {
            // Already open — this happens if the hotkey fires again while it's up (WM_HOTKEY
            // still gets delivered and queued through ShowDialog's nested message pump, so a
            // second press used to spawn a second overlapping, both-Topmost window fighting
            // the first for focus/Z-order instead of doing anything useful). Refocus the existing
            // one using the hotkey's foreground activation permission, as the Loaded handler
            // does on its first opening. Never share the other app's input queue.
            Win32Interop.TryBringToForeground(new WindowInteropHelper(_quickAddWindow).Handle);
            Keyboard.Focus(_quickAddWindow.InputBox);
            return;
        }

        _quickAddWindow = new QuickAddWindow { Owner = MainWindow };
        try
        {
            _quickAddWindow.ShowDialog();
        }
        finally
        {
            _quickAddWindow = null;
        }
    }

    internal void NotifyFirstMinimizeToTray()
    {
        var settings = AppServices.Instance.Settings;
        if (settings.HasShownTrayMinimizeHint)
            return;

        settings.HasShownTrayMinimizeHint = true;
        _trayIcon?.ShowNotification(Strings.Tray_StillRunning);
    }

    /// Says, once, which data files were damaged, what Pendulum used instead, and where the
    /// damaged copy is kept, so nothing is lost without the user knowing.
    private static void ShowDataFileNotices(IEnumerable<DataFileNotice> notices)
    {
        var lines = notices.Select(n => n.Outcome == StoreOutcome.RecoveredFromBackup
            ? string.Format(Strings.DataFile_Recovered, n.File, n.SetAsidePath ?? Strings.DataFile_NoDamagedCopy)
            : string.Format(Strings.DataFile_SetAside, n.File, n.SetAsidePath));
        MessageBox.Show(string.Join("\n\n", lines), Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static async Task CheckForUpdateAsync(MainWindow mainWindow)
    {
        try
        {
            var currentVersion = AppVersion.Current;
            var newerVersion = await UpdateCheckService.CheckForNewerVersionAsync(AppServices.Instance.Settings, currentVersion, CancellationToken.None);
            if (newerVersion is not null)
                await mainWindow.Dispatcher.BeginInvoke(() => mainWindow.ShowUpdateAvailable(newerVersion));
        }
        catch (Exception ex)
        {
            // Background nicety — never worth crashing or bothering the user over.
            CrashLog.Write("CheckForUpdateAsync", ex);
        }
    }

    private void OnTriggerFired(Pendulum.Core.Models.TriggerTimer trigger)
    {
        Dispatcher.Invoke(() =>
        {
            var services = AppServices.Instance;
            // The alert's buttons act on this firing only: if the reminder is edited while the
            // alert is open, they leave the edit alone.
            var firedAt = trigger.TriggerAt;
            AlertCoordinator.Fire(
                trigger.Name,
                DisplayFormat.DateAndTime(trigger.TriggerAt),
                trigger.SoundFileName,
                trigger.Mode,
                trigger.Phrase,
                volumePercent: trigger.Volume,
                onSnooze: () =>
                {
                    services.Update(trigger, () => TriggerReconciler.Snooze(trigger, firedAt, DateTime.Now, services.Settings.SnoozeMinutes));
                },
                onDismiss: () =>
                {
                    services.Update(trigger, () => TriggerReconciler.Dismiss(trigger, firedAt, DateTime.Now));
                });
        });
    }

    internal void ShowMainWindow()
    {
        if (IsExiting || MainWindow is null)
            return;

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    internal void ExitApplication()
    {
        IsExiting = true;
        MainWindow?.Close();
    }

    private static void OnSystemTimeChanged(object? sender, EventArgs e) => TimeZoneInfo.ClearCachedData();

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (_singleInstance?.IsPrimary == true)
            AppServices.Instance.FlushPendingSaves();
        base.OnSessionEnding(e);
    }

    private static void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            TimeZoneInfo.ClearCachedData();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        Microsoft.Win32.SystemEvents.TimeChanged -= OnSystemTimeChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        try
        {
            if (_singleInstance?.IsPrimary == true)
            {
                try
                {
                    _settingsReactions?.Dispose();
                    _hotkeyManager?.Dispose();
                    _trayIcon?.Dispose();
                }
                finally { AppServices.Instance.Shutdown(); }
            }
        }
        finally
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
