using System.Windows.Threading;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Services;

public static class AlertCoordinator
{
    public static void Fire(
        string title,
        string subtitle,
        string? soundFileName,
        AlertMode mode,
        string? phrase,
        bool canSnooze,
        Action? onSnooze,
        Action? onDismiss,
        int volumePercent = 100)
    {
        var services = AppServices.Instance;
        var soundPath = services.ResolveSoundPath(soundFileName);
        var repeat = services.Settings.RepeatAlertUntilDismissed;
        var volume = Math.Clamp(volumePercent, 0, 100) / 100f;

        services.AlertPlayback.Start(soundPath, mode, phrase, repeat, volume);

        var window = new AlertWindow(title, subtitle, canSnooze);
        window.Dismissed += () =>
        {
            services.AlertPlayback.Stop();
            onDismiss?.Invoke();
        };
        window.Snoozed += () =>
        {
            services.AlertPlayback.Stop();
            onSnooze?.Invoke();
        };

        // A non-repeating alert plays its sound/phrase once and then just sits there —
        // if nobody's at the desk to click it, resolve it the same way Dismiss would
        // (reschedule if recurring, else mark spent) instead of leaving it stuck forever.
        if (!repeat && services.Settings.AutoResolveMinutes > 0)
        {
            var autoResolveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(services.Settings.AutoResolveMinutes) };
            autoResolveTimer.Tick += (_, __) =>
            {
                autoResolveTimer.Stop();
                services.AlertPlayback.Stop();
                onDismiss?.Invoke();
                window.Close();
            };
            window.Closed += (_, __) => autoResolveTimer.Stop();
            autoResolveTimer.Start();
        }

        window.Show();
    }
}
