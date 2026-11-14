namespace Pendulum.Core.Speech;

/// Factories for the primary voice and, only when that voice supports it, a fallback.
/// Selecting them separately from construction preserves fallback if primary setup fails.
public sealed record SpeechEngineSelection(
    Func<ITextToSpeechEngine> Primary,
    Func<ITextToSpeechEngine>? Fallback = null);
