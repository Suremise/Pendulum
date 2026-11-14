using NAudio.Wave;
using Pendulum.Core.Audio;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Engine;

public enum AlertStep
{
    Sound,
    Speech,
}

/// A sound or phrase that could not play; failures never close the alert.
public sealed record AlertProblem(AlertStep Step, Exception Error, bool FellBackToWindowsSpeech);

/// Owns an alert's players. Setup, synthesis, playback and disposal run on a worker.
/// Starts are serialized so an old cancellation cannot stop the new playback. Prepared
/// speech survives a queue handover and is released when this alert is disposed.
public class AlertPlaybackController : IDisposable
{
    private sealed class Run
    {
        public CancellationTokenSource Source { get; } = new();
        public Task Cancellation { get; set; } = Task.CompletedTask;
    }

    private readonly object _gate = new();
    private readonly Func<IAlertSound> _createAudio;
    private readonly Func<ITextToSpeechEngine> _createSpeech;
    private readonly Func<ITextToSpeechEngine>? _fallbackSpeech;
    private readonly Action<AlertProblem>? _onProblem;
    private readonly HashSet<AlertStep> _reported = [];
    private IAlertSound? _audio;
    private ITextToSpeechEngine? _speech;
    private ITextToSpeechEngine? _fallback;
    private SpeechAudio? _prepared;
    private string? _preparedPhrase;
    private bool _speechUnavailable;
    private bool _disposed;
    private Run? _run;
    private Task _loop = Task.CompletedTask;

    public AlertPlaybackController(
        IAlertSound audio,
        ITextToSpeechEngine speech,
        Func<ITextToSpeechEngine>? fallbackSpeech = null,
        Action<AlertProblem>? onProblem = null)
        : this(() => audio, () => speech, fallbackSpeech, onProblem)
    {
        _audio = audio;
        _speech = speech;
    }

    public AlertPlaybackController(
        Func<IAlertSound> createAudio,
        Func<ITextToSpeechEngine> createSpeech,
        Func<ITextToSpeechEngine>? fallbackSpeech = null,
        Action<AlertProblem>? onProblem = null)
    {
        _createAudio = createAudio;
        _createSpeech = createSpeech;
        _fallbackSpeech = fallbackSpeech;
        _onProblem = onProblem;
    }

    internal Task Running { get { lock (_gate) return _loop; } }

    /// speakOnce says the phrase on the first cycle only when a waiting alert gets its turn.
    public void Start(string? soundPath, AlertMode mode, string? phrase, bool repeatUntilDismissed, float volume = 1.0f, bool speakOnce = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCurrent();
            var previous = _loop;
            var run = new Run();
            _run = run;
            _loop = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    if (run.Source.IsCancellationRequested)
                        return;
                    _reported.Clear();
                    await RunLoopAsync(soundPath, mode, phrase, repeatUntilDismissed, volume, speakOnce, run.Source.Token).ConfigureAwait(false);
                }
                finally
                {
                    Task cancellation;
                    lock (_gate)
                    {
                        if (ReferenceEquals(_run, run)) _run = null;
                        cancellation = run.Cancellation;
                    }
                    try { await cancellation.ConfigureAwait(false); }
                    finally { run.Source.Dispose(); }
                }
            });
        }
    }

    private async Task RunLoopAsync(string? soundPath, AlertMode mode, string? phrase, bool repeat, float volume, bool speakOnce, CancellationToken token)
    {
        var spoken = false;
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();
                if (mode != AlertMode.SpeechOnly && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                    await PlaySoundAsync(soundPath, volume, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if ((mode == AlertMode.SoundAndSpeech || mode == AlertMode.SpeechOnly) && !string.IsNullOrWhiteSpace(phrase)
                    && !(speakOnce && spoken))
                {
                    await SpeakAsync(phrase, token).ConfigureAwait(false);
                    spoken = true;
                }
                if (token.IsCancellationRequested || !repeat)
                    break;
                await Task.Delay(400, token).ConfigureAwait(false);
            } while (!token.IsCancellationRequested);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // A dismissal or snooze cuts off the active step without reporting a failure.
        }
    }

    private IAlertSound Audio => _audio ??= _createAudio();

    private async Task PlaySoundAsync(string soundPath, float volume, CancellationToken token)
    {
        try { await Audio.PlayOnceAsync(soundPath, volume, token).ConfigureAwait(false); }
        catch (Exception e) when (!token.IsCancellationRequested) { Report(AlertStep.Sound, e, fellBack: false); }
    }

    private async Task PlaySpeechAsync(CancellationToken token)
    {
        if (_prepared is not { Wave.Length: > 0 } audio)
            return;
        using var stream = new WaveFileReader(new MemoryStream(audio.Wave, writable: false));
        await Audio.PlayOnceAsync(stream, audio.Volume, token).ConfigureAwait(false);
    }

    private async Task SpeakAsync(string phrase, CancellationToken token)
    {
        if (_preparedPhrase != phrase)
        {
            _preparedPhrase = phrase;
            _prepared = null;
            _speechUnavailable = false;
        }
        if (_speechUnavailable)
            return;
        try
        {
            var engine = _fallback ?? (_speech ??= _createSpeech());
            token.ThrowIfCancellationRequested();
            _prepared ??= await engine.SynthesizeAsync(phrase, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await PlaySpeechAsync(token).ConfigureAwait(false);
            return;
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            if (_fallback is not null || _fallbackSpeech is null)
            {
                _speechUnavailable = true;
                Report(AlertStep.Speech, e, fellBack: false);
                return;
            }
            try { _fallback = _fallbackSpeech(); }
            catch (Exception fallbackError)
            {
                _speechUnavailable = true;
                Report(AlertStep.Speech, new AggregateException(e, fallbackError), fellBack: false);
                return;
            }
            _prepared = null;
            Report(AlertStep.Speech, e, fellBack: true);
        }
        try
        {
            _prepared = await _fallback.SynthesizeAsync(phrase, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await PlaySpeechAsync(token).ConfigureAwait(false);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            _speechUnavailable = true;
            _reported.Remove(AlertStep.Speech);
            Report(AlertStep.Speech, e, fellBack: false);
        }
    }

    private void Report(AlertStep step, Exception error, bool fellBack)
    {
        if (!_reported.Add(step))
            return;
        try { _onProblem?.Invoke(new AlertProblem(step, error, fellBack)); }
        catch { /* Reporting must never stop the alert. */ }
    }

    // CancelAsync marks the token immediately, then runs device/process cancellation callbacks
    // off the caller's UI thread. The worker drains them before disposing its source or players.
    private void CancelCurrent()
    {
        if (_run is { } run && !run.Source.IsCancellationRequested)
            run.Cancellation = run.Source.CancelAsync();
    }

    public void Stop()
    {
        lock (_gate) CancelCurrent();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelCurrent();
            var previous = _loop;
            _loop = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); }
                finally
                {
                    _prepared = null;
                    try { (_audio as IDisposable)?.Dispose(); }
                    finally
                    {
                        try { _speech?.Dispose(); }
                        finally { _fallback?.Dispose(); }
                    }
                }
            });
        }
    }
}
