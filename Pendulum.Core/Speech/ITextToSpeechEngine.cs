namespace Pendulum.Core.Speech;

/// Common shape shared by the Windows (SpeechService) and Piper (PiperSpeechService) text-to-speech
/// engines, so AlertPlaybackController and its callers can swap between them via a single setting
/// without caring which one is actually doing the talking.
public interface ITextToSpeechEngine : IDisposable
{
    Task SpeakAndWaitAsync(string phrase, CancellationToken token);
    void Stop();
}
