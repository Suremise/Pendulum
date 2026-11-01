using NAudio;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Speech;

/// Why speaking a reminder didn't work, so Quick Reminder can say the right thing.
public enum DictationProblem
{
    /// No microphone, or it stopped (unplugged, say) while listening.
    Microphone,
    /// The Whisper model chosen in Settings isn't in Data\WhisperModels\ any more.
    WhisperModelMissing,
    /// The Whisper model is there but couldn't be loaded (damaged, or not a Whisper model).
    WhisperModelUnreadable,
    /// Windows has no speech recognizer for dictation.
    RecognizerMissing,
    /// Whisper's native libraries can't load: Microsoft's Visual C++ runtime they need isn't
    /// beside Pendulum or in Windows (see WhisperRuntime).
    WhisperRuntimeMissing,
    /// Anything else.
    Failed,
}

/// Speaking a reminder failed for a known reason.
public sealed class DictationException(DictationProblem problem, Exception? inner = null)
    : Exception(problem.ToString(), inner)  // not user-visible: Quick Reminder shows its own text
{
    public DictationProblem Problem { get; } = problem;
}

/// What Quick Reminder listens with, decided before it starts listening.
public sealed record DictationChoice(bool UseWhisper, string? WhisperModelPath, DictationProblem? Problem)
{
    /// Whisper when it's chosen and has a model named; the Windows recognizer otherwise, as
    /// before. A named model that isn't in its folder any more is a problem to report before
    /// listening, not after the user has spoken. So is a Visual C++ runtime that's missing, when
    /// <paramref name="missingWhisperRuntime"/> is given (the app passes WhisperRuntime's check).
    public static DictationChoice For(SpeechToTextEngine engine, string? whisperModelFileName, string whisperModelsDirectory,
        Func<IReadOnlyList<string>>? missingWhisperRuntime = null)
    {
        if (engine != SpeechToTextEngine.Whisper)
            return new DictationChoice(false, null, null);

        var path = DataFileName.PathInside(whisperModelsDirectory, whisperModelFileName);
        if (path is null)
            return new DictationChoice(false, null, null);

        if (!File.Exists(path))
            return new DictationChoice(true, path, DictationProblem.WhisperModelMissing);

        return missingWhisperRuntime?.Invoke() is { Count: > 0 }
            ? new DictationChoice(true, path, DictationProblem.WhisperRuntimeMissing)
            : new DictationChoice(true, path, null);
    }

    /// The problem behind a failure while listening.
    public static DictationProblem Classify(Exception error) => error switch
    {
        DictationException d => d.Problem,
        MmException => DictationProblem.Microphone,
        DllNotFoundException => DictationProblem.WhisperRuntimeMissing,
        _ => DictationProblem.Failed,
    };
}
