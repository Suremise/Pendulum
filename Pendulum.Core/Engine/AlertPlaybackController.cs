using Pendulum.Core.Audio;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Engine;

/// Which part of an alert failed.
public enum AlertStep
{
    Sound,
    Speech,
}

/// A sound or phrase that couldn't be played. FellBackToWindowsSpeech: Piper failed and the
/// Windows voice speaks the phrase from now on.
public sealed record AlertProblem(AlertStep Step, Exception Error, bool FellBackToWindowsSpeech);

/// Coordinates sound + optional spoken phrase for a firing alert, looping
/// the sound-then-phrase cycle until Stop() is called (dismiss/snooze).
///
/// A failing step never ends the alert: a sound that can't play still leaves the phrase, and a
/// phrase that can't be spoken still leaves the sound. When the speech engine fails and a
/// fallback is given (the Windows voice, for Piper), the fallback speaks from then on. Each kind
/// of failure is reported once per Start, through onProblem.
public class AlertPlaybackController : IDisposable
{
    private readonly IAlertSound _audio;
    private readonly ITextToSpeechEngine _speech;
    private readonly Func<ITextToSpeechEngine>? _fallbackSpeech;
    private readonly Action<AlertProblem>? _onProblem;
    private readonly HashSet<AlertStep> _reported = [];
    private ITextToSpeechEngine? _fallback;
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;

    public AlertPlaybackController(
        IAlertSound audio,
        ITextToSpeechEngine speech,
        Func<ITextToSpeechEngine>? fallbackSpeech = null,
        Action<AlertProblem>? onProblem = null)
    {
        _audio = audio;
        _speech = speech;
        _fallbackSpeech = fallbackSpeech;
        _onProblem = onProblem;
    }

    /// The running loop, for tests: it completes once the alert has finished or been stopped.
    internal Task Running => _loop;

    /// Starts the sound-then-phrase cycle. <paramref name="speakOnce"/>: the phrase is spoken on the
    /// first cycle only (the sound still repeats): for an alert whose turn comes after others were
    /// dealt with, which says what it is once rather than over and over.
    public void Start(string? soundPath, AlertMode mode, string? phrase, bool repeatUntilDismissed, float volume = 1.0f, bool speakOnce = false)
    {
        Stop();
        _reported.Clear();
        _cts = new CancellationTokenSource();
        _loop = RunLoopAsync(soundPath, mode, phrase, repeatUntilDismissed, volume, speakOnce, _cts.Token);
    }

    private async Task RunLoopAsync(string? soundPath, AlertMode mode, string? phrase, bool repeat, float volume, bool speakOnce, CancellationToken token)
    {
        var spoken = false;
        try
        {
            do
            {
                if (mode != AlertMode.SpeechOnly && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                    await PlaySoundAsync(soundPath, volume, token);

                if (token.IsCancellationRequested)
                    break;

                if ((mode == AlertMode.SoundAndSpeech || mode == AlertMode.SpeechOnly) && !string.IsNullOrWhiteSpace(phrase)
                    && !(speakOnce && spoken))
                {
                    await SpeakAsync(phrase, token);
                    spoken = true;
                }

                if (token.IsCancellationRequested || !repeat)
                    break;

                await Task.Delay(400, token);
            } while (!token.IsCancellationRequested);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Stopped (dismissed or snoozed) mid-step or mid-delay: whatever the step threw as it
            // was cut off doesn't matter.
        }
    }

    private async Task PlaySoundAsync(string soundPath, float volume, CancellationToken token)
    {
        try
        {
            await _audio.PlayOnceAsync(soundPath, volume, token);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            Report(AlertStep.Sound, e, fellBack: false);
        }
    }

    private async Task SpeakAsync(string phrase, CancellationToken token)
    {
        var engine = _fallback ?? _speech;
        try
        {
            await engine.SpeakAndWaitAsync(phrase, token);
            return;
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            if (_fallback is not null || _fallbackSpeech is null)
            {
                Report(AlertStep.Speech, e, fellBack: false);
                return;
            }

            ITextToSpeechEngine fallback;
            try
            {
                fallback = _fallbackSpeech();
            }
            catch (Exception fallbackError)
            {
                Report(AlertStep.Speech, new AggregateException(e, fallbackError), fellBack: false);
                return;
            }

            _fallback = fallback;
            Report(AlertStep.Speech, e, fellBack: true);
        }

        try
        {
            await _fallback.SpeakAndWaitAsync(phrase, token);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            // The fallback failed too: reported even though the first failure already was.
            _reported.Remove(AlertStep.Speech);
            Report(AlertStep.Speech, e, fellBack: false);
        }
    }

    private void Report(AlertStep step, Exception error, bool fellBack)
    {
        if (!_reported.Add(step))
            return;
        try
        {
            _onProblem?.Invoke(new AlertProblem(step, error, fellBack));
        }
        catch
        {
            // Telling about a problem must never stop the alert.
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _audio.Stop();
        _speech.Stop();
        _fallback?.Stop();
    }

    public void Dispose()
    {
        Stop();
        _fallback?.Dispose();
        _fallback = null;
    }
}
