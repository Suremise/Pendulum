using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class ReminderBatchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pendulum-batch-tests-" + Guid.NewGuid().ToString("N"));
    private static TriggerTimer Reminder(string name) => new() { Name = name, TriggerAt = new DateTime(2090, 1, 1), RecurrenceAnchor = new DateTime(2090, 1, 1) };

    [Fact]
    public void Importing_fifty_reminders_requests_one_save() => UiThread.Invoke(() =>
    {
        var clock = new CountingClock();
        var services = new AppServices((_, _) => { }, clock);
        try
        {
            var file = Path.Combine(_folder, "import.json");
            new TriggerRepository().SaveTo(file, Enumerable.Range(0, 50).Select(i => Reminder($"Reminder {i}")));
            var result = services.ImportTriggersMerge(file, _ => true);
            Assert.Equal(50, result.Added);
            Assert.Equal(50, services.Triggers.Count);
            Assert.Equal(1, clock.Requests);
            Assert.Equal(1, services.Engine.TriggerSetCount);
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void An_editor_update_refreshes_and_requests_saving_once() => UiThread.Invoke(() =>
    {
        var clock = new CountingClock();
        var writes = 0;
        var services = new AppServices((_, _) => writes++, clock);
        var reminder = Reminder("before");
        try
        {
            services.AddTrigger(reminder);
            services.FlushPendingSaves();
            clock.Requests = writes = 0;
            var refreshes = services.Engine.TriggerSetCount;
            Assert.True(services.Update(reminder, () =>
            {
                reminder.Name = "edited";
                reminder.Recurrence = new();
                reminder.RecurrenceAnchor = reminder.TriggerAt.AddDays(1);
                reminder.TriggerAt = reminder.RecurrenceAnchor;
                reminder.Mode = AlertMode.SoundAndSpeech;
                reminder.SoundFileName = "bell.wav";
                reminder.Phrase = "edited phrase";
                reminder.Volume = 30;
                reminder.Enabled = false;
            }));
            Assert.Equal(1, clock.Requests);
            Assert.Equal(refreshes + 1, services.Engine.TriggerSetCount);
            services.FlushPendingSaves();
            Assert.Equal(1, writes);
            refreshes = services.Engine.TriggerSetCount;
            clock.Requests = 0;
            services.ChangeRemindersTogether(() => reminder.IsSelectedForBulk = true);
            Assert.Equal(0, clock.Requests);
            Assert.Equal(refreshes, services.Engine.TriggerSetCount);
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void Import_keeps_independent_edits_additions_and_removals_made_during_prompts() => UiThread.Invoke(() =>
    {
        var clock = new CountingClock();
        var services = new AppServices((_, _) => { }, clock);
        var matched = Reminder("replace");
        var retained = Reminder("retained");
        var independent = Reminder("independent");
        var refreshes = 0;
        try
        {
            services.Reminders.AddRange([matched, retained]);
            var file = Path.Combine(_folder, "import.json");
            new TriggerRepository().SaveTo(file, [Reminder("new before prompt"), Reminder("replace")]);
            var result = services.ImportTriggersMerge(file, _ =>
            {
                Assert.DoesNotContain(services.Triggers, t => t.Name == "new before prompt");
                services.Update(retained, () => { retained.Name = "changed during prompt"; retained.HasFired = true; });
                services.RemoveTrigger(matched);
                services.AddTrigger(independent);
                services.FlushPendingSaves();
                clock.Requests = 0;
                refreshes = services.Engine.TriggerSetCount;
                return true;
            });
            Assert.Equal(1, result.Added);
            Assert.Equal(1, result.Replaced);
            Assert.Equal(new[] { "changed during prompt", "independent", "new before prompt", "replace" }, services.Triggers.Select(t => t.Name));
            Assert.Same(retained, services.Triggers[0]);
            Assert.True(retained.HasFired);
            Assert.Same(independent, services.Triggers[1]);
            Assert.DoesNotContain(matched, services.Triggers);
            Assert.Equal(1, clock.Requests);
            Assert.Equal(refreshes + 1, services.Engine.TriggerSetCount);
        }
        finally { services.Shutdown(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_import_names_keep_sequential_replace_and_skip_decisions(bool replaceSecond) => UiThread.Invoke(() =>
    {
        var services = new AppServices((_, _) => { }, new CountingClock());
        try
        {
            services.AddTrigger(Reminder("same"));
            var first = Reminder("same"); first.Phrase = "first";
            var second = Reminder("same"); second.Phrase = "second";
            var file = Path.Combine(_folder, "import.json");
            new TriggerRepository().SaveTo(file, [first, second]);
            var prompts = 0;
            var result = services.ImportTriggersMerge(file, _ => ++prompts == 1 || replaceSecond);
            Assert.Equal(2, prompts);
            var imported = Assert.Single(services.Triggers);
            Assert.Equal(replaceSecond ? "second" : "first", imported.Phrase);
            Assert.NotEqual(first.Id, imported.Id);
            Assert.NotEqual(second.Id, imported.Id);
            Assert.Equal(replaceSecond ? 2 : 1, result.Replaced);
            Assert.Equal(replaceSecond ? 0 : 1, result.Skipped);
        }
        finally { services.Shutdown(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recovered_and_set_aside_trigger_files_enable_later_persistence(bool hasBackup) => UiThread.Invoke(() =>
    {
        var previous = AppPaths.BaseDirectoryOverride;
        AppPaths.BaseDirectoryOverride = _folder;
        var repository = new TriggerRepository(Path.Combine(_folder, "injected", "triggers.json"));
        var services = new AppServices(timeProvider: new CountingClock(), triggerRepository: repository, persistReminders: false);
        try
        {
            AppPaths.EnsureDirectories();
            new SettingsRepository().Save(new AppSettings { LaunchOnWindowsStartup = false });
            Directory.CreateDirectory(Path.GetDirectoryName(repository.FilePath)!);
            if (hasBackup)
            {
                repository.Save([Reminder("recovered")]);
                repository.Save([Reminder("current")]);
            }
            File.WriteAllText(repository.FilePath, "broken JSON");
            services.Initialize();
            Assert.Equal(hasBackup ? 1 : 0, services.Triggers.Count);
            Assert.Contains(services.DataFileNotices, notice => notice.File == repository.FilePath
                && notice.Outcome == (hasBackup ? StoreOutcome.RecoveredFromBackup : StoreOutcome.SetAside));
            services.AddTrigger(Reminder("saved after recovery"));
            services.FlushPendingSaves();
            Assert.Contains(repository.Load(), reminder => reminder.Name == "saved after recovery");
            Assert.False(File.Exists(AppPaths.TriggersFile)); // injected path, never the default file
        }
        finally
        {
            services.Shutdown();
            AppPaths.BaseDirectoryOverride = previous;
        }
    });

    [Fact]
    public void Import_preserves_the_selected_unchanged_row_in_the_real_list_view() => UiThread.Invoke(() =>
    {
        var services = AppServices.Instance;
        var previous = services.Triggers.ToList();
        var selected = Reminder("selected");
        services.UseRemindersForTests();
        services.UseSettingsForTests(new());
        services.Reminders.ReplaceAll([selected]);
        try
        {
            var panel = new TriggersPanel { Width = 1100, Height = 700 };
            panel.Measure(new Size(1100, 700));
            panel.Arrange(new Rect(0, 0, 1100, 700));
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            panel.RemindersList.SelectedItem = selected;
            var file = Path.Combine(_folder, "import.json");
            new TriggerRepository().SaveTo(file, [Reminder("added")]);
            services.ImportTriggersMerge(file, _ => true);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.Same(selected, panel.RemindersList.SelectedItem);
            Assert.Same(selected, ((TriggersViewModel)panel.DataContext).SelectedTrigger);
        }
        finally { services.Reminders.ReplaceAll(previous); }
    });

    [Fact]
    public void Replaced_reminders_cannot_request_more_saves_after_restore() => UiThread.Invoke(() =>
    {
        var clock = new CountingClock();
        var services = new AppServices((_, _) => { }, clock);
        var old = Reminder("old");
        old.Recurrence = new();
        try
        {
            services.AddTrigger(old);
            var file = Path.Combine(_folder, "backup.json");
            BackupRepository.Save(file, new AppBackup { Settings = new(), Triggers = [Reminder("restored")] });
            services.RestoreBackup(file, out _);
            clock.Requests = 0;
            old.Name = "detached";
            old.Recurrence.OccurrencesSoFar++;
            Assert.Equal(0, clock.Requests);
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void A_failed_confirmation_leaves_the_whole_import_unapplied_even_if_saving_runs() => UiThread.Invoke(() =>
    {
        var snapshots = new List<JsonElement>();
        var services = new AppServices((_, snapshot) => snapshots.Add(snapshot), new CountingClock());
        var original = Reminder("existing");
        try
        {
            services.AddTrigger(original);
            services.FlushPendingSaves();
            var file = Path.Combine(_folder, "import.json");
            new TriggerRepository().SaveTo(file, [Reminder("new"), Reminder("existing")]);
            Assert.Throws<InvalidOperationException>(() => services.ImportTriggersMerge(file, _ =>
            {
                // A confirmation can pump the UI; a pending save must still see the old whole
                // collection, not the part of the import already visited by the loop.
                services.FlushPendingSaves();
                throw new InvalidOperationException("decision failed");
            }));
            Assert.Same(original, Assert.Single(services.Triggers));
            Assert.All(snapshots, snapshot => Assert.Equal(new[] { "existing" },
                snapshot.EnumerateArray().Select(item => item.GetProperty(nameof(TriggerTimer.Name)).GetString())));
        }
        finally { services.Shutdown(); }
    });

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class CountingClock : TimeProvider
    {
        public int Requests;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new CountingTimer(this);
        private sealed class CountingTimer(CountingClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime != Timeout.InfiniteTimeSpan) clock.Requests++;
                return true;
            }
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
