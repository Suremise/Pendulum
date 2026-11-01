using NAudio.Wave;

namespace Pendulum.Core.Audio;

/// What an alert plays its sound through: AudioService, or a fake in tests.
public interface IAlertSound
{
    Task PlayOnceAsync(string filePath, float volume, CancellationToken token);
    void Stop();
}

public class AudioService : IAlertSound, IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    /// Plays a file once. Throws when it can't be read or there's no output device.
    public async Task PlayOnceAsync(string filePath, float volume, CancellationToken token)
    {
        Stop();

        if (!File.Exists(filePath))
            return;

        var tcs = new TaskCompletionSource();
        // Locals, not the fields: Stop() (a dismiss, from another call) sets the fields to null
        // while this playback is still being awaited.
        var reader = new AudioFileReader(filePath) { Volume = Math.Clamp(volume, 0f, 1f) };
        var output = new WaveOutEvent();
        _reader = reader;
        _output = output;
        try
        {
            output.Init(reader);
        }
        catch
        {
            Stop();
            throw;
        }

        void OnStopped(object? s, StoppedEventArgs e) => Finish(tcs, e);
        output.PlaybackStopped += OnStopped;

        using var registration = token.Register(() =>
        {
            tcs.TrySetResult();
        });

        try
        {
            output.Play();
            await tcs.Task;
        }
        finally
        {
            output.PlaybackStopped -= OnStopped;
        }
    }

    /// Ends a playback's wait when NAudio says it stopped: normally (the end, or a stop), or
    /// with the error NAudio hands over when the device fails mid-play, which then reaches the
    /// alert as a sound problem.
    internal static void Finish(TaskCompletionSource playback, StoppedEventArgs stopped)
    {
        if (stopped.Exception is { } error)
            playback.TrySetException(error);
        else
            playback.TrySetResult();
    }

    public void Stop()
    {
        if (_output is not null)
        {
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => Stop();
}
