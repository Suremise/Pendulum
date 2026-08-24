using System.Speech.Recognition;

namespace Pendulum.Core.Speech;

/// One-shot dictation: listens for a single spoken phrase via the OS's default speech
/// recognizer and default microphone, and returns the recognized text. Mirrors
/// SpeechService's async-wraps-event pattern (a TaskCompletionSource around the underlying
/// EAP events) so callers can just await it and have the continuation land back wherever
/// they awaited from, without needing to marshal threads themselves.
public sealed class SpeechRecognitionService : IDisposable
{
    private SpeechRecognitionEngine? _engine;

    /// Listens for one spoken phrase and returns the recognized text, or null if nothing was
    /// understood. Throws if no microphone or speech recognizer is available on this machine.
    public async Task<string?> ListenOnceAsync(CancellationToken token)
    {
        Stop();

        var engine = new SpeechRecognitionEngine();
        _engine = engine;
        engine.SetInputToDefaultAudioDevice();
        engine.LoadGrammar(new DictationGrammar());

        // Defaults leave InitialSilenceTimeout unbounded (waits forever for speech to start)
        // and EndSilenceTimeout very short (~0.15s), which risks both hanging indefinitely on
        // an accidental click and cutting a sentence off at a natural pause mid-phrase.
        engine.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
        engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);

        var tcs = new TaskCompletionSource<string?>();

        void OnSpeechRecognized(object? s, SpeechRecognizedEventArgs e) =>
            tcs.TrySetResult(e.Result?.Text);

        void OnRecognizeCompleted(object? s, RecognizeCompletedEventArgs e)
        {
            if (e.Error is not null)
                tcs.TrySetException(e.Error);
            else
                tcs.TrySetResult(null);
        }

        engine.SpeechRecognized += OnSpeechRecognized;
        engine.RecognizeCompleted += OnRecognizeCompleted;

        using var registration = token.Register(() => tcs.TrySetCanceled(token));

        engine.RecognizeAsync(RecognizeMode.Single);

        try
        {
            return await tcs.Task;
        }
        finally
        {
            engine.SpeechRecognized -= OnSpeechRecognized;
            engine.RecognizeCompleted -= OnRecognizeCompleted;
        }
    }

    public void Stop()
    {
        if (_engine is null)
            return;

        _engine.RecognizeAsyncCancel();
        _engine.Dispose();
        _engine = null;
    }

    public void Dispose() => Stop();
}
