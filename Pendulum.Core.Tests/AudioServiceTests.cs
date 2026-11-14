using NAudio.Wave;
using Pendulum.Core.Audio;

namespace Pendulum.Core.Tests;

public sealed class AudioServiceTests
{
    private static WaveStream Wave() => new WaveFileReader(WavWriter.ToStream([0, 64, 0, 64], 22050));

    [Fact]
    public async Task Cancelling_the_token_stops_the_local_device_without_an_explicit_Stop()
    {
        var output = new Output();
        using var audio = new AudioService(() => output);
        using var cancel = new CancellationTokenSource();
        var playing = audio.PlayOnceAsync(Wave(), 0.25f, cancel.Token);
        await output.Played.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cancel.Cancel();
        await playing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, output.Stops);
        Assert.True(output.Disposed);
        Assert.Equal(0.25f, output.Gain);
        Assert.False(output.Padded);
    }

    [Fact]
    public async Task Cancelling_an_old_playback_cannot_stop_its_replacement()
    {
        var first = new Output();
        var second = new Output();
        var outputs = new Queue<Output>([first, second]);
        using var audio = new AudioService(() => outputs.Dequeue());
        using var cancelFirst = new CancellationTokenSource();
        using var cancelSecond = new CancellationTokenSource();
        var oldPlayback = audio.PlayOnceAsync(Wave(), 1f, cancelFirst.Token);
        await first.Played.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var replacement = audio.PlayOnceAsync(Wave(), 1f, cancelSecond.Token);
        await second.Played.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await oldPlayback.WaitAsync(TimeSpan.FromSeconds(30));
        cancelFirst.Cancel();
        Assert.Equal(0, second.Stops);
        Assert.False(replacement.IsCompleted);
        cancelSecond.Cancel();
        await replacement.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, second.Stops);
    }

    [Fact]
    public async Task A_device_error_is_propagated_and_disposes_the_output()
    {
        var output = new Output();
        using var audio = new AudioService(() => output);
        var playing = audio.PlayOnceAsync(Wave(), 1f, CancellationToken.None);
        output.Fail();
        await Assert.ThrowsAsync<IOException>(() => playing);
        Assert.True(output.Disposed);
    }

    private sealed class Output : IWavePlayer
    {
        public TaskCompletionSource Played { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops { get; private set; }
        public bool Disposed { get; private set; }
        public float Gain { get; private set; }
        public bool Padded { get; private set; }
        public float Volume { get; set; } = 1f;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => new(22050, 16, 1);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider)
        {
            // The input samples are 0.5. Check actual gain and end-of-stream behavior rather
            // than the choice of NAudio wrapper; channel count must stay with the source.
            var buffer = new byte[64];
            var read = provider.Read(buffer, 0, buffer.Length);
            Gain = BitConverter.ToSingle(buffer, 0) * 2;
            Padded = read == buffer.Length;
            Assert.Equal(1, provider.WaveFormat.Channels);
        }
        public void Play() { PlaybackState = PlaybackState.Playing; Played.TrySetResult(); }
        public void Pause() => PlaybackState = PlaybackState.Paused;
        // Deliberately does not raise PlaybackStopped: cancellation must stop the device and
        // finish the wait even if a failing driver never delivers its stopped event.
        public void Stop() { Stops++; PlaybackState = PlaybackState.Stopped; }
        public void Fail() => PlaybackStopped?.Invoke(this, new StoppedEventArgs(new IOException("device removed")));
        public void Dispose() => Disposed = true;
    }
}
