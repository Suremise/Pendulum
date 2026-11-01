using System.Text;

namespace Pendulum.Core.Audio;

/// Wraps raw PCM audio in a WAV file's header: what Piper's output needs to be played, and what
/// Whisper reads a recording from.
public static class WavWriter
{
    /// The whole WAV file, header and audio, as a stream positioned at its start.
    public static MemoryStream ToStream(byte[] pcmData, int sampleRate, int bitsPerSample = 16, int channels = 1)
    {
        var stream = new MemoryStream(44 + pcmData.Length);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + pcmData.Length);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write("data"u8);
            writer.Write(pcmData.Length);
            writer.Write(pcmData);
        }

        stream.Position = 0;
        return stream;
    }

    /// The whole WAV file, header and audio, as bytes.
    public static byte[] ToBytes(byte[] pcmData, int sampleRate, int bitsPerSample = 16, int channels = 1)
    {
        using var stream = ToStream(pcmData, sampleRate, bitsPerSample, channels);
        return stream.ToArray();
    }
}
