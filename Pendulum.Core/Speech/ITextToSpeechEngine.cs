namespace Pendulum.Core.Speech;

/// Common shape shared by the Windows (SpeechService) and Piper (PiperSpeechService) text-to-speech
/// engines, so AlertPlaybackController and its callers can swap between them via a single setting
/// without caring which one is actually doing the talking.
public interface ITextToSpeechEngine : IDisposable
{
    Task<SpeechAudio> SynthesizeAsync(string phrase, CancellationToken token);
    Task SpeakAndWaitAsync(string phrase, CancellationToken token);
    void Stop();
}

/// A phrase prepared once, with its engine's volume, ready to replay without synthesis.
public sealed record SpeechAudio(byte[] Wave, float Volume);
