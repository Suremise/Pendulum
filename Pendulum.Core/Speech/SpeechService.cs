using System.Speech.Synthesis;

namespace Pendulum.Core.Speech;

public class SpeechService : IDisposable, ITextToSpeechEngine
{
    private readonly SpeechSynthesizer _synth = new();

    public IReadOnlyList<string> GetVoiceNames() =>
        _synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo.Name)
            .ToList();

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
        if (string.IsNullOrWhiteSpace(phrase))
            return;

        var tcs = new TaskCompletionSource();

        void OnCompleted(object? s, SpeakCompletedEventArgs e) => tcs.TrySetResult();
        _synth.SpeakCompleted += OnCompleted;

        using var registration = token.Register(() =>
        {
            _synth.SpeakAsyncCancelAll();
            tcs.TrySetResult();
        });

        _synth.SpeakAsync(phrase);

        try
        {
            await tcs.Task;
        }
        finally
        {
            _synth.SpeakCompleted -= OnCompleted;
        }
    }

    public void Stop() => _synth.SpeakAsyncCancelAll();

    public void Dispose() => _synth.Dispose();
}
