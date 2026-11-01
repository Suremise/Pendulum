using System.Text;
using NAudio.Wave;
using Pendulum.Core.Audio;
using Whisper.net;

namespace Pendulum.Core.Speech;

/// One-shot dictation via a locally downloaded Whisper ggml model: records raw PCM from the
/// default microphone until the speaker goes quiet (or a max duration is hit), then runs it
/// through Whisper.net for transcription. Fully offline — no data leaves the machine.
///
/// The model is loaded and the audio transcribed on a background thread, so the window that
/// asked stays responsive (a model is 75 MB to 1.5 GB). Failures come as DictationException.
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
        // Before listening, not after the user has spoken.
        if (!File.Exists(modelPath))
            throw new DictationException(DictationProblem.WhisperModelMissing);

        var pcm = await RecordUntilSilenceAsync(token);
        onRecordingStopped?.Invoke();

        if (pcm is null || pcm.Length == 0)
            return null;

        return await Task.Run(() => TranscribeAsync(modelPath, pcm, token), token);
    }

    /// Loads the model and transcribes the audio. Runs off the caller's thread.
    internal static async Task<string?> TranscribeAsync(string modelPath, byte[] pcm, CancellationToken token)
    {
        var isEnglishOnly = Path.GetFileNameWithoutExtension(modelPath)
            .EndsWith(".en", StringComparison.OrdinalIgnoreCase);

        using var wavStream = WavWriter.ToStream(pcm, SampleRate);

        WhisperFactory factory;
        try
        {
            factory = WhisperFactory.FromPath(modelPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DictationException(DictationProblem.WhisperModelMissing, e);
        }
        catch (Exception e)
        {
            throw new DictationException(DictationProblem.WhisperModelUnreadable, e);
        }

        using (factory)
        {
            WhisperProcessor processor;
            try
            {
                processor = factory.CreateBuilder()
                    .WithLanguage(isEnglishOnly ? "en" : "auto")
                    .Build();
            }
            catch (Exception e)
            {
                throw new DictationException(DictationProblem.WhisperModelUnreadable, e);
            }

            await using (processor.ConfigureAwait(false))
            {
                var text = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(wavStream, token).ConfigureAwait(false))
                    text.Append(segment.Text);

                var result = text.ToString().Trim();
                return result.Length > 0 ? result : null;
            }
        }
    }

    private Task<byte[]?> RecordUntilSilenceAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new MemoryStream();
        var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 100 };

        var hasSpoken = false;
        var lastLoudAt = DateTime.UtcNow;
        var startedAt = DateTime.UtcNow;
        var stopped = 0;
        var registration = default(CancellationTokenRegistration);

        void Finish(Action complete)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
                return;

            _cancelRecording = null;
            registration.Dispose();
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            try
            {
                waveIn.StopRecording();
            }
            catch (Exception)
            {
                // The device may already be gone: there's nothing left to stop.
            }

            waveIn.Dispose();
            complete();
        }

        void Stop(bool keepAudio) =>
            Finish(() => tcs.TrySetResult(keepAudio && hasSpoken ? buffer.ToArray() : null));

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

        // Recording ended by itself: the microphone was unplugged or failed (NAudio hands over
        // the error), or capture just stopped. Either way, the listen ends instead of waiting.
        void OnRecordingStopped(object? s, StoppedEventArgs e)
        {
            if (e.Exception is { } error)
                Finish(() => tcs.TrySetException(new DictationException(DictationProblem.Microphone, error)));
            else
                Stop(keepAudio: true);
        }

        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;
        _cancelRecording = Stop;
        registration = token.Register(() => Stop(keepAudio: false));

        try
        {
            waveIn.StartRecording();
        }
        catch (Exception e)
        {
            // No microphone, or Windows won't open it.
            Finish(() => tcs.TrySetException(new DictationException(DictationProblem.Microphone, e)));
        }

        return tcs.Task;
    }

    /// Stops an in-progress recording without keeping the audio captured so far.
    public void Stop() => _cancelRecording?.Invoke(false);

    public void Dispose() => Stop();
}
