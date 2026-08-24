using System.Windows.Threading;
using Pendulum.App.Views;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

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

        // Each fired alert gets its own audio/speech stack instead of the shared AppServices
        // singletons — a shared controller's Start() stops whatever it was already playing, so
        // two reminders firing close together would silently cut each other's sound/speech off.
        var audio = new AudioService();
        var speech = new SpeechService();
        speech.SetVoice(services.Settings.TtsVoiceName);
        speech.SetRate(services.Settings.TtsRate);
        speech.SetVolume(services.Settings.TtsVolume);
        var playback = new AlertPlaybackController(audio, speech);
        playback.Start(soundPath, mode, phrase, repeat, volume);

        var window = new AlertWindow(title, subtitle, canSnooze);
        window.Dismissed += () => onDismiss?.Invoke();
        window.Snoozed += () => onSnooze?.Invoke();
        window.Closed += (_, __) =>
        {
            playback.Dispose();
            audio.Dispose();
            speech.Dispose();
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
                onDismiss?.Invoke();
                window.Close();
            };
            window.Closed += (_, __) => autoResolveTimer.Stop();
            autoResolveTimer.Start();
        }

        window.Show();
    }
}
