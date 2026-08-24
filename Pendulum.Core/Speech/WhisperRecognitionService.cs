using System.Text;
using NAudio.Wave;
using Whisper.net;

namespace Pendulum.Core.Speech;

/// One-shot dictation via a locally downloaded Whisper ggml model: records raw PCM from the
/// default microphone until the speaker goes quiet (or a max duration is hit), then runs it
/// through Whisper.net for transcription. Fully offline — no data leaves the machine.
public sealed class WhisperRecognitionService : IDisposable
{
    private const int SampleRate = 16000;
    private const short SilenceThreshold = 700;
    private static readonly TimeSpan SilenceHangover = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromSeconds(20);

    private Action<bool>? _cancelRecording;

    /// Records one phrase and transcribes it using the ggml model at modelPath. Returns null if
    /// nothing was said (or nothing was understood). onRecordingStopped fires once capture ends
    /// and transcription begins, so callers can update a "Listening…" indicator to "Transcribing…".
    public async Task<string?> ListenOnceAsync(string modelPath, Action? onRecordingStopped, CancellationToken token)
    {
        var pcm = await RecordUntilSilenceAsync(token);
        onRecordingStopped?.Invoke();

        if (pcm is null || pcm.Length == 0)
            return null;

        var isEnglishOnly = Path.GetFileNameWithoutExtension(modelPath)
            .EndsWith(".en", StringComparison.OrdinalIgnoreCase);

        using var wavStream = BuildWavStream(pcm, SampleRate, bitsPerSample: 16, channels: 1);
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage(isEnglishOnly ? "en" : "auto")
            .Build();

        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(wavStream))
        {
            token.ThrowIfCancellationRequested();
            text.Append(segment.Text);
        }

        var result = text.ToString().Trim();
        return result.Length > 0 ? result : null;
    }

    private Task<byte[]?> RecordUntilSilenceAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource<byte[]?>();
        var buffer = new MemoryStream();
        var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 100 };

        var hasSpoken = false;
        var lastLoudAt = DateTime.UtcNow;
        var startedAt = DateTime.UtcNow;
        var stopped = 0;
        var registration = default(CancellationTokenRegistration);

        void Stop(bool keepAudio)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
                return;

            _cancelRecording = null;
            registration.Dispose();
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.StopRecording();
            waveIn.Dispose();

            tcs.TrySetResult(keepAudio && hasSpoken ? buffer.ToArray() : null);
        }

        void OnDataAvailable(object? s, WaveInEventArgs e)
        {
            buffer.Write(e.Buffer, 0, e.BytesRecorded);

            var loud = false;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                if (Math.Abs((int)sample) > SilenceThreshold)
                {
                    loud = true;
                    break;
                }
            }

            var now = DateTime.UtcNow;
            if (loud)
            {
                hasSpoken = true;
                lastLoudAt = now;
            }

            if ((hasSpoken && now - lastLoudAt > SilenceHangover) || now - startedAt > MaxRecordingDuration)
                Stop(keepAudio: true);
        }

        waveIn.DataAvailable += OnDataAvailable;
        _cancelRecording = Stop;
        registration = token.Register(() => Stop(keepAudio: false));

        waveIn.StartRecording();

        return tcs.Task;
    }

    private static MemoryStream BuildWavStream(byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
    {
        var stream = new MemoryStream();
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + pcmData.Length);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(pcmData.Length);
            writer.Write(pcmData);
        }

        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// Stops an in-progress recording without keeping the audio captured so far.
    public void Stop() => _cancelRecording?.Invoke(false);

    public void Dispose() => Stop();
}
