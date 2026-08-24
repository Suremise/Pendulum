using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pendulum.Core.Audio;

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

        using var process = new Process { StartInfo = psi };
        process.Start();

        await process.StandardInput.WriteLineAsync(phrase);
        process.StandardInput.Close();

        using var pcm = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(pcm, token);
        await process.WaitForExitAsync(token);

        if (pcm.Length == 0)
            return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"pendulum-piper-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(tempPath, BuildWavBytes(pcm.ToArray(), sampleRate), token);
        try
        {
            await _audio.PlayOnceAsync(tempPath, _volume, token);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
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
