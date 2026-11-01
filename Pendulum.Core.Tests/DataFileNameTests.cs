using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// Sound, voice and model names from imported reminders and restored backups: only a plain file
/// name inside its own folder is ever used. Temp folders only; nothing is read over a network.
public sealed class DataFileNameTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> NamesOutside => new()
    {
        @"C:\Windows\Media\chimes.wav",
        @"\\host\share\a.mp3",
        @"\\?\C:\a.wav",
        "//host/share/a.mp3",
        @"..\Data\settings.json",
        "../a.wav",
        @"Sub\a.wav",
        "C:a.wav",
        "a.wav:stream",
        "..",
        ".",
        " ",
        "",
    };

    [Theory]
    [MemberData(nameof(NamesOutside))]
    public void A_path_a_drive_a_share_or_dot_dot_is_not_a_plain_name(string name)
    {
        Assert.False(DataFileName.IsPlain(name));
        Assert.Null(DataFileName.PathInside(_temp.Path, name));
    }

    [Theory]
    [InlineData("chime.wav")]
    [InlineData("en_GB-alba-medium.onnx")]
    [InlineData("ggml-base.en.bin")]
    [InlineData("my sound (2).mp3")]
    public void A_plain_name_resolves_inside_its_folder(string name)
    {
        Assert.True(DataFileName.IsPlain(name));
        Assert.Equal(Path.Combine(_temp.Path, name), DataFileName.PathInside(_temp.Path, name));
    }

    [Fact]
    public void Null_is_no_file()
    {
        Assert.False(DataFileName.IsPlain(null));
        Assert.Null(DataFileName.PathInside(_temp.Path, null));
    }

    [Fact]
    public void An_imported_reminder_with_an_outside_sound_plays_none_and_the_rest_are_kept()
    {
        var path = _temp.File("import.json");
        File.WriteAllText(path, """
            [
              { "Name": "Share", "TriggerAt": "2026-11-02T09:00:00", "SoundFileName": "\\\\host\\s\\a.mp3" },
              { "Name": "Climb", "TriggerAt": "2026-11-02T09:00:00", "SoundFileName": "..\\..\\Windows\\a.wav" },
              { "Name": "Rooted", "TriggerAt": "2026-11-02T09:00:00", "SoundFileName": "C:\\a.wav" },
              { "Name": "Plain", "TriggerAt": "2026-11-02T09:00:00", "SoundFileName": "bell.wav" },
              { "Name": "Silent", "TriggerAt": "2026-11-02T09:00:00" }
            ]
            """);

        var imported = new TriggerRepository().LoadFrom(path);

        Assert.Equal(new[] { "Share", "Climb", "Rooted", "Plain", "Silent" }, imported.Select(t => t.Name));
        Assert.Equal(new string?[] { null, null, null, "bell.wav", null }, imported.Select(t => t.SoundFileName));
    }

    [Fact]
    public void A_restored_backup_keeps_only_plain_names_for_sounds_voices_and_models()
    {
        var bad = _temp.File("bad-backup.json");
        BackupRepository.Save(bad, new AppBackup
        {
            Settings = new AppSettings
            {
                DefaultSoundFileName = @"\\host\s\a.mp3",
                WhisperModelFileName = @"..\..\evil.bin",
                PiperVoiceModelFileName = @"C:\elsewhere\voice.onnx",
            },
            Triggers = [new TriggerTimer { Name = "Dentist", SoundFileName = @"\\host\s\b.wav" }],
        });
        var good = _temp.File("good-backup.json");
        BackupRepository.Save(good, new AppBackup
        {
            Settings = new AppSettings
            {
                DefaultSoundFileName = "bell.wav",
                WhisperModelFileName = "ggml-base.en.bin",
                PiperVoiceModelFileName = "en_GB-alba-medium.onnx",
            },
            Triggers = [new TriggerTimer { Name = "Dentist", SoundFileName = "chime.wav" }],
        });

        var cleaned = BackupRepository.Load(bad)!;
        Assert.Equal(string.Empty, cleaned.Settings!.DefaultSoundFileName);
        Assert.Null(cleaned.Settings.WhisperModelFileName);
        Assert.Null(cleaned.Settings.PiperVoiceModelFileName);
        Assert.Null(Assert.Single(cleaned.Triggers).SoundFileName);

        var kept = BackupRepository.Load(good)!;
        Assert.Equal("bell.wav", kept.Settings!.DefaultSoundFileName);
        Assert.Equal("ggml-base.en.bin", kept.Settings.WhisperModelFileName);
        Assert.Equal("en_GB-alba-medium.onnx", kept.Settings.PiperVoiceModelFileName);
        Assert.Equal("chime.wav", Assert.Single(kept.Triggers).SoundFileName);
    }
}
