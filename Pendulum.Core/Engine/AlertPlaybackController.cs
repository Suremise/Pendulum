using Pendulum.Core.Audio;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Engine;

/// Coordinates sound + optional spoken phrase for a firing alert, looping
/// the sound-then-phrase cycle until Stop() is called (dismiss/snooze).
public class AlertPlaybackController : IDisposable
{
    private readonly AudioService _audio;
    private readonly ITextToSpeechEngine _speech;
    private CancellationTokenSource? _cts;

    public AlertPlaybackController(AudioService audio, ITextToSpeechEngine speech)
    {
        _audio = audio;
        _speech = speech;
    }

    public void Start(string? soundPath, AlertMode mode, string? phrase, bool repeatUntilDismissed, float volume = 1.0f)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(soundPath, mode, phrase, repeatUntilDismissed, volume, _cts.Token);
    }

    private async Task RunLoopAsync(string? soundPath, AlertMode mode, string? phrase, bool repeat, float volume, CancellationToken token)
    {
        try
        {
            do
            {
                if (mode != AlertMode.SpeechOnly && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                    await _audio.PlayOnceAsync(soundPath, volume, token);

                if (token.IsCancellationRequested)
                    break;

                if ((mode == AlertMode.SoundAndSpeech || mode == AlertMode.SpeechOnly) && !string.IsNullOrWhiteSpace(phrase))
                    await _speech.SpeakAndWaitAsync(phrase, token);

                if (token.IsCancellationRequested || !repeat)
                    break;

                await Task.Delay(400, token);
            } while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // expected when Stop() cancels the token mid-delay.
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _audio.Stop();
        _speech.Stop();
    }

    public void Dispose() => Stop();
}
