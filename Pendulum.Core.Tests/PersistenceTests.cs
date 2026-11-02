using System.Text.Json;
using System.Text.Json.Nodes;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// Settings, reminders and backups on disk, in temp folders only.
public sealed class PersistenceTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // ---- Settings ----

    [Fact]
    public void New_settings_have_the_documented_defaults()
    {
        var s = new AppSettings();

        Assert.Equal("chime.wav", s.DefaultSoundFileName);
        Assert.Equal(100, s.TtsVolume);
        Assert.True(s.LaunchOnWindowsStartup);
        Assert.True(s.StartMinimized);
        Assert.Equal(5, s.SnoozeMinutes);
        Assert.True(s.RepeatAlertUntilDismissed);
        Assert.Equal(30, s.AutoResolveMinutes);
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.False(s.CheckForUpdatesOnStartup);
        Assert.True(s.QuickAddHotkeyEnabled);
        Assert.Equal("Ctrl+Shift+R", s.QuickAddHotkeyGesture);
        Assert.Equal(SpeechToTextEngine.WindowsSpeechRecognition, s.SpeechToTextEngine);
        Assert.Equal(TextToSpeechEngine.WindowsSpeechSynthesis, s.TextToSpeechEngine);
        Assert.Equal(0, s.AutoDeleteSpentAfterDays);
    }

    [Fact]
    public void Without_a_settings_file_the_defaults_load_and_nothing_is_written()
    {
        var path = _temp.File("settings.json");

        var s = new SettingsRepository(path).Load();

        Assert.Equal(Json(new AppSettings()), Json(s));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Settings_saved_load_back_the_same()
    {
        var path = _temp.File("settings.json");
        var saved = new AppSettings
        {
            SnoozeMinutes = 12,
            Theme = ThemeMode.Dark,
            CheckForUpdatesOnStartup = true,
            QuickAddHotkeyGesture = "Ctrl+Alt+P",
            TextToSpeechEngine = TextToSpeechEngine.Piper,
            PiperVoiceModelFileName = "voice.onnx",
            ReminderFilterFrom = new DateTime(2026, 1, 2),
        };

        new SettingsRepository(path).Save(saved);
        var loaded = new SettingsRepository(path).Load();

        Assert.Equal(Json(saved), Json(loaded));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Saving_settings_twice_replaces_the_file()
    {
        var path = _temp.File("settings.json");
        var repo = new SettingsRepository(path);

        repo.Save(new AppSettings { SnoozeMinutes = 7 });
        repo.Save(new AppSettings { SnoozeMinutes = 9 });

        Assert.Equal(9, repo.Load().SnoozeMinutes);
        // The save before is kept as the .bak; no temp file is left.
        Assert.Equal(new[] { path, path + ".bak" }, Directory.GetFiles(_temp.Path).Order());
        Assert.Equal(7, JsonStore.Load<AppSettings>(path + ".bak")!.SnoozeMinutes);
    }

    [Fact]
    public void A_corrupt_settings_file_is_kept_aside_and_the_defaults_load()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, "{ \"SnoozeMinutes\": 12, oops");

        var s = new SettingsRepository(path).Load();

        Assert.Equal(Json(new AppSettings()), Json(s));
        Assert.False(File.Exists(path));
        var quarantined = Assert.Single(Directory.GetFiles(_temp.Path, "settings.json.corrupt-*"));
        Assert.Equal("{ \"SnoozeMinutes\": 12, oops", File.ReadAllText(quarantined));
    }

    [Fact]
    public void An_empty_settings_file_is_kept_aside_too()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, "");

        new SettingsRepository(path).Load();

        Assert.Single(Directory.GetFiles(_temp.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Settings_keys_the_app_no_longer_knows_are_ignored()
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, """{ "SnoozeMinutes": 8, "SomethingFromTheFuture": true }""");

        Assert.Equal(8, new SettingsRepository(path).Load().SnoozeMinutes);
    }

    // ---- The update check's default, old files and new (on by default up to 1.1.0, off since) ----

    [Fact]
    public void Update_check_is_off_for_new_settings()
    {
        Assert.False(new AppSettings().CheckForUpdatesOnStartup);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "SnoozeMinutes": 8, "Theme": 2 }""")]
    public void A_file_without_the_update_check_key_keeps_it_on_and_keeps_its_other_values(string json)
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, json);

        var s = JsonStore.Load<AppSettings>(path)!;

        Assert.True(s.CheckForUpdatesOnStartup);
        if (json.Contains("SnoozeMinutes"))
        {
            Assert.Equal(8, s.SnoozeMinutes);
            Assert.Equal(ThemeMode.Dark, s.Theme);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_file_with_the_update_check_key_keeps_its_value(bool value)
    {
        var path = _temp.File("settings.json");
        File.WriteAllText(path, $$"""{ "CheckForUpdatesOnStartup": {{(value ? "true" : "false")}} }""");

        Assert.Equal(value, JsonStore.Load<AppSettings>(path)!.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void A_backup_whose_settings_lack_the_update_check_key_keeps_it_on()
    {
        var path = _temp.File("backup.json");
        File.WriteAllText(path, """{ "Settings": { "SnoozeMinutes": 8 }, "Triggers": [] }""");

        Assert.True(BackupRepository.Load(path)!.Settings!.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void New_settings_saved_and_reloaded_keep_the_update_check_off_and_name_the_key()
    {
        var path = _temp.File("settings.json");

        JsonStore.Save(path, new AppSettings());

        Assert.Contains("\"CheckForUpdatesOnStartup\": false", File.ReadAllText(path));
        Assert.False(JsonStore.Load<AppSettings>(path)!.CheckForUpdatesOnStartup);
    }

    // ---- Reminders ----

    [Fact]
    public void Reminders_saved_load_back_the_same()
    {
        var path = _temp.File("triggers.json");
        var reminders = SampleReminders();

        new TriggerRepository().SaveTo(path, reminders);
        var loaded = new TriggerRepository().LoadFrom(path);

        Assert.Equal(Json(reminders), Json(loaded));
    }

    [Fact]
    public void Reminders_dont_save_the_list_selection()
    {
        var path = _temp.File("triggers.json");
        var t = new TriggerTimer { Name = "x", IsSelectedForBulk = true };

        new TriggerRepository().SaveTo(path, new[] { t });

        Assert.DoesNotContain("IsSelectedForBulk", File.ReadAllText(path));
        Assert.False(new TriggerRepository().LoadFrom(path).Single().IsSelectedForBulk);
    }

    [Fact]
    public void The_apps_own_corrupt_reminders_file_is_kept_aside_not_overwritten()
    {
        // triggers.json in Data\ goes through JsonStore.Load, which sets a bad file aside.
        var path = _temp.File("triggers.json");
        File.WriteAllText(path, "[ { \"Name\": \"half a remind");

        Assert.Null(JsonStore.Load<List<TriggerTimer>>(path));
        var quarantined = Assert.Single(Directory.GetFiles(_temp.Path, "triggers.json.corrupt-*"));
        Assert.Equal("[ { \"Name\": \"half a remind", File.ReadAllText(quarantined));
    }

    // ---- Surviving a damaged save ----

    [Fact]
    public void A_damaged_file_loads_the_save_before_it_and_is_kept_aside()
    {
        var path = _temp.File("triggers.json");
        JsonStore.Save(path, new List<TriggerTimer> { new() { Name = "Dentist" } });
        JsonStore.Save(path, new List<TriggerTimer> { new() { Name = "Dentist" }, new() { Name = "Gym" } });
        File.WriteAllText(path, "");  // what a power cut mid-save can leave

        var load = JsonStore.LoadWithOutcome<List<TriggerTimer>>(path);

        Assert.Equal(StoreOutcome.RecoveredFromBackup, load.Outcome);
        Assert.Equal(new[] { "Dentist" }, load.Value!.Select(t => t.Name));
        Assert.NotNull(load.SetAsidePath);
        Assert.Equal("", File.ReadAllText(load.SetAsidePath!));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_damaged_file_with_no_earlier_copy_is_reported_and_kept()
    {
        var path = _temp.File("triggers.json");
        File.WriteAllText(path, "[ { \"Name\": \"half a remind");

        var load = JsonStore.LoadWithOutcome<List<TriggerTimer>>(path);

        Assert.Equal(StoreOutcome.SetAside, load.Outcome);
        Assert.Null(load.Value);
        Assert.Equal("[ { \"Name\": \"half a remind", File.ReadAllText(load.SetAsidePath!));
    }

    [Fact]
    public void A_file_another_program_holds_is_never_set_aside_or_overwritten()
    {
        var path = _temp.File("triggers.json");
        JsonStore.Save(path, new List<TriggerTimer> { new() { Name = "Dentist" } });
        var before = File.ReadAllText(path);
        var delays = JsonStore.RetryDelays;
        JsonStore.RetryDelays = [];
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var e = Assert.Throws<StoreUnreadableException>(() => JsonStore.LoadWithOutcome<List<TriggerTimer>>(path));
                Assert.Equal(path, e.Path);
                Assert.Contains(path, e.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            JsonStore.RetryDelays = delays;
        }

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_temp.Path, "triggers.json.corrupt-*"));
    }

    [Fact]
    public void A_missing_file_whose_earlier_copy_is_held_stops_and_keeps_it()
    {
        var path = _temp.File("triggers.json");
        JsonStore.Save(path + ".bak", new List<TriggerTimer> { new() { Name = "Dentist" } }, keepBackup: false);
        var before = File.ReadAllText(path + ".bak");

        AssertUnreadableWhileHeld(path + ".bak", path);

        Assert.Equal(before, File.ReadAllText(path + ".bak"));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.corrupt-*"));
    }

    [Fact]
    public void A_damaged_file_whose_earlier_copy_is_held_stops_with_both_in_place()
    {
        var path = _temp.File("triggers.json");
        JsonStore.Save(path + ".bak", new List<TriggerTimer> { new() { Name = "Dentist" } }, keepBackup: false);
        File.WriteAllText(path, "");
        var before = File.ReadAllText(path + ".bak");

        AssertUnreadableWhileHeld(path + ".bak", path);

        Assert.True(File.Exists(path));
        Assert.Equal(before, File.ReadAllText(path + ".bak"));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.corrupt-*"));
    }

    [Fact]
    public void A_missing_file_with_a_damaged_earlier_copy_is_reported_and_kept()
    {
        var path = _temp.File("triggers.json");
        File.WriteAllText(path + ".bak", "[ { half");

        var load = JsonStore.LoadWithOutcome<List<TriggerTimer>>(path);

        Assert.Equal(StoreOutcome.SetAside, load.Outcome);
        Assert.Null(load.Value);
        Assert.Equal("[ { half", File.ReadAllText(load.SetAsidePath!));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void When_the_file_and_its_earlier_copy_are_both_damaged_both_are_kept()
    {
        var path = _temp.File("triggers.json");
        File.WriteAllText(path, "{ main");
        File.WriteAllText(path + ".bak", "{ bak");

        var load = JsonStore.LoadWithOutcome<List<TriggerTimer>>(path);

        Assert.Equal(StoreOutcome.SetAside, load.Outcome);
        Assert.Equal("{ main", File.ReadAllText(load.SetAsidePath!));
        var kept = Directory.GetFiles(_temp.Path, "triggers.json.bak.corrupt-*");
        Assert.Equal("{ bak", File.ReadAllText(Assert.Single(kept)));
    }

    /// Loading path while held is open with no sharing: StoreUnreadableException naming it.
    private static void AssertUnreadableWhileHeld(string held, string path)
    {
        var delays = JsonStore.RetryDelays;
        JsonStore.RetryDelays = [];
        try
        {
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var e = Assert.Throws<StoreUnreadableException>(() => JsonStore.LoadWithOutcome<List<TriggerTimer>>(path));
                Assert.Equal(held, e.Path);
            }
        }
        finally
        {
            JsonStore.RetryDelays = delays;
        }
    }

    [Fact]
    public void A_missing_file_with_a_good_earlier_copy_uses_it()
    {
        // A save stopped between keeping the .bak and putting the new file in place.
        var path = _temp.File("settings.json");
        JsonStore.Save(path + ".bak", new AppSettings { SnoozeMinutes = 11 }, keepBackup: false);

        var load = JsonStore.LoadWithOutcome<AppSettings>(path);

        Assert.Equal(StoreOutcome.RecoveredFromBackup, load.Outcome);
        Assert.Equal(11, load.Value!.SnoozeMinutes);
        Assert.Null(load.SetAsidePath);
    }

    [Fact]
    public void No_file_at_all_is_a_first_run()
    {
        var load = JsonStore.LoadWithOutcome<AppSettings>(_temp.File("settings.json"));
        Assert.Equal(StoreOutcome.Missing, load.Outcome);
        Assert.Null(load.Value);
    }

    [Fact]
    public void Temp_files_a_cut_off_save_left_are_cleared_and_never_read()
    {
        var path = _temp.File("settings.json");
        JsonStore.Save(path, new AppSettings { SnoozeMinutes = 7 });
        File.WriteAllText(path + ".0123456789abcdef0123456789abcdef.tmp", "{ half");

        Assert.Equal(7, JsonStore.Load<AppSettings>(path)!.SnoozeMinutes);
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }

    [Fact]
    public void Exports_and_backups_the_user_saves_get_no_bak_beside_them()
    {
        var export = _temp.File("my-reminders.json");
        new TriggerRepository().SaveTo(export, [new TriggerTimer { Name = "One" }]);
        new TriggerRepository().SaveTo(export, [new TriggerTimer { Name = "Two" }]);
        var backup = _temp.File("my-backup.json");
        BackupRepository.Save(backup, new AppBackup());
        BackupRepository.Save(backup, new AppBackup());

        Assert.Equal(new[] { backup, export }, Directory.GetFiles(_temp.Path).Order());
    }

    // ---- Files the user picks: to import or restore. Never moved or changed, whatever they hold ----

    public static TheoryData<string, string> NotPendulumFiles => new()
    {
        { "notes.json", "{ \"title\": \"shopping\", \"items\": [\"milk\"] }" },
        { "half.json", "[ { \"Name\": \"half a remind" },
        { "empty.json", "" },
        { "words.json", "not json at all" },
        { "null.json", "null" },
        { "numbers.json", "[1, 2, 3]" },
        { "empty-object.json", "{}" },
    };

    [Theory]
    [MemberData(nameof(NotPendulumFiles))]
    public void Importing_a_file_that_isnt_reminders_says_so_and_leaves_it_where_it_was(string name, string content)
    {
        var path = _temp.File(name);
        File.WriteAllText(path, content);
        var written = File.GetLastWriteTimeUtc(path);

        Assert.Throws<InvalidDataException>(() => new TriggerRepository().LoadFrom(path));

        AssertUntouched(path, content, written);
    }

    [Theory]
    [MemberData(nameof(NotPendulumFiles))]
    public void Restoring_a_file_that_isnt_a_backup_finds_no_backup_and_leaves_it_where_it_was(string name, string content)
    {
        var path = _temp.File(name);
        File.WriteAllText(path, content);
        var written = File.GetLastWriteTimeUtc(path);

        Assert.Null(BackupRepository.Load(path));

        AssertUntouched(path, content, written);
    }

    [Fact]
    public void Importing_a_backup_says_it_isnt_reminders_and_leaves_it_where_it_was()
    {
        var path = _temp.File("backup.json");
        BackupRepository.Save(path, new AppBackup { Settings = new AppSettings(), Triggers = SampleReminders() });
        var content = File.ReadAllText(path);

        Assert.Throws<InvalidDataException>(() => new TriggerRepository().LoadFrom(path));

        AssertUntouched(path, content, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Importing_a_file_thats_gone_says_so()
    {
        Assert.Throws<FileNotFoundException>(() => new TriggerRepository().LoadFrom(_temp.File("missing.json")));
    }

    [Fact]
    public void An_empty_reminders_export_imports_nothing()
    {
        var path = _temp.File("none.json");
        new TriggerRepository().SaveTo(path, Array.Empty<TriggerTimer>());

        Assert.Empty(new TriggerRepository().LoadFrom(path));
    }

    private void AssertUntouched(string path, string content, DateTime writtenUtc)
    {
        Assert.True(File.Exists(path), "the user's file was moved");
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal(writtenUtc, File.GetLastWriteTimeUtc(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_temp.Path));
    }

    // ---- Backups ----

    [Fact]
    public void A_whole_app_backup_restores_the_same_settings_and_reminders()
    {
        var path = _temp.File("backup.json");
        var settings = new AppSettings { SnoozeMinutes = 15, Use24HourTime = true, CheckForUpdatesOnStartup = true };
        var reminders = SampleReminders();

        BackupRepository.Save(path, new AppBackup { Settings = settings, Triggers = reminders });
        var restored = BackupRepository.Load(path)!;

        Assert.Equal(Json(settings), Json(restored.Settings));
        Assert.Equal(Json(reminders), Json(restored.Triggers));
    }

    [Fact]
    public void A_reminders_only_export_isnt_read_as_a_whole_app_backup()
    {
        var path = _temp.File("export.json");
        new TriggerRepository().SaveTo(path, SampleReminders());
        var content = File.ReadAllText(path);

        Assert.Null(BackupRepository.Load(path));
        AssertUntouched(path, content, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_settings_file_isnt_taken_for_an_empty_backup()
    {
        // Restoring one would otherwise replace every reminder with none.
        var path = _temp.File("settings.json");
        JsonStore.Save(path, new AppSettings { SnoozeMinutes = 9 });
        var content = File.ReadAllText(path);

        Assert.Null(BackupRepository.Load(path));
        AssertUntouched(path, content, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("""{ "Settings": {}, "Triggers": null }""")]
    [InlineData("""{ "Settings": null, "Triggers": [] }""")]
    [InlineData("""{ "Triggers": [] }""")]
    [InlineData("""{ "Settings": {} }""")]
    [InlineData("""[ { "Settings": {}, "Triggers": [] } ]""")]
    public void A_backup_must_have_both_its_settings_and_its_reminders(string json)
    {
        var path = _temp.File("backup.json");
        File.WriteAllText(path, json);

        Assert.Null(BackupRepository.Load(path));
        AssertUntouched(path, json, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_backup_with_no_reminders_still_restores()
    {
        var path = _temp.File("backup.json");
        BackupRepository.Save(path, new AppBackup { Settings = new AppSettings { SnoozeMinutes = 9 }, Triggers = new() });

        var backup = BackupRepository.Load(path)!;

        Assert.Empty(backup.Triggers);
        Assert.Equal(9, backup.Settings!.SnoozeMinutes);
    }

    // ---- Upgrading: Data folders as each release wrote them ----

    public static TheoryData<string> Releases => ReleaseFixtures.All;

    private static string Fixture(string release, string file) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", release, file);

    /// A copy in this test's temp folder, so a failed load's quarantine can't touch the fixture.
    private string Copy(string release, string file)
    {
        var path = _temp.File($"{release}-{file}");
        File.Copy(Fixture(release, file), path);
        return path;
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void Settings_from_a_release_load_with_every_value_kept(string release)
    {
        var path = Copy(release, "settings.json");
        var original = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        var s = new SettingsRepository(path).Load();

        Assert.True(File.Exists(path), "the file was set aside as corrupt");
        var reloaded = JsonNode.Parse(JsonSerializer.Serialize(s))!.AsObject();
        foreach (var (key, value) in original)
        {
            Assert.True(reloaded.ContainsKey(key), $"{release}'s setting {key} has no home in this version");
            Assert.Equal(value?.ToJsonString(), reloaded[key]?.ToJsonString());
        }

        Assert.Equal(10, s.SnoozeMinutes);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal("Ctrl+Alt+P", s.QuickAddHotkeyGesture);
        Assert.Equal(TextToSpeechEngine.Piper, s.TextToSpeechEngine);
        Assert.Equal("en_GB-jenny_dioco-medium.onnx", s.PiperVoiceModelFileName);
        Assert.Equal(SpeechToTextEngine.Whisper, s.SpeechToTextEngine);
        Assert.Equal("ggml-base.en.bin", s.WhisperModelFileName);
        Assert.False(s.RepeatAlertUntilDismissed);
        Assert.Equal(15, s.AutoResolveMinutes);
    }

    [Fact]
    public void Settings_from_1_0_0_which_had_no_update_check_keep_it_on()
    {
        var s = new SettingsRepository(Copy("v1.0.0", "settings.json")).Load();

        Assert.True(s.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void Settings_from_1_1_0_keep_the_update_check_as_they_had_it()
    {
        var s = new SettingsRepository(Copy("v1.1.0", "settings.json")).Load();

        Assert.False(s.CheckForUpdatesOnStartup);
        Assert.Equal("1.1.0", s.LastKnownLatestVersion);
        Assert.True(s.QuickAddAutoListen);
    }

    [Fact]
    public void Reminders_from_1_2_0_keep_what_it_added_a_span_a_snooze_and_a_relative_day()
    {
        var loaded = new TriggerRepository().LoadFrom(Copy("v1.2.0", "triggers.json"));

        var dentist = loaded.Single(t => t.Name == "Call the dentist");
        Assert.Equal(new DateTime(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc), dentist.DueUtc);
        Assert.Equal(new DateTime(2026, 9, 3, 15, 0, 0), dentist.TriggerAt);

        var standUp = loaded.Single(t => t.Name == "Stand-up");
        Assert.Equal(new DateTime(2026, 8, 26, 9, 10, 0), standUp.TriggerAt);
        Assert.Equal(new DateTime(2026, 8, 26, 9, 0, 0), standUp.SnoozedOccurrence);
        Assert.Equal(new DateTime(2026, 8, 26, 8, 10, 0, DateTimeKind.Utc), standUp.DueUtc);

        var bookClub = loaded.Single(t => t.Name == "Book club").Recurrence!;
        Assert.Equal(RecurrenceFrequency.Monthly, bookClub.Frequency);
        Assert.Equal(MonthDayRule.RelativeDay, bookClub.MonthDayRule);
        Assert.Equal(WeekOfMonth.Last, bookClub.WeekOfMonth);
        Assert.Equal(RelativeDay.Friday, bookClub.RelativeDay);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void Reminders_from_a_release_load_with_every_reminder_and_value_kept(string release)
    {
        var path = Copy(release, "triggers.json");
        var original = JsonNode.Parse(File.ReadAllText(path))!.AsArray();

        var loaded = new TriggerRepository().LoadFrom(path);

        Assert.Equal(ReleaseFixtures.Reminders(release), loaded.Count);
        Assert.Equal(original.Count, loaded.Count);
        var reloaded = JsonNode.Parse(JsonSerializer.Serialize(loaded))!.AsArray();
        for (var i = 0; i < original.Count; i++)
            AssertKeepsEveryValue(original[i]!.AsObject(), reloaded[i]!.AsObject(), $"{release} reminder {i}");

        var rent = loaded.Single(t => t.Name == "Pay rent");
        Assert.Equal(RecurrenceFrequency.Monthly, rent.Recurrence!.Frequency);
        Assert.Equal(31, rent.Recurrence.DayOfMonth);
        Assert.Equal(new DateTime(2027, 1, 31), rent.Recurrence.EndDate);
        var standUp = loaded.Single(t => t.Name == "Stand-up");
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, standUp.Recurrence!.DaysOfWeek);
        Assert.Equal(AlertMode.SoundAndSpeech, standUp.Mode);
        Assert.Equal("Stand-up in five", standUp.Phrase);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_backup_from_a_release_restores_every_reminder_and_setting(string release)
    {
        var path = Copy(release, "backup.json");

        var backup = BackupRepository.Load(path)!;

        Assert.Equal(ReleaseFixtures.Reminders(release), backup.Triggers.Count);
        Assert.Equal(10, backup.Settings!.SnoozeMinutes);
        var original = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var reloaded = JsonNode.Parse(JsonSerializer.Serialize(backup))!.AsObject();
        AssertKeepsEveryValue(original["Settings"]!.AsObject(), reloaded["Settings"]!.AsObject(), $"{release} backup settings");
        var originalTriggers = original["Triggers"]!.AsArray();
        for (var i = 0; i < originalTriggers.Count; i++)
            AssertKeepsEveryValue(originalTriggers[i]!.AsObject(), reloaded["Triggers"]![i]!.AsObject(), $"{release} backup reminder {i}");
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void After_an_upgrade_the_first_launch_keeps_every_reminder(string release)
    {
        // Months after the release: everything due has gone by. Nothing is lost; the one-offs that
        // came due are spent, the recurring ones move on.
        var reminders = new TriggerRepository().LoadFrom(Copy(release, "triggers.json"));

        var missed = Engine.TriggerReconciler.CatchUpMissed(reminders, new DateTime(2026, 11, 1, 12, 0, 0));

        Assert.Equal(ReleaseFixtures.Reminders(release), reminders.Count);
        Assert.Contains("Call the dentist", missed);
        Assert.True(reminders.Single(t => t.Name == "Call the dentist").HasFired);
        var standUp = reminders.Single(t => t.Name == "Stand-up");
        Assert.True(standUp.TriggerAt > new DateTime(2026, 11, 1, 12, 0, 0));
        Assert.True(standUp.Enabled);
        var tablets = reminders.Single(t => t.Name == "Take tablets");
        Assert.False(tablets.Enabled);
        Assert.Equal(new DateTime(2026, 8, 26, 21, 0, 0), tablets.TriggerAt);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void Data_from_a_release_saved_again_by_this_version_loads_the_same(string release)
    {
        var settings = new SettingsRepository(Copy(release, "settings.json")).Load();
        var reminders = new TriggerRepository().LoadFrom(Copy(release, "triggers.json"));

        var settingsPath = _temp.File("resaved-settings.json");
        var remindersPath = _temp.File("resaved-triggers.json");
        new SettingsRepository(settingsPath).Save(settings);
        new TriggerRepository().SaveTo(remindersPath, reminders);

        Assert.Equal(Json(settings), Json(new SettingsRepository(settingsPath).Load()));
        Assert.Equal(Json(reminders), Json(new TriggerRepository().LoadFrom(remindersPath)));
    }

    /// Every key and value the release wrote is still there, at every depth. Keys this version
    /// adds (a new property, saved with its default) are allowed: that's how the format grows.
    private static void AssertKeepsEveryValue(JsonObject original, JsonObject reloaded, string what)
    {
        foreach (var (key, value) in original)
        {
            Assert.True(reloaded.ContainsKey(key), $"{what}: {key} has no home in this version");
            if (value is JsonObject nested && reloaded[key] is JsonObject reloadedNested)
                AssertKeepsEveryValue(nested, reloadedNested, $"{what}.{key}");
            else
                Assert.Equal(value?.ToJsonString(), reloaded[key]?.ToJsonString());
        }
    }

    private static List<TriggerTimer> SampleReminders() => new()
    {
        new() { Name = "One-off", TriggerAt = new DateTime(2026, 6, 3, 15, 0, 0), RecurrenceAnchor = new DateTime(2026, 6, 3, 15, 0, 0), Mode = AlertMode.SoundOnly, SoundFileName = "chime.wav", Volume = 80, Color = "teal" },
        new() { Name = "Weekly", TriggerAt = new DateTime(2026, 6, 5, 9, 0, 0), RecurrenceAnchor = new DateTime(2026, 6, 1, 9, 0, 0), Mode = AlertMode.SpeechOnly, Phrase = "hello", Color = "plum",
            Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly, DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Friday }, EndType = RecurrenceEndType.AfterOccurrences, OccurrenceCount = 8, OccurrencesSoFar = 3 } },
        new() { Name = "Spent", TriggerAt = new DateTime(2026, 5, 1, 9, 0, 0), RecurrenceAnchor = new DateTime(2026, 5, 1, 9, 0, 0), HasFired = true, Enabled = false, Color = "ochre" },
    };
}
