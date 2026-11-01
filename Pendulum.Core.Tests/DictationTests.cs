using NAudio;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Tests;

/// Speaking a reminder: what Quick Reminder listens with, and how a failure is told apart. No
/// microphone is opened: the tests that reach Whisper fail before recording starts, or skip it.
public sealed class DictationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Windows_speech_needs_no_model()
    {
        var choice = DictationChoice.For(SpeechToTextEngine.WindowsSpeechRecognition, "ggml-base.en.bin", _temp.Path);
        Assert.Equal(new DictationChoice(false, null, null), choice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"..\elsewhere.bin")]
    public void Whisper_with_no_model_named_listens_with_Windows_as_before(string? model)
    {
        var choice = DictationChoice.For(SpeechToTextEngine.Whisper, model, _temp.Path);
        Assert.Equal(new DictationChoice(false, null, null), choice);
    }

    [Fact]
    public void Whisper_with_its_model_in_place_uses_it()
    {
        File.WriteAllText(_temp.File("ggml-base.en.bin"), "");
        var choice = DictationChoice.For(SpeechToTextEngine.Whisper, "ggml-base.en.bin", _temp.Path);
        Assert.Equal(new DictationChoice(true, _temp.File("ggml-base.en.bin"), null), choice);
    }

    [Fact]
    public void A_Whisper_model_that_was_deleted_is_reported_before_listening()
    {
        var choice = DictationChoice.For(SpeechToTextEngine.Whisper, "ggml-base.en.bin", _temp.Path);
        Assert.Equal(DictationProblem.WhisperModelMissing, choice.Problem);
    }

    [Theory]
    [InlineData(DictationProblem.Microphone)]
    [InlineData(DictationProblem.WhisperModelMissing)]
    [InlineData(DictationProblem.WhisperModelUnreadable)]
    [InlineData(DictationProblem.RecognizerMissing)]
    public void A_known_failure_keeps_its_reason(DictationProblem problem) =>
        Assert.Equal(problem, DictationChoice.Classify(new DictationException(problem, new InvalidOperationException())));

    [Fact]
    public void A_sound_device_error_is_a_microphone_problem() =>
        Assert.Equal(DictationProblem.Microphone, DictationChoice.Classify(new MmException(MmResult.NoDriver, "waveInOpen")));

    [Fact]
    public void Anything_else_isnt_blamed_on_the_microphone() =>
        Assert.Equal(DictationProblem.Failed, DictationChoice.Classify(new InvalidOperationException("boom")));

    [Fact]
    public async Task Whisper_says_its_model_is_missing_before_it_records()
    {
        using var whisper = new WhisperRecognitionService();
        var recordingStopped = false;

        var e = await Assert.ThrowsAsync<DictationException>(() =>
            whisper.ListenOnceAsync(_temp.File("gone.bin"), () => recordingStopped = true, CancellationToken.None));

        Assert.Equal(DictationProblem.WhisperModelMissing, e.Problem);
        Assert.False(recordingStopped);  // it never got as far as recording
    }

    [Fact]
    public async Task A_damaged_Whisper_model_is_reported_as_unreadable()
    {
        var model = _temp.File("ggml-damaged.bin");
        File.WriteAllBytes(model, new byte[4096]);

        var e = await Assert.ThrowsAsync<DictationException>(() =>
            WhisperRecognitionService.TranscribeAsync(model, new byte[3200], CancellationToken.None));

        Assert.Equal(DictationProblem.WhisperModelUnreadable, e.Problem);
    }
}
