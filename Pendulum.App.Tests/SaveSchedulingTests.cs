using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class SaveSchedulingTests
{
    [Theory]
    [InlineData("fire")]
    [InlineData("snooze")]
    [InlineData("edit")]
    public void Reminder_changes_write_once_after_quiet_on_a_worker(string operation)
    {
        var clock = new SaveClock();
        using var finished = new ManualResetEventSlim();
        var writes = 0;
        var writerThread = 0;
        var ownerThread = 0;
        JsonElement saved = default;
        AppServices? services = null;
        try
        {
            UiThread.Invoke(() =>
            {
                ownerThread = Environment.CurrentManagedThreadId;
                services = new AppServices((_, snapshot) =>
                {
                    saved = snapshot;
                    writerThread = Environment.CurrentManagedThreadId;
                    Interlocked.Increment(ref writes);
                    finished.Set();
                }, clock);
                var at = new DateTime(2000, 1, 1, 12, 0, 0);
                var trigger = new TriggerTimer { TriggerAt = at, RecurrenceAnchor = at, Recurrence = new() };
                services.AddTrigger(trigger);
                services.FlushPendingSaves();
                writes = 0;
                finished.Reset();
                clock.Requests = 0;

                if (operation == "fire")
                {
                    services.Engine.Start(null);
                    services.Engine.CheckDue();
                    // HasFired is persisted; StatusSortOrder is derived and must be ignored.
                    Assert.Equal(1, clock.Requests);
                }
                else if (operation == "snooze")
                {
                    trigger.HasFired = true;
                    trigger.Recurrence.OccurrencesSoFar = 1;
                    TriggerReconciler.Snooze(trigger, at, 5, TimeZoneInfo.Utc);
                    services.RefreshTriggers(); // the alert callback does this too
                }
                else
                {
                    // The properties applied by the editor's Save, followed by its caller.
                    trigger.Name = "Edited";
                    trigger.RecurrenceAnchor = at.AddDays(1);
                    trigger.Recurrence = null;
                    trigger.Mode = AlertMode.SoundAndSpeech;
                    trigger.SoundFileName = "bell.wav";
                    trigger.Phrase = "A reminder";
                    trigger.Volume = 30;
                    trigger.Color = "teal";
                    trigger.TriggerAt = at.AddDays(1);
                    trigger.HasFired = false;
                    trigger.Enabled = false;
                    services.RefreshTriggers();
                }
                Assert.Equal(0, writes);
                clock.Fire();
            });
            // Let the posted snapshot run, without showing or focusing a window.
            UiThread.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
            UiThread.Invoke(() => services!.FlushPendingSaves());
            Assert.Equal(1, writes);
            Assert.NotEqual(ownerThread, writerThread);
            var reminder = saved[0];
            if (operation == "fire")
            {
                Assert.True(reminder.GetProperty(nameof(TriggerTimer.HasFired)).GetBoolean());
                Assert.Equal(1, reminder.GetProperty(nameof(TriggerTimer.Recurrence))
                    .GetProperty(nameof(RecurrenceRule.OccurrencesSoFar)).GetInt32());
            }
            if (operation == "snooze")
                Assert.False(reminder.GetProperty(nameof(TriggerTimer.HasFired)).GetBoolean());
            if (operation == "edit")
                Assert.Equal("Edited", reminder.GetProperty(nameof(TriggerTimer.Name)).GetString());
        }
        finally { UiThread.Invoke(() => services?.Shutdown()); }
    }

    [Fact]
    public void Filter_and_speech_sliders_are_coalesced_and_shutdown_flushes_immediately() => UiThread.Invoke(() =>
    {
        var snapshots = new List<JsonElement>();
        var services = new AppServices((_, snapshot) => snapshots.Add(snapshot));
        try
        {
            var settings = new AppSettings();
            services.UseSettingsForTests(settings, saveOnChange: true);
            settings.ReminderFilterName = "t";
            settings.ReminderFilterName = "tea";
            settings.TtsRate = 2;
            settings.TtsRate = 3;
            settings.TtsVolume = 50;
            Assert.Empty(snapshots);
            services.Shutdown();
            var saved = Assert.Single(snapshots);
            Assert.Equal(3, saved.GetProperty(nameof(AppSettings.TtsRate)).GetInt32());
            Assert.Equal(50, saved.GetProperty(nameof(AppSettings.TtsVolume)).GetInt32());
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void Selecting_a_row_does_not_schedule_a_save() => UiThread.Invoke(() =>
    {
        var clock = new SaveClock();
        var services = new AppServices((_, _) => { }, clock);
        try
        {
            var trigger = new TriggerTimer();
            services.AddTrigger(trigger);
            services.FlushPendingSaves();
            clock.Requests = 0;
            trigger.IsSelectedForBulk = true;
            Assert.Equal(0, clock.Requests);
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void Closing_right_after_changes_puts_both_files_on_disk() => UiThread.Invoke(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "pendulum-save-tests-" + Guid.NewGuid().ToString("N"));
        var previous = Pendulum.Core.Persistence.AppPaths.BaseDirectoryOverride;
        var services = new AppServices();
        Pendulum.Core.Persistence.AppPaths.BaseDirectoryOverride = folder;
        try
        {
            var settings = new AppSettings();
            services.UseSettingsForTests(settings, saveOnChange: true);
            settings.ReminderFilterName = "tea";
            settings.TtsRate = 4;
            services.AddTrigger(new TriggerTimer { Name = "Saved before exit" });
            Assert.False(File.Exists(Pendulum.Core.Persistence.AppPaths.SettingsFile));
            Assert.False(File.Exists(Pendulum.Core.Persistence.AppPaths.TriggersFile));
            services.Shutdown();
            Assert.Equal("tea", new Pendulum.Core.Persistence.SettingsRepository().Load().ReminderFilterName);
            Assert.Equal("Saved before exit", Assert.Single(new Pendulum.Core.Persistence.TriggerRepository().Load()).Name);
        }
        finally
        {
            services.Shutdown();
            Pendulum.Core.Persistence.AppPaths.BaseDirectoryOverride = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    });

    private sealed class SaveClock : TimeProvider
    {
        private readonly List<Action> _ticks = new();
        private long _timestamp;
        public int Requests;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Fire()
        {
            _timestamp += TimeSpan.FromMilliseconds(400).Ticks;
            foreach (var tick in _ticks) tick();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _ticks.Add(() => callback(state));
            return new TestTimer(this);
        }
        private sealed class TestTimer(SaveClock clock) : ITimer
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
