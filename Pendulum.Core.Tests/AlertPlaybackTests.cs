using System.Collections.Concurrent;
using System.Diagnostics;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;
using NAudio.Wave;

namespace Pendulum.Core.Tests;

/// An alert whose sound or speech fails keeps going, and says why. Fakes stand in for the sound
/// and the voices, so nothing is played; the sound file is an empty temp file.
public sealed class AlertPlaybackTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _sound;

    public AlertPlaybackTests()
    {
        _sound = _temp.File("chime.wav");
        File.WriteAllText(_sound, "");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_sound_that_fails_still_leaves_the_phrase_repeating()
    {
        var sound = new FakeSound { Fails = true };
        var speech = new FakeSpeech();
        var problems = new ConcurrentQueue<AlertProblem>();
        using var playback = new AlertPlaybackController(sound, speech, onProblem: problems.Enqueue);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true);
        await WaitUntil(() => speech.Spoken.Count >= 3);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(speech.Spoken, p => Assert.Equal("Dentist", p));
        Assert.True(sound.Attempts >= 3);
        var problem = Assert.Single(problems);  // told once, not every round
        Assert.Equal(AlertStep.Sound, problem.Step);
        Assert.IsType<InvalidOperationException>(problem.Error);
    }

    [Fact]
    public async Task A_phrase_that_fails_still_leaves_the_sound_repeating()
    {
        var sound = new FakeSound();
        var speech = new FakeSpeech { Fails = true };
        var problems = new ConcurrentQueue<AlertProblem>();
        using var playback = new AlertPlaybackController(sound, speech, onProblem: problems.Enqueue);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true);
        await WaitUntil(() => sound.Attempts >= 3);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        var problem = Assert.Single(problems);
        Assert.Equal(AlertStep.Speech, problem.Step);
        Assert.False(problem.FellBackToWindowsSpeech);
    }

    [Fact]
    public async Task When_Piper_fails_the_fallback_voice_speaks_from_then_on()
    {
        var piper = new FakeSpeech { Fails = true };
        var windows = new FakeSpeech();
        var problems = new ConcurrentQueue<AlertProblem>();
        var fallbacksMade = 0;
        using var playback = new AlertPlaybackController(
            new FakeSound(), piper, () => { fallbacksMade++; return windows; }, problems.Enqueue);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true);
        await WaitUntil(() => windows.Spoken.Count >= 3);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, piper.Attempts);  // tried once, then the Windows voice took over
        Assert.Equal(1, fallbacksMade);
        var problem = Assert.Single(problems);
        Assert.Equal(AlertStep.Speech, problem.Step);
        Assert.True(problem.FellBackToWindowsSpeech);
    }

    [Fact]
    public async Task A_failing_alert_that_doesnt_repeat_plays_once_and_ends()
    {
        var sound = new FakeSound { Fails = true };
        var speech = new FakeSpeech();
        using var playback = new AlertPlaybackController(sound, speech);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: false);
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, sound.Attempts);
        Assert.Equal(["Dentist"], speech.Spoken);
    }

    [Fact]
    public async Task A_problem_handler_that_throws_never_stops_the_alert()
    {
        var sound = new FakeSound { Fails = true };
        var speech = new FakeSpeech();
        using var playback = new AlertPlaybackController(sound, speech, onProblem: _ => throw new InvalidOperationException("handler"));

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true);
        await WaitUntil(() => speech.Spoken.Count >= 2);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_sound_device_that_fails_mid_play_is_a_sound_problem()
    {
        // What NAudio hands over in PlaybackStopped when the output fails after Play.
        var playback = new TaskCompletionSource();
        AudioService.Finish(playback, new StoppedEventArgs(new InvalidOperationException("device removed")));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => playback.Task);
        Assert.Equal("device removed", e.Message);
    }

    [Fact]
    public void A_sound_that_ends_or_is_stopped_finishes_normally()
    {
        var playback = new TaskCompletionSource();
        AudioService.Finish(playback, new StoppedEventArgs());
        Assert.True(playback.Task.IsCompletedSuccessfully);
    }

    // ---- An alert whose turn has come: says what it is once, then keeps sounding ----

    [Fact]
    public async Task Speaking_once_says_the_phrase_on_the_first_cycle_and_the_sound_goes_on()
    {
        var sound = new FakeSound();
        var speech = new FakeSpeech();
        using var playback = new AlertPlaybackController(sound, speech);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true, speakOnce: true);
        await WaitUntil(() => sound.Attempts >= 4);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(new[] { "Dentist" }, speech.Spoken);
    }

    [Fact]
    public async Task Without_speaking_once_the_phrase_repeats_as_before()
    {
        var sound = new FakeSound();
        var speech = new FakeSpeech();
        using var playback = new AlertPlaybackController(sound, speech);

        playback.Start(_sound, AlertMode.SoundAndSpeech, "Dentist", repeatUntilDismissed: true);
        await WaitUntil(() => speech.Spoken.Count >= 3);
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(speech.Spoken.Count >= 3);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "the condition never came true");
            await Task.Delay(20);
        }
    }

    private sealed class FakeSound : IAlertSound
    {
        private int _attempts;
        public bool Fails { get; init; }
        public int Attempts => Volatile.Read(ref _attempts);

        public Task PlayOnceAsync(string filePath, float volume, CancellationToken token)
        {
            Interlocked.Increment(ref _attempts);
            return Fails ? Task.FromException(new InvalidOperationException("no output device")) : Task.CompletedTask;
        }

        public void Stop() { }
    }

    private sealed class FakeSpeech : ITextToSpeechEngine
    {
        private int _attempts;
        public bool Fails { get; init; }
        public int Attempts => Volatile.Read(ref _attempts);
        public ConcurrentQueue<string> Spoken { get; } = new();

        public Task SpeakAndWaitAsync(string phrase, CancellationToken token)
        {
            Interlocked.Increment(ref _attempts);
            if (Fails)
                return Task.FromException(new InvalidOperationException("piper failed"));
            Spoken.Enqueue(phrase);
            return Task.CompletedTask;
        }

        public void Stop() { }
        public void Dispose() { }
    }
}
