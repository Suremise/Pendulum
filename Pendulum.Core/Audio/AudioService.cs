using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pendulum.Core.Audio;

/// What an alert plays its sound and prepared speech through; tests use no real device.
public interface IAlertSound
{
    Task PlayOnceAsync(string filePath, float volume, CancellationToken token);
    Task PlayOnceAsync(WaveStream stream, float volume, CancellationToken token);
    void Stop();
}

public class AudioService : IAlertSound, IDisposable
{
    private readonly Func<IWavePlayer> _createOutput;
    private readonly object _gate = new();
    private IWavePlayer? _output;
    private TaskCompletionSource? _completion;

    public AudioService() : this(() => new WaveOutEvent()) { }

    internal AudioService(Func<IWavePlayer> createOutput) => _createOutput = createOutput;

    /// Plays a file once. Throws when it can't be read or there's no output device.
    public async Task PlayOnceAsync(string filePath, float volume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
            return;
        using var reader = new AudioFileReader(filePath);
        await PlayOnceAsync(reader, volume, token).ConfigureAwait(false);
    }

    /// Plays a stream from its current position, disposing it when playback finishes.
    /// Cancellation stops this output as well as its wait, without stopping a later playback.
    public async Task PlayOnceAsync(WaveStream stream, float volume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Stop();
        using var ownedStream = stream;
        var samples = new VolumeSampleProvider(stream.ToSampleProvider()) { Volume = Math.Clamp(volume, 0f, 1f) };
        var output = _createOutput();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StoppedEventArgs e) => Finish(completion, e);
        output.PlaybackStopped += OnStopped;
        CancellationTokenRegistration registration = default;
        try
        {
            lock (_gate)
            {
                _output = output;
                _completion = completion;
                output.Init(samples.ToWaveProvider());
            }
            registration = token.Register(() =>
            {
                lock (_gate)
                {
                    try { output.Stop(); }
                    catch (Exception e) { completion.TrySetException(e); }
                    completion.TrySetResult();
                }
            });
            lock (_gate)
            {
                if (!token.IsCancellationRequested && ReferenceEquals(_output, output))
                    output.Play();
                else
                    completion.TrySetResult();
            }
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Never wait for a cancellation callback while holding the gate it needs.
            registration.Dispose();
            lock (_gate)
            {
                if (ReferenceEquals(_output, output))
                {
                    _output = null;
                    _completion = null;
                }
                output.PlaybackStopped -= OnStopped;
                output.Dispose();
            }
        }
    }

    /// Propagates device errors delivered by NAudio after playback has started.
    internal static void Finish(TaskCompletionSource playback, StoppedEventArgs stopped)
    {
        if (stopped.Exception is { } error)
            playback.TrySetException(error);
        else
            playback.TrySetResult();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _output?.Stop();
            _completion?.TrySetResult();
            _output = null;
            _completion = null;
        }
    }

    public void Dispose() => Stop();
}
