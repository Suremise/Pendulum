using System.Reflection;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// Restore and Reset copy every setting the user chose, found by reflection, so none is skipped.
/// Start with Windows and the bookkeeping are left as they are. Temp folders only.
public sealed class SettingsCopyTests
{
    private static readonly string BackupFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.1.0", "backup.json");

    private static IEnumerable<PropertyInfo> AllSaved => typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite);

    /// Saved values that aren't settings the user chose. A new one is a deliberate choice: add it
    /// here and mark it [Bookkeeping]; anything else is copied by Restore and Reset.
    private static readonly string[] Bookkeeping =
    [
        nameof(AppSettings.LastUpdateCheckUtc), nameof(AppSettings.LastKnownLatestVersion),
        nameof(AppSettings.HasShownTrayMinimizeHint), nameof(AppSettings.ReminderFilterRowVisible),
        nameof(AppSettings.ReminderFilterName), nameof(AppSettings.ReminderFilterTypeFixed),
        nameof(AppSettings.ReminderFilterTypeScheduled), nameof(AppSettings.ReminderFilterStatusUpcoming),
        nameof(AppSettings.ReminderFilterStatusSpent), nameof(AppSettings.ReminderFilterStatusDisabled),
        nameof(AppSettings.ReminderFilterModeSoundOnly), nameof(AppSettings.ReminderFilterModeSoundAndSpeech),
        nameof(AppSettings.ReminderFilterModeSpeechOnly), nameof(AppSettings.ReminderFilterFrom),
        nameof(AppSettings.ReminderFilterTo),
    ];

    [Fact]
    public void Every_saved_value_is_copied_except_the_bookkeeping_and_Start_with_Windows()
    {
        var copied = AppSettings.CopiedSettings.Select(p => p.Name).ToHashSet();
        var expected = AllSaved.Select(p => p.Name)
            .Except(Bookkeeping)
            .Except([nameof(AppSettings.LaunchOnWindowsStartup)])
            .ToHashSet();

        Assert.Equal(expected.Order(), copied.Order());
        Assert.Contains(nameof(AppSettings.AutoDeleteSpentAfterDays), copied);
        Assert.Equal(Bookkeeping.Order(), AllSaved.Where(p => p.GetCustomAttribute<BookkeepingAttribute>() is not null).Select(p => p.Name).Order());
    }

    [Fact]
    public void A_v1_1_0_backup_restores_every_setting()
    {
        var backup = BackupRepository.Load(BackupFixture)!.Settings!;
        var current = new AppSettings { LaunchOnWindowsStartup = false, ReminderFilterName = "mine", HasShownTrayMinimizeHint = false };

        current.CopySettingsFrom(backup);

        foreach (var p in AppSettings.CopiedSettings)
            Assert.True(Equals(p.GetValue(backup), p.GetValue(current)), p.Name);
        Assert.Equal(30, current.AutoDeleteSpentAfterDays);
        // Left as they were: Start with Windows, and this PC's own bookkeeping.
        Assert.False(current.LaunchOnWindowsStartup);
        Assert.Equal("mine", current.ReminderFilterName);
        Assert.False(current.HasShownTrayMinimizeHint);
    }

    [Fact]
    public void Reset_gives_the_default_for_every_setting()
    {
        var current = BackupRepository.Load(BackupFixture)!.Settings!;
        var defaults = new AppSettings();
        Assert.Contains(AppSettings.CopiedSettings, p => !Equals(p.GetValue(defaults), p.GetValue(current)));

        current.CopySettingsFrom(defaults);

        foreach (var p in AppSettings.CopiedSettings)
            Assert.True(Equals(p.GetValue(defaults), p.GetValue(current)), p.Name);
        Assert.Equal(0, current.AutoDeleteSpentAfterDays);
        Assert.True(current.LaunchOnWindowsStartup);  // as the backup had it: Reset leaves it
        Assert.Equal("dentist", current.ReminderFilterName);
    }
}
