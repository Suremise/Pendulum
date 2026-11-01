using Pendulum.Core.Audio;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Tests;

/// The shared helpers: the WAV header, listing a folder's files, the crash log's size limit and
/// piper's arguments. Temp folders only.
public sealed class CleanupHelperTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_wav_is_a_canonical_16_bit_mono_header_then_the_audio()
    {
        var pcm = new byte[] { 1, 2, 3, 4, 5, 6 };

        var wav = WavWriter.ToBytes(pcm, 22050);

        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal("RIFF"u8.ToArray(), wav[0..4]);
        Assert.Equal(36 + pcm.Length, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt "u8.ToArray(), wav[8..16]);
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));      // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));      // mono
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(44100, BitConverter.ToInt32(wav, 28));  // bytes a second
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));      // bytes a sample
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data"u8.ToArray(), wav[36..40]);
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));
        Assert.Equal(pcm, wav[44..]);
    }

    [Fact]
    public void A_wav_stream_starts_at_its_beginning_and_NAudio_reads_it()
    {
        var pcm = new byte[3200];
        using var stream = WavWriter.ToStream(pcm, 16000);

        Assert.Equal(0, stream.Position);
        using var reader = new NAudio.Wave.WaveFileReader(stream);
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(pcm.Length, reader.Length);
    }

    [Fact]
    public void Listing_a_folder_gives_names_of_the_types_asked_in_name_order()
    {
        foreach (var name in new[] { "b.WAV", "a.mp3", "c.txt", "d.wav.bak" })
            File.WriteAllText(_temp.File(name), "");
        Directory.CreateDirectory(_temp.File("sub.wav"));

        Assert.Equal(["a.mp3", "b.WAV"], DataFileName.ListIn(_temp.Path, ".wav", ".mp3"));
        Assert.Empty(DataFileName.ListIn(_temp.File("missing"), ".wav"));
    }

    [Fact]
    public void A_log_grows_to_its_limit_then_rolls_over_keeping_the_one_before()
    {
        var path = _temp.File("crash.log");

        LogFile.Append(path, "one\n", limitBytes: 10);
        LogFile.Append(path, "two\n", limitBytes: 10);
        Assert.Equal("one\ntwo\n", File.ReadAllText(path));
        Assert.False(File.Exists(LogFile.OldPath(path)));

        LogFile.Append(path, "three\n", limitBytes: 10);
        Assert.Equal("three\n", File.ReadAllText(path));
        Assert.Equal("one\ntwo\n", File.ReadAllText(LogFile.OldPath(path)));

        LogFile.Append(path, "four\n", limitBytes: 10);
        Assert.Equal("four\n", File.ReadAllText(path));
        Assert.Equal("three\n", File.ReadAllText(LogFile.OldPath(path)));
    }

    [Fact]
    public void An_entry_bigger_than_the_limit_is_still_written()
    {
        var path = _temp.File("crash.log");

        LogFile.Append(path, "a long entry\n", limitBytes: 4);

        Assert.Equal("a long entry\n", File.ReadAllText(path));
        Assert.False(File.Exists(LogFile.OldPath(path)));
    }

    [Fact]
    public void Piper_gets_the_model_path_as_one_argument_and_the_rate_in_invariant_form()
    {
        var model = @"C:\Pendulum Data\my voice.onnx";

        Assert.Equal(["--quiet", "--output-raw", "--model", model], PiperSpeechService.Arguments(model, 1f));
        Assert.Equal(["--quiet", "--output-raw", "--model", model, "--length_scale", "0.75"], PiperSpeechService.Arguments(model, 0.75f));
    }
}
