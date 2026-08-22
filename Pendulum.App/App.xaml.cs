using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Wpf.Ui.Appearance;

namespace Pendulum.App;

public partial class App : Application
{
    private const string MutexName = "Pendulum.SingleInstance.Mutex";
    private const string WakeEventName = "Pendulum.WakeExisting.Event";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _wakeEvent;
    private TaskbarIcon? _trayIcon;
    private DispatcherTimer? _trayTooltipTimer;
    internal bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                CrashLog.Write("AppDomain.UnhandledException", ex);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                $"Pendulum hit an unexpected error and logged the details to Data\\crash.log.\n\n{args.Exception.Message}",
                "Pendulum", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            // Another instance is already running (likely hidden in the tray) — wake it instead of silently exiting.
            try
            {
                using var existingWakeEvent = EventWaitHandle.OpenExisting(WakeEventName);
                existingWakeEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // the running instance hasn't finished starting up yet; nothing more we can do here.
            }
            Shutdown();
            return;
        }

        _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
        var wakeThread = new Thread(() =>
        {
            while (true)
            {
                _wakeEvent.WaitOne();
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        { IsBackground = true };
        wakeThread.Start();

        try
        {
            var missed = AppServices.Instance.Initialize();
            AppServices.Instance.Engine.TriggerFired += OnTriggerFired;
            AppServices.Instance.Engine.TickFailed += ex => CrashLog.Write("TimerEngine.Tick", ex);

            ApplicationAccentColorManager.Apply(
                (Color)ColorConverter.ConvertFromString("#7C6AE0")!,
                (Color)ColorConverter.ConvertFromString("#8F7FF0")!,
                (Color)ColorConverter.ConvertFromString("#6552D0")!,
                (Color)ColorConverter.ConvertFromString("#4C3BAE")!);

            var iconUri = new Uri("pack://application:,,,/Resources/pendulum.ico", UriKind.Absolute);
            var iconImage = new BitmapImage(iconUri);

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Pendulum",
                IconSource = iconImage,
                ContextMenu = BuildTrayMenu(),
                Visibility = Visibility.Visible
            };
            _trayIcon.TrayLeftMouseUp += (_, __) => ShowMainWindow();
            _trayIcon.ForceCreate();

            // The OS reads the tooltip text at the moment it's set, not live on hover,
            // so keep it refreshed periodically rather than computing it on click/hover.
            UpdateTrayTooltip();
            _trayTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _trayTooltipTimer.Tick += (_, __) => UpdateTrayTooltip();
            _trayTooltipTimer.Start();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            if (missed.Count > 0)
                NotifyMissedReminders(missed);
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnStartup", ex);
            MessageBox.Show(
                $"Pendulum failed to start and logged the details to Data\\crash.log.\n\n{ex.Message}",
                "Pendulum", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = $"Pendulum - {ReminderStatus.GetNextReminderSummary()}";
    }

    private void NotifyMissedReminders(List<string> missed)
    {
        var message = missed.Count switch
        {
            1 => $"\"{missed[0]}\" was missed while Pendulum was closed.",
            <= 3 => $"Missed while Pendulum was closed: {string.Join(", ", missed)}",
            _ => $"{missed.Count} reminders were missed while Pendulum was closed."
        };

        _trayIcon?.ShowNotification("Pendulum", message);
    }

    private void OnTriggerFired(Pendulum.Core.Models.TriggerTimer trigger)
    {
        Dispatcher.Invoke(() =>
        {
            var services = AppServices.Instance;
            AlertCoordinator.Fire(
                trigger.Name,
                trigger.TriggerAt.ToString("ddd, dd MMM yyyy  HH:mm"),
                trigger.SoundFileName,
                trigger.Mode,
                trigger.Phrase,
                canSnooze: true,
                volumePercent: trigger.Volume,
                onSnooze: () =>
                {
                    trigger.TriggerAt = DateTime.Now.AddMinutes(services.Settings.SnoozeMinutes);
                    trigger.HasFired = false;
                    services.RefreshTriggers();
                },
                onDismiss: () =>
                {
                    TriggerReconciler.AdvancePastFiring(trigger, DateTime.Now);
                    services.RefreshTriggers();
                });
        });
    }

    private ContextMenu BuildTrayMenu()
    {
        var openItem = new MenuItem { Header = "Open Pendulum" };
        openItem.Click += (_, __) => ShowMainWindow();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, __) => ExitApplication();

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    internal void ShowMainWindow()
    {
        if (MainWindow is null)
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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            _trayTooltipTimer?.Stop();
            _trayIcon?.Dispose();
            AppServices.Instance.Shutdown();
            _singleInstanceMutex?.ReleaseMutex();
        }
        base.OnExit(e);
    }
}
