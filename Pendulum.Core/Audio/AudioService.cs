using NAudio.Wave;

namespace Pendulum.Core.Audio;

public class AudioService : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public async Task PlayOnceAsync(string filePath, float volume, CancellationToken token)
    {
        Stop();

        if (!File.Exists(filePath))
            return;

        var tcs = new TaskCompletionSource();
        _reader = new AudioFileReader(filePath) { Volume = Math.Clamp(volume, 0f, 1f) };
        _output = new WaveOutEvent();
        _output.Init(_reader);

        void OnStopped(object? s, StoppedEventArgs e) => tcs.TrySetResult();
        _output.PlaybackStopped += OnStopped;

        using var registration = token.Register(() =>
        {
            tcs.TrySetResult();
        });

        _output.Play();

        try
        {
            await tcs.Task;
        }
        finally
        {
            _output.PlaybackStopped -= OnStopped;
        }
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
