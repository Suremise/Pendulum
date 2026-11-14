using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Pendulum.App.Resources;

namespace Pendulum.App.Services;

/// Owns the tray icon, its menu, notifications and tooltip refresh for the app's lifetime.
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly TrayTooltipUpdater _tooltip;
    private readonly DispatcherTimer _timer;
    private readonly Func<string?> _shortcut;
    private bool _disposed;

    public TrayIcon(Action open, Action exit, Func<string?> shortcut)
    {
        _shortcut = shortcut;
        _trayIcon = new TaskbarIcon();
        try
        {
            _trayIcon.ToolTipText = Strings.App_Name;
            _trayIcon.IconSource = new BitmapImage(new Uri("pack://application:,,,/Resources/tray.ico", UriKind.Absolute));
            _trayIcon.ContextMenu = BuildMenu(open, exit);
            _trayIcon.Visibility = Visibility.Visible;
            _trayIcon.TrayLeftMouseUp += (_, _) => open();
            // The library defaults to process-wide Idle priority and EcoQoS. Reminders and
            // speech need normal scheduling even when Pendulum is hidden.
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
            _tooltip = new TrayTooltipUpdater(text => _trayIcon.ToolTipText = text,
                ex => CrashLog.Write("TrayTooltip.Update", ex)); // not user-visible
            // Explorer reads the tooltip at the moment it is set. Refresh the countdown,
            // but TrayTooltipUpdater skips unchanged text and contains shell failures.
            RefreshTooltip();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) => RefreshTooltip();
            _timer.Start();
        }
        catch
        {
            _timer?.Stop();
            _trayIcon.Dispose();
            throw;
        }
    }

    public void RefreshTooltip()
    {
        if (!_disposed)
            _tooltip.Update(TrayTooltip.Build(ReminderStatus.GetNextReminderSummary(), _shortcut()));
    }

    public void ShowNotification(string message)
    {
        if (!_disposed) _trayIcon.ShowNotification(Strings.App_Name, message);
    }

    public void NotifyMissedReminders(IReadOnlyList<string> missed)
    {
        var message = missed.Count switch
        {
            1 => string.Format(Strings.Tray_MissedOne, missed[0]),
            <= 3 => string.Format(Strings.Tray_MissedFew, string.Join(Strings.List_Separator, missed)),
            _ => string.Format(Strings.Tray_MissedMany, missed.Count),
        };
        ShowNotification(message);
    }

    internal static ContextMenu BuildMenu(Action open, Action exit)
    {
        var openItem = new MenuItem { Header = Strings.Tray_Open };
        openItem.Click += (_, _) => open();
        var exitItem = new MenuItem { Header = Strings.Tray_Exit };
        exitItem.Click += (_, _) => exit();
        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _trayIcon.Dispose();
    }
}
