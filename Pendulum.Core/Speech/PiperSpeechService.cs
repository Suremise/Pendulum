using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Pendulum.Core.Audio;
using Pendulum.Core.Resources;

namespace Pendulum.Core.Speech;

/// Text-to-speech via a locally installed Piper engine (piper.exe, downloaded separately by the
/// user — see Settings) and a chosen ggml/onnx voice model. Runs piper.exe as a subprocess per
/// phrase (piper has no persistent "server" mode in its portable build), feeding text over stdin
/// and reading raw 16-bit PCM back over stdout, then plays it through the same AudioService the
/// rest of the app uses for alert sounds.
public sealed class PiperSpeechService : ITextToSpeechEngine, IDisposable
{
    private readonly string _executablePath;
    private readonly string _modelPath;
    private readonly float _speakingRate;
    private readonly float _volume;
    private readonly AudioService _audio = new();

    /// How long piper may take to turn one phrase into audio before it's stopped. Tests set less.
    internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public PiperSpeechService(string executablePath, string modelPath, float speakingRate, float volume)
    {
        _executablePath = executablePath;
        _modelPath = modelPath;
        _speakingRate = speakingRate;
        _volume = volume;
    }

    public async Task SpeakAndWaitAsync(string phrase, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return;

        var sampleRate = ReadSampleRate(_modelPath);

        var args = new StringBuilder()
            .Append("--quiet --output-raw --model ").Append(Quote(_modelPath));
        if (Math.Abs(_speakingRate - 1f) > 0.01f)
            args.Append(" --length_scale ").Append(_speakingRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = args.ToString(),
            WorkingDirectory = Path.GetDirectoryName(_executablePath),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
        };

        var pcm = await RunPiperAsync(psi, phrase, token);
        if (pcm.Length == 0)
            return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"pendulum-piper-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(tempPath, BuildWavBytes(pcm, sampleRate), token);
        try
        {
            await _audio.PlayOnceAsync(tempPath, _volume, token);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
        }
    }

    /// Runs piper for one phrase and returns its audio (empty for none). Piper runs in a job of
    /// its own: a dismiss or the time limit ends it and everything it started, and so does piper
    /// finishing, so nothing it left running (a child holding its pipes) can keep the alert
    /// waiting. stderr is never waited for.
    private async Task<byte[]> RunPiperAsync(ProcessStartInfo psi, string phrase, CancellationToken token)
    {
        using var process = new Process { StartInfo = psi };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? exitWait = null;
        ManualResetEvent? exitHandle = null;

        // stderr is read as it comes (left unread, a chatty piper fills the pipe and waits for
        // ever); only its last line is kept, to say why piper failed.
        var lastError = string.Empty;
        var errorLock = new object();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                lock (errorLock) lastError = e.Data.Trim();
        };

        ProcessJob? job = null;
        try
        {
            try
            {
                job = new ProcessJob();
            }
            catch (Win32Exception)
            {
                // No job (Windows refused one): a dismiss or timeout still ends piper's tree.
            }

            try
            {
                process.Start();
            }
            catch (Win32Exception e)
            {
                // piper.exe removed, or blocked by antivirus or policy.
                throw new InvalidOperationException(string.Format(CoreStrings.Piper_CouldNotStart, _executablePath, e.Message), e);
            }

            job?.TryAdd(process);
            process.BeginErrorReadLine();

            // piper's own exit, from its process handle: Process.Exited and WaitForExitAsync also
            // wait for its pipes to close, which a child it left running can hold open.
            exitHandle = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process.Handle, ownsHandle: false) };
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitHandle, (_, _) => exited.TrySetResult(), null, System.Threading.Timeout.Infinite, executeOnlyOnce: true);

            using var pcm = new MemoryStream();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Timeout);
            try
            {
                try
                {
                    await process.StandardInput.WriteLineAsync(phrase.AsMemory(), timeout.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // piper has already exited: its exit code, below, says why.
                }

                var audio = process.StandardOutput.BaseStream.CopyToAsync(pcm, timeout.Token);
                await exited.Task.WaitAsync(timeout.Token);

                // piper has finished, so its audio is all in the pipe. Anything it left running
                // (holding its pipes open) is ended now, which lets the pipe close after the
                // audio rather than keep the alert waiting.
                job?.Terminate();
                await audio.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                StopAll(process, job);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException(string.Format(CoreStrings.Piper_TimedOut, (int)Timeout.TotalSeconds));
            }

            if (pcm.Length == 0 && process.ExitCode != 0)
            {
                string reason;
                lock (errorLock) reason = lastError.Length > 300 ? lastError[..300] : lastError;
                throw new InvalidOperationException(string.Format(CoreStrings.Piper_Failed, process.ExitCode, reason));
            }

            return pcm.ToArray();
        }
        finally
        {
            exitWait?.Unregister(null);
            exitHandle?.Dispose();

            // Ends anything piper left running.
            if (job is not null)
                job.Dispose();
            else
                StopAll(process, null);
        }
    }

    /// Ends piper and everything it started, whether or not piper itself is still running.
    private static void StopAll(Process process, ProcessJob? job)
    {
        job?.Terminate();
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    // Piper's voice config (<model>.onnx.json) carries the sample rate its ONNX model was
    // trained/exported at — almost always 22050Hz, but reading it avoids hardcoding a value
    // that would play back at the wrong pitch/speed for a voice that differs.
    private static int ReadSampleRate(string modelPath)
    {
        const int defaultSampleRate = 22050;
        var configPath = modelPath + ".json";
        if (!File.Exists(configPath))
            return defaultSampleRate;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var rate))
                return rate.GetInt32();
        }
        catch (JsonException)
        {
            // malformed config — fall back to the standard rate rather than failing to speak.
        }

        return defaultSampleRate;
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    private static byte[] BuildWavBytes(byte[] pcmData, int sampleRate)
    {
        const int bitsPerSample = 16;
        const int channels = 1;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using var stream = new MemoryStream();
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

        return stream.ToArray();
    }

    public void Stop() => _audio.Stop();

    public void Dispose() => _audio.Dispose();
}
