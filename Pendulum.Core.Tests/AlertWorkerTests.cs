using NAudio.Wave;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Tests;

public sealed class AlertWorkerTests
{
    [Fact]
    public async Task Sound_only_setup_and_playback_leave_the_callers_context_and_create_no_voice()
    {
        using var temp = new TempFolder();
        var path = temp.File("sound.wav");
        File.WriteAllBytes(path, []);
        var context = new SynchronizationContext();
        var original = SynchronizationContext.Current;
        var factories = 0;
        var sound = new Sound();
        using var playback = new AlertPlaybackController(() =>
        {
            Assert.NotSame(context, SynchronizationContext.Current);
            return sound;
        }, () => { Interlocked.Increment(ref factories); return new Speech(); });
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            playback.Start(path, AlertMode.SoundOnly, "unused", repeatUntilDismissed: false);
        }
        finally { SynchronizationContext.SetSynchronizationContext(original); }
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotSame(context, sound.Context);
        Assert.Equal(0, factories);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sound.Disposed);
    }

    [Fact]
    public async Task Prepared_speech_is_replayed_and_survives_a_queue_handover()
    {
        var speech = new Speech();
        var sound = new Sound();
        using var playback = new AlertPlaybackController(() => sound, () => speech);
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: true);
        await sound.ThirdPlayback.Task.WaitAsync(TimeSpan.FromSeconds(30));
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        var beforeHandover = sound.Plays;
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: false, speakOnce: true);
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, speech.Preparations);
        Assert.Equal(beforeHandover + 1, sound.Plays);
        Assert.Equal(0.4f, sound.Volume);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(speech.Disposed);
        Assert.True(sound.Disposed);
    }

    [Fact]
    public async Task Dismissing_cancels_active_playback_and_finishes_before_disposal()
    {
        var sound = new Sound { Block = true };
        using var playback = new AlertPlaybackController(() => sound, () => new Speech());
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: true);
        await sound.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sound.Cancelled);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sound.Disposed);
    }

    [Fact]
    public async Task A_restart_waits_for_the_cancelled_player_before_reusing_prepared_speech()
    {
        var sound = new Sound { Block = true };
        var speech = new Speech();
        using var playback = new AlertPlaybackController(() => sound, () => speech);
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: true);
        await sound.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: false);
        await sound.SecondPlayback.Task.WaitAsync(TimeSpan.FromSeconds(30));
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(sound.Overlapped);
        Assert.Equal(1, speech.Preparations);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Dismissal_returns_while_a_device_cancellation_callback_is_still_busy()
    {
        using var release = new ManualResetEventSlim();
        var sound = new Sound { Block = true, CancellationGate = release };
        using var playback = new AlertPlaybackController(() => sound, () => new Speech());
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: true);
        await sound.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            playback.Stop();
            playback.Stop(); // Repeated dismiss/close keeps waiting for the same callbacks.
            playback.Dispose();
            await sound.CancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(playback.Running.IsCompleted);
        }
        finally { release.Set(); }
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(sound.Cancelled);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Dismissing_during_synthesis_never_starts_audio()
    {
        var speech = new Speech { Block = true };
        var audioFactories = 0;
        using var playback = new AlertPlaybackController(() => { audioFactories++; return new Sound(); }, () => speech);
        playback.Start(null, AlertMode.SpeechOnly, "Dentist", repeatUntilDismissed: true);
        await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        playback.Stop();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(speech.Cancelled);
        Assert.Equal(0, audioFactories);
        playback.Dispose();
        await playback.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(speech.Disposed);
    }

    private sealed class Sound : IAlertSound, IDisposable
    {
        private int _plays;
        private int _active;
        private int _overlapped;
        public bool Overlapped => Volatile.Read(ref _overlapped) != 0;
        public int Plays => Volatile.Read(ref _plays);
        public bool Block { get; init; }
        public ManualResetEventSlim? CancellationGate { get; init; }
        public bool Cancelled { get; private set; }
        public bool Disposed { get; private set; }
        public float Volume { get; private set; }
        public SynchronizationContext? Context { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondPlayback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThirdPlayback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PlayOnceAsync(string file, float volume, CancellationToken token) => Play(volume, token);
        public Task PlayOnceAsync(WaveStream stream, float volume, CancellationToken token) => Play(volume, token);
        private async Task Play(float volume, CancellationToken token)
        {
            if (Interlocked.Increment(ref _active) > 1) Interlocked.Exchange(ref _overlapped, 1);
            try
            {
                Context = SynchronizationContext.Current;
                Volume = volume;
                var count = Interlocked.Increment(ref _plays);
                if (count >= 3) ThirdPlayback.TrySetResult();
                if (count >= 2) SecondPlayback.TrySetResult();
                using var registration = token.Register(() =>
                {
                    if (CancellationGate is not { } gate) return;
                    CancellationStarted.TrySetResult();
                    Assert.True(gate.Wait(TimeSpan.FromSeconds(30)), "Cancellation callback was not released");
                });
                Started.TrySetResult();
                if (Block)
                {
                    try { await Task.Delay(Timeout.Infinite, token); }
                    finally { Cancelled = token.IsCancellationRequested; }
                }
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Stop() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class Speech : ITextToSpeechEngine
    {
        public int Preparations { get; private set; }
        public bool Block { get; init; }
        public bool Cancelled { get; private set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SpeechAudio> SynthesizeAsync(string phrase, CancellationToken token)
        {
            Preparations++;
            Started.TrySetResult();
            if (Block)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Cancelled = token.IsCancellationRequested; }
            }
            return new SpeechAudio(WavWriter.ToBytes([0, 0], 22050), 0.4f);
        }
        public Task SpeakAndWaitAsync(string phrase, CancellationToken token) => throw new InvalidOperationException("Playback must use prepared audio");
        public void Stop() { }
        public void Dispose() => Disposed = true;
    }
}
