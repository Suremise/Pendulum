using System.Speech.Synthesis;
using NAudio.Wave;
using Pendulum.Core.Audio;

namespace Pendulum.Core.Speech;

public class SpeechService : IDisposable, ITextToSpeechEngine
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly AudioService _audio = new();

    /// The names of the Windows voices installed and enabled, for Settings to offer.
    public static IReadOnlyList<string> GetVoiceNames()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo.Name)
            .ToList();
    }

    public void SetVoice(string? voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
            return;

        try
        {
            _synth.SelectVoice(voiceName);
        }
        catch (ArgumentException)
        {
            // voice no longer installed; fall back to default.
        }
    }

    public void SetRate(int rate) => _synth.Rate = Math.Clamp(rate, -10, 10);

    public void SetVolume(int volume) => _synth.Volume = Math.Clamp(volume, 0, 100);

    public async Task SpeakAndWaitAsync(string phrase, CancellationToken token)
    {
        var audio = await SynthesizeAsync(phrase, token).ConfigureAwait(false);
        if (audio.Wave.Length == 0)
            return;
        using var stream = new WaveFileReader(new MemoryStream(audio.Wave, writable: false));
        await _audio.PlayOnceAsync(stream, audio.Volume, token).ConfigureAwait(false);
    }

    public async Task<SpeechAudio> SynthesizeAsync(string phrase, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return new SpeechAudio([], 1f);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wave = new MemoryStream();

        void OnCompleted(object? s, SpeakCompletedEventArgs e)
        {
            if (e.Error is { } error) tcs.TrySetException(error);
            else if (e.Cancelled) tcs.TrySetCanceled();
            else tcs.TrySetResult();
        }
        _synth.SpeakCompleted += OnCompleted;

        try
        {
            token.ThrowIfCancellationRequested();
            _synth.SetOutputToWaveStream(wave);
            _synth.SpeakAsync(phrase);
            using var registration = token.Register(() => _synth.SpeakAsyncCancelAll());
            await tcs.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // SAPI applies its volume while synthesizing, so playback uses unity gain.
            return new SpeechAudio(wave.ToArray(), 1f);
        }
        finally
        {
            _synth.SpeakCompleted -= OnCompleted;
            _synth.SetOutputToNull();
        }
    }

    public void Stop()
    {
        _synth.SpeakAsyncCancelAll();
        _audio.Stop();
    }

    public void Dispose()
    {
        _synth.Dispose();
        _audio.Dispose();
    }
}
