using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// JSON of the right outline but not Pendulum's own: refused before anything is imported or
/// restored, and the user's file is left exactly as it was. Temp folders only.
public sealed class UserFileShapeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private const string Reminder = """{ "Name": "Call the dentist", "TriggerAt": "2026-09-03T15:00:00" }""";
    private const string Settings = """{ "SnoozeMinutes": 8 }""";

    public static TheoryData<string> NotReminderLists => new()
    {
        """[ { "title": "shopping", "items": ["milk"] } ]""",
        """[ {} ]""",
        """[ null ]""",
        """[ "Call the dentist" ]""",
        """[ [ ] ]""",
        $"""[ {Reminder}, {"{"} "title": "shopping" {"}"} ]""",
        $"""[ {Reminder}, null ]""",
        """[ { "Name": "No time" } ]""",
        """[ { "TriggerAt": "2026-09-03T15:00:00" } ]""",
        """[ { "Name": 42, "TriggerAt": "2026-09-03T15:00:00" } ]""",
        """[ { "Name": "Bad time", "TriggerAt": "next tuesday" } ]""",
        """[ { "Name": "Bad time", "TriggerAt": 20260903 } ]""",
        """[ { "Name": "Odd repeat", "TriggerAt": "2026-09-03T15:00:00", "Recurrence": "daily" } ]""",
    };

    [Theory]
    [MemberData(nameof(NotReminderLists))]
    public void Importing_a_list_that_isnt_reminders_is_refused_and_the_file_left_alone(string json)
    {
        var path = Write("import.json", json);

        Assert.Throws<InvalidDataException>(() => new TriggerRepository().LoadFrom(path));

        AssertUntouched(path, json);
    }

    [Theory]
    [MemberData(nameof(NotReminderLists))]
    public void Restoring_a_backup_whose_reminders_arent_reminders_is_refused_and_the_file_left_alone(string triggers)
    {
        var json = $$"""{ "Settings": {{Settings}}, "Triggers": {{triggers}} }""";
        var path = Write("backup.json", json);

        Assert.Null(BackupRepository.Load(path));

        AssertUntouched(path, json);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "title": "shopping" }""")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"settings\"")]
    public void Restoring_a_backup_whose_settings_arent_pendulums_is_refused(string settings)
    {
        var json = $$"""{ "Settings": {{settings}}, "Triggers": [ {{Reminder}} ] }""";
        var path = Write("backup.json", json);

        Assert.Null(BackupRepository.Load(path));

        AssertUntouched(path, json);
    }

    [Fact]
    public void A_minimal_reminder_imports()
    {
        var path = Write("import.json", $"[ {Reminder} ]");

        var reminder = Assert.Single(new TriggerRepository().LoadFrom(path));

        Assert.Equal("Call the dentist", reminder.Name);
        Assert.Equal(new DateTime(2026, 9, 3, 15, 0, 0), reminder.TriggerAt);
    }

    [Fact]
    public void A_reminder_with_its_recurrence_null_or_set_imports()
    {
        var path = Write("import.json", """
            [
              { "Name": "a", "TriggerAt": "2026-09-03T15:00:00", "Recurrence": null },
              { "Name": "b", "TriggerAt": "2026-09-03T15:00:00", "Recurrence": { "Frequency": 1 } }
            ]
            """);

        var reminders = new TriggerRepository().LoadFrom(path);

        Assert.Null(reminders[0].Recurrence);
        Assert.Equal(RecurrenceFrequency.Daily, reminders[1].Recurrence!.Frequency);
    }

    [Fact]
    public void A_backup_with_real_settings_and_reminders_restores()
    {
        var path = Write("backup.json", $$"""{ "Settings": {{Settings}}, "Triggers": [ {{Reminder}} ] }""");

        var backup = BackupRepository.Load(path)!;

        Assert.Equal(8, backup.Settings!.SnoozeMinutes);
        Assert.Equal("Call the dentist", Assert.Single(backup.Triggers).Name);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v1.1.0")]
    public void Every_release_fixture_still_passes(string release)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", release);
        var triggers = Path.Combine(_temp.Path, $"{release}-triggers.json");
        var backup = Path.Combine(_temp.Path, $"{release}-backup.json");
        File.Copy(Path.Combine(fixtures, "triggers.json"), triggers);
        File.Copy(Path.Combine(fixtures, "backup.json"), backup);

        Assert.Equal(6, new TriggerRepository().LoadFrom(triggers).Count);
        Assert.Equal(6, BackupRepository.Load(backup)!.Triggers.Count);
    }

    private string Write(string name, string content)
    {
        var path = _temp.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    private void AssertUntouched(string path, string content)
    {
        Assert.True(File.Exists(path), "the user's file was moved");
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_temp.Path));
    }
}
