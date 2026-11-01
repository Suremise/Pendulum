using System.Windows.Threading;
using Pendulum.App.Views;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.App.Services;

public static class AlertCoordinator
{
    /// One open alert: its playback and its window, and how it plays when it's its turn.
    private sealed class Session(AlertPlaybackController playback, AlertWindow window, string? soundPath, AlertMode mode, string? phrase, bool repeat, float volume)
    {
        public AlertWindow Window => window;

        /// Its turn: sound and speech, the phrase once if it waited for its turn.
        public void Ring(bool turnCame)
        {
            window.SetWaiting(false);
            playback.Start(soundPath, mode, phrase, repeat, volume, speakOnce: turnCame);
        }

        /// A newer alert rings: this one waits, silent and still on screen.
        public void Silence()
        {
            playback.Stop();
            window.SetWaiting(true);
        }
    }

    // Only the newest open alert makes a sound; dismissing or snoozing it hands the sound on.
    private static readonly AlertQueue<Session> Queue = new();

    private static void Apply(AlertQueue<Session>.Handover change)
    {
        change.Silence?.Silence();
        change.Ring?.Ring(change.TurnCame);
    }

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
        var speech = services.CreateSpeechEngine();
        // A sound or phrase that fails never stops the alert: the rest carries on, Piper falls
        // back to the Windows voice, and the reason goes to Data\crash.log.
        var playback = new AlertPlaybackController(
            audio,
            speech,
            speech is PiperSpeechService ? services.CreateWindowsSpeechEngine : null,
            problem => CrashLog.Write($"Alert {problem.Step}{(problem.FellBackToWindowsSpeech ? " (fell back to the Windows voice)" : "")}", problem.Error));  // not user-visible
        var window = new AlertWindow(title, subtitle, canSnooze);
        var session = new Session(playback, window, soundPath, mode, phrase, repeat, volume);
        window.Dismissed += () => onDismiss?.Invoke();
        window.Snoozed += () => onSnooze?.Invoke();
        window.Closed += (_, __) =>
        {
            playback.Dispose();
            audio.Dispose();
            speech.Dispose();
            Apply(Queue.Close(session));
        };
        Apply(Queue.Open(session));

        // A non-repeating alert plays its sound/phrase once and then just sits there —
        // if nobody's at the desk to click it, resolve it the same way Dismiss would
        // (reschedule if recurring, else mark spent) instead of leaving it stuck forever.
        if (TriggerReconciler.AutoResolveDelay(services.Settings) is { } autoResolveDelay)
        {
            var autoResolveTimer = new DispatcherTimer { Interval = autoResolveDelay };
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
