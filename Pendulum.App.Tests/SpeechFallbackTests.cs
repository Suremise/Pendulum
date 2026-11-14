using System.Collections.Concurrent;
using System.IO;
using Pendulum.App.Services;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class SpeechFallbackTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-speech-tests-" + Guid.NewGuid().ToString("N"));

    public SpeechFallbackTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Only_an_available_Piper_primary_can_fall_back_to_Windows(bool executableExists, bool modelExists)
    {
        var piperAvailable = executableExists && modelExists;
        var settings = new AppSettings
        {
            TextToSpeechEngine = TextToSpeechEngine.Piper,
            PiperVoiceModelFileName = "voice.onnx",
            TtsRate = 4,
            TtsVolume = 60,
        };
        AppServices.Instance.UseSettingsForTests(settings);
        var windowsMade = 0;
        var piperMade = 0;
        var windows = new Speech(fails: !piperAvailable);
        var piper = new Speech(fails: true);
        var problems = new ConcurrentQueue<AlertProblem>();
        var playback = AppServices.Instance.CreatePlayback(problems.Enqueue,
            () => throw new InvalidOperationException("no audio expected"),
            () => { Interlocked.Increment(ref windowsMade); return windows; },
            (exe, model, rate, volume) =>
            {
                Assert.Equal(Path.Combine(_base, "Data", "PiperModels", "piper.exe"), exe);
                Assert.Equal(Path.Combine(_base, "Data", "PiperModels", "voice.onnx"), model);
                Assert.Equal(0.8f, rate);
                Assert.Equal(0.6f, volume);
                Interlocked.Increment(ref piperMade);
                return piper;
            });
        try
        {
            // Settings are captured now, but availability and all engine setup wait for playback.
            Assert.Equal(0, windowsMade);
            Assert.Equal(0, piperMade);
            settings.TextToSpeechEngine = TextToSpeechEngine.WindowsSpeechSynthesis;
            settings.PiperVoiceModelFileName = "changed.onnx";
            settings.TtsRate = -4;
            settings.TtsVolume = 10;
            if (executableExists) File.WriteAllText(Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe"), "");
            if (modelExists) File.WriteAllText(Path.Combine(AppPaths.PiperModelsDirectory, "voice.onnx"), "");

            playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: false);
            await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(1, windowsMade);
            Assert.Equal(1, windows.Attempts);
            Assert.Equal(piperAvailable ? 1 : 0, piperMade);
            Assert.Equal(piperAvailable ? 1 : 0, piper.Attempts);
            var problem = Assert.Single(problems);
            Assert.Equal(AlertStep.Speech, problem.Step);
            Assert.Equal(piperAvailable, problem.FellBackToWindowsSpeech);
        }
        finally
        {
            playback.Dispose();
            await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.Equal(1, windows.Disposals);
        Assert.Equal(piperAvailable ? 1 : 0, piper.Disposals);
    }

    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        Directory.Delete(_base, recursive: true);
    }

    private sealed class Speech(bool fails) : ITextToSpeechEngine
    {
        public int Attempts { get; private set; }
        public int Disposals { get; private set; }

        public Task<SpeechAudio> SynthesizeAsync(string phrase, CancellationToken token)
        {
            Attempts++;
            return fails
                ? Task.FromException<SpeechAudio>(new InvalidOperationException("synthesis failed"))
                : Task.FromResult(new SpeechAudio([], 1));
        }

        public async Task SpeakAndWaitAsync(string phrase, CancellationToken token) => await SynthesizeAsync(phrase, token);
        public void Stop() { }
        public void Dispose() => Disposals++;
    }
}
