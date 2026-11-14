using System.IO;
using System.Windows;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// The real commands with recorded decisions, no windows, and files only in a test-owned folder.
[Collection(nameof(SettingsScreen))]
public sealed class DialogCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pendulum-dialog-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _previousSettings;
    private readonly TriggerTimer[] _previousReminders;

    public DialogCommandTests()
    {
        (_previousSettings, _previousReminders) = UiThread.Invoke(() =>
        {
            var services = AppServices.Instance;
            services.FlushPendingSaves();
            var previous = (services.Settings, services.Triggers.ToArray());
            AppPaths.BaseDirectoryOverride = _folder;
            AppPaths.EnsureDirectories();
            services.UseSettingsForTests(new AppSettings { LaunchOnWindowsStartup = false });
            services.Reminders.ReplaceAll([]);
            return previous;
        });
    }

    private static TriggerTimer Reminder(string name) => new() { Name = name, TriggerAt = new DateTime(2090, 1, 1) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_asks_about_the_selected_reminder_and_respects_the_answer(bool accept) => UiThread.Invoke(() =>
    {
        var reminder = Reminder("dentist");
        AppServices.Instance.AddTrigger(reminder);
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(accept);
        var vm = new TriggersViewModel(dialogs) { SelectedTrigger = reminder };

        vm.DeleteCommand.Execute(null);

        Assert.Equal((string.Format(Strings.Reminders_ConfirmDelete, reminder.Name), false), Assert.Single(dialogs.Confirmations));
        Assert.Equal(!accept, AppServices.Instance.Triggers.Contains(reminder));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteSelected_only_removes_ticked_reminders_after_confirmation(bool accept) => UiThread.Invoke(() =>
    {
        var selected = Reminder("selected");
        var other = Reminder("other");
        AppServices.Instance.Reminders.AddRange([selected, other]);
        var dialogs = new FakeDialogs();
        dialogs.Answers.Enqueue(accept);
        var vm = new TriggersViewModel(dialogs) { IsSelecting = true };
        vm.DeleteSelectedCommand.Execute(null);
        Assert.Empty(dialogs.Confirmations);
        selected.IsSelectedForBulk = true;

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal((string.Format(Strings.Reminders_ConfirmDeleteSelected, 1), true), Assert.Single(dialogs.Confirmations));
        Assert.Equal(!accept, AppServices.Instance.Triggers.Contains(selected));
        Assert.Contains(other, AppServices.Instance.Triggers);
        Assert.Equal(!accept, vm.IsSelecting);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_asks_to_replace_duplicates_and_reports_replaced_or_skipped_counts(bool replace) => UiThread.Invoke(() =>
    {
        var existing = Reminder("same");
        AppServices.Instance.AddTrigger(existing);
        var path = Path.Combine(_folder, "import.json");
        var incoming = Reminder("same");
        incoming.TriggerAt = new DateTime(2091, 1, 1);
        new TriggerRepository().SaveTo(path, [incoming, Reminder("new")]);
        var dialogs = new FakeDialogs { OpenPath = path };
        dialogs.Answers.Enqueue(replace);

        new TriggersViewModel(dialogs).ImportCommand.Execute(null);

        Assert.Equal((Strings.Dialog_ImportRemindersTitle, Strings.Dialog_RemindersFilter), Assert.Single(dialogs.OpenRequests));
        Assert.Equal((string.Format(Strings.Reminders_ConfirmReplace, "same"), false), Assert.Single(dialogs.Confirmations));
        Assert.Equal(2, AppServices.Instance.Triggers.Count);
        var same = AppServices.Instance.Triggers.Single(t => t.Name == "same");
        Assert.Equal(replace ? incoming.TriggerAt : existing.TriggerAt, same.TriggerAt);
        Assert.Equal(!replace, ReferenceEquals(existing, same));
        Assert.Equal(replace ? "Imported 2 reminder(s) (1 replaced)." : "Imported 1 reminder(s), skipped 1.", Assert.Single(dialogs.Information));
        Assert.Empty(dialogs.Warnings);
    });

    [Fact]
    public void Exports_use_the_chosen_paths_and_report_the_actual_counts() => UiThread.Invoke(() =>
    {
        var selected = Reminder("selected");
        selected.IsSelectedForBulk = true;
        AppServices.Instance.Reminders.AddRange([selected, Reminder("other")]);
        var dialogs = new FakeDialogs { SavePath = Path.Combine(_folder, "reminders.json") };
        var vm = new TriggersViewModel(dialogs) { IsSelecting = true };

        vm.ExportCommand.Execute(null);

        Assert.Equal("selected", Assert.Single(new TriggerRepository().LoadFrom(dialogs.SavePath)).Name);
        Assert.False(vm.IsSelecting);
        Assert.Equal(string.Format(Strings.Reminders_Exported, 1, "reminders.json"), Assert.Single(dialogs.Information));
        Assert.Equal(Strings.Dialog_ExportRemindersTitle, Assert.Single(dialogs.SaveRequests).Title);
        dialogs.SavePath = Path.Combine(_folder, "backup.json");
        new SettingsViewModel(dialogs).ExportBackupCommand.Execute(null);
        Assert.Equal(2, BackupRepository.Load(dialogs.SavePath).Triggers.Count);
        Assert.Equal(string.Format(Strings.Backup_Done, 2, "backup.json"), dialogs.Information.Last());
        Assert.Equal(Strings.Dialog_BackupFilter, dialogs.SaveRequests.Last().Filter);
    });

    [Fact]
    public void Import_errors_show_the_error_message_without_changing_reminders() => UiThread.Invoke(() =>
    {
        var original = Reminder("original");
        AppServices.Instance.AddTrigger(original);
        var path = Path.Combine(_folder, "not-reminders.json");
        File.WriteAllText(path, "{}");
        var dialogs = new FakeDialogs { OpenPath = path };

        new TriggersViewModel(dialogs).ImportCommand.Execute(null);

        var warning = Assert.Single(dialogs.Warnings);
        Assert.True(warning.Error);
        Assert.Equal(string.Format(Strings.Reminders_CouldNotImport, Pendulum.Core.Resources.CoreStrings.File_NotReminderList), warning.Message);
        Assert.Same(original, Assert.Single(AppServices.Instance.Triggers));
        Assert.Empty(dialogs.Information);
    });

    [Fact]
    public void Import_summary_keeps_its_punctuation_and_caught_up_explanation() => UiThread.Invoke(() =>
    {
        var past = Reminder("past");
        past.TriggerAt = new DateTime(2000, 1, 1);
        var path = Path.Combine(_folder, "import.json");
        new TriggerRepository().SaveTo(path, [past]);
        var dialogs = new FakeDialogs { OpenPath = path };

        new TriggersViewModel(dialogs).ImportCommand.Execute(null);

        Assert.Equal("Imported 1 reminder(s)." + string.Format(Strings.Reminders_ImportedCaughtUp, 1), Assert.Single(dialogs.Information));
        Assert.True(Assert.Single(AppServices.Instance.Triggers).HasFired);
    });

    [Fact]
    public void Cancelled_file_pickers_leave_data_unchanged_and_show_no_followup() => UiThread.Invoke(() =>
    {
        var reminder = Reminder("existing");
        AppServices.Instance.AddTrigger(reminder);
        var dialogs = new FakeDialogs();
        var triggers = new TriggersViewModel(dialogs);
        var settings = new SettingsViewModel(dialogs);
        triggers.ImportCommand.Execute(null);
        triggers.ExportCommand.Execute(null);
        settings.RestoreBackupCommand.Execute(null);
        settings.ExportBackupCommand.Execute(null);
        settings.AddSoundCommand.Execute(null);
        Assert.Same(reminder, Assert.Single(AppServices.Instance.Triggers));
        Assert.Empty(dialogs.Confirmations);
        Assert.Empty(dialogs.Information);
        Assert.Empty(dialogs.Warnings);
        Assert.Equal(3, dialogs.OpenRequests.Count);
        Assert.Equal(2, dialogs.SaveRequests.Count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_replaces_reminders_and_settings_only_after_confirmation(bool accept) => UiThread.Invoke(() =>
    {
        var original = Reminder("original");
        AppServices.Instance.AddTrigger(original);
        var vmDialogs = new FakeDialogs { OpenPath = Path.Combine(_folder, "backup.json") };
        vmDialogs.Answers.Enqueue(accept);
        BackupRepository.Save(vmDialogs.OpenPath, new AppBackup
        {
            Settings = new AppSettings { SnoozeMinutes = 19 },
            Triggers = [Reminder("restored")]
        });
        var vm = new SettingsViewModel(vmDialogs);
        var before = vm.Settings.SnoozeMinutes;

        vm.RestoreBackupCommand.Execute(null);

        Assert.Equal((Strings.Backup_ConfirmRestore, true), Assert.Single(vmDialogs.Confirmations));
        Assert.Equal(accept ? "restored" : "original", Assert.Single(AppServices.Instance.Triggers).Name);
        Assert.Equal(accept ? 19 : before, vm.Settings.SnoozeMinutes);
        Assert.False(vm.Settings.LaunchOnWindowsStartup);
        if (accept) Assert.Equal(string.Format(Strings.Backup_Restored, 1), Assert.Single(vmDialogs.Information));
        else Assert.Empty(vmDialogs.Information);
    });

    [Fact]
    public void A_locked_restore_reports_the_read_error_through_dialogs_and_keeps_data() => UiThread.Invoke(() =>
    {
        var original = Reminder("original");
        AppServices.Instance.AddTrigger(original);
        var path = Path.Combine(_folder, "backup.json");
        BackupRepository.Save(path, new AppBackup { Settings = new(), Triggers = [] });
        var dialogs = new FakeDialogs { OpenPath = path };
        dialogs.Answers.Enqueue(true);
        var vm = new SettingsViewModel(dialogs);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            vm.RestoreBackupCommand.Execute(null);

        var warning = Assert.Single(dialogs.Warnings);
        Assert.True(warning.Error);
        Assert.Contains(Path.GetFileName(path), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Strings.Backup_NotValid, warning.Message, StringComparison.Ordinal);
        Assert.Empty(dialogs.Information);
        Assert.Same(original, Assert.Single(AppServices.Instance.Triggers));
    });

    [Fact]
    public void Reset_and_hotkey_changes_respect_cancel_and_accept() => UiThread.Invoke(() =>
    {
        var dialogs = new FakeDialogs();
        var vm = new SettingsViewModel(dialogs);
        vm.Settings.SnoozeMinutes = 19;
        vm.ResetToDefaultsCommand.Execute(null);
        Assert.Equal(19, vm.Settings.SnoozeMinutes);
        dialogs.Answers.Enqueue(true);
        vm.ResetToDefaultsCommand.Execute(null);
        Assert.Equal(new AppSettings().SnoozeMinutes, vm.Settings.SnoozeMinutes);
        Assert.False(vm.Settings.LaunchOnWindowsStartup);
        var before = vm.Settings.QuickAddHotkeyGesture;
        vm.ChangeHotkeyCommand.Execute(null);
        Assert.Equal(before, vm.Settings.QuickAddHotkeyGesture);
        dialogs.HotkeyResult = "Ctrl+Alt+P";
        vm.ChangeHotkeyCommand.Execute(null);
        Assert.Equal(dialogs.HotkeyResult, vm.Settings.QuickAddHotkeyGesture);
        Assert.Equal(new[] { before, before }, dialogs.HotkeyRequests);
    });

    [Fact]
    public void Add_only_commits_saved_drafts_and_list_and_calendar_share_the_editor() => UiThread.Invoke(() =>
    {
        var dialogs = new FakeDialogs();
        var vm = new TriggersViewModel(dialogs);
        vm.AddCommand.Execute(null);
        Assert.Empty(AppServices.Instance.Triggers);
        var reminder = Reminder("saved");
        dialogs.EditResult = reminder;
        vm.AddCommand.Execute(null);
        Assert.Same(reminder, Assert.Single(AppServices.Instance.Triggers));
        Assert.Same(reminder, vm.SelectedTrigger);
        vm.EditCommand.Execute(null);
        new CalendarViewModel(dialogs).EditReminderCommand.Execute(reminder);
        Assert.Equal(new[] { true, true, false, false }, dialogs.Edits.Select(e => e.IsNew));
        Assert.All(dialogs.Edits.Skip(2), edit => Assert.Same(reminder, edit.Reminder));
    });

    /// A day cell's Add reminder: the same new-reminder editor, on the cell's own date (here a day
    /// of the month before, shown in the grid) at 00:00:00; Cancel adds nothing, and Save adds it
    /// on that day, where the calendar shows it.
    [Fact]
    public void Calendar_add_reminder_opens_the_new_reminder_editor_on_the_cell_s_date_at_midnight() => UiThread.Invoke(() =>
    {
        AppServices.Instance.UseSettingsForTests(new AppSettings { LaunchOnWindowsStartup = false, DefaultSoundFileName = "chime.wav" });
        var dialogs = new FakeDialogs();
        var calendar = new CalendarViewModel(AppServices.Instance.Reminders, () => new DateTime(2090, 4, 10, 15, 20, 45), action => action(), dialogs);
        calendar.SetVisible(true);
        var day = calendar.Days.First(d => !d.IsCurrentMonth);
        Assert.Equal(3, day.Date.Month);

        calendar.AddReminderCommand.Execute(day);   // then Cancel

        var (cancelled, isNew) = Assert.Single(dialogs.Edits);
        Assert.True(isNew);
        Assert.Equal(day.Date.Date, cancelled.TriggerAt);
        Assert.Equal(day.Date.Date, cancelled.RecurrenceAnchor);
        Assert.Equal(Strings.Reminders_NewReminderName, cancelled.Name);
        Assert.Equal("chime.wav", cancelled.SoundFileName);
        Assert.Empty(AppServices.Instance.Triggers);

        foreach (var use24 in new[] { true, false })
        {
            var editor = new EditorViewModel(cancelled, isNew: true, use24, [], "teal");
            Assert.Equal(day.Date.Date, editor.Date);
            Assert.Equal(use24 ? "00" : "12", editor.Hour);
            Assert.Equal(0, editor.AmPmIndex);
            Assert.Equal("00", editor.Minute);
            Assert.Equal("00", editor.Second);
        }

        // Save, after changing the time to 09:30, as the editor's Save applies it.
        dialogs.OnEdit = () =>
        {
            var draft = dialogs.Edits[^1].Reminder;
            var editor = new EditorViewModel(draft, isNew: true, use24HourTime: true, [], "teal") { Hour = "09", Minute = "30" };
            var now = new DateTime(2090, 1, 1);
            TriggerReconciler.ApplyEdit(draft, editor.CreateDraft(now), now);
            dialogs.EditResult = draft;
        };
        calendar.AddReminderCommand.Execute(day);

        var saved = Assert.Single(AppServices.Instance.Triggers);
        Assert.Same(dialogs.Edits[^1].Reminder, saved);
        Assert.Equal(day.Date.AddHours(9).AddMinutes(30), saved.TriggerAt);
        Assert.Contains(saved, calendar.Days.Single(d => d.Date == day.Date).Reminders);
        Assert.Equal(1, calendar.Days.Sum(d => d.Reminders.Count));
    });

    public void Dispose()
    {
        UiThread.Invoke(() =>
        {
            AppServices.Instance.FlushPendingSaves();
            AppServices.Instance.Reminders.ReplaceAll(_previousReminders);
            AppServices.Instance.UseSettingsForTests(_previousSettings);
            AppPaths.BaseDirectoryOverride = null;
            Assert.Empty(Application.Current.Windows);
        });
        Directory.Delete(_folder, recursive: true);
    }
}
