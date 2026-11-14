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
    // Deliberate examples, independent of ReminderStore's property-discovery implementation.
    // The JSON contract check below forces a new saved property to get a behavioral case too.
    private static readonly Dictionary<string, object> ReminderChanges = new()
    {
        [nameof(TriggerTimer.Id)] = Guid.Parse("3a678c60-409d-4efb-a154-e921bd99fb70"),
        [nameof(TriggerTimer.Name)] = "Changed",
        [nameof(TriggerTimer.TriggerAt)] = new DateTime(2030, 4, 5, 12, 0, 0),
        [nameof(TriggerTimer.DueUtc)] = new DateTime(2030, 4, 5, 11, 0, 0, DateTimeKind.Utc),
        [nameof(TriggerTimer.SnoozedOccurrence)] = new DateTime(2030, 4, 5, 10, 0, 0),
        [nameof(TriggerTimer.Enabled)] = false,
        [nameof(TriggerTimer.Mode)] = AlertMode.SoundAndSpeech,
        [nameof(TriggerTimer.SoundFileName)] = "bell.wav",
        [nameof(TriggerTimer.Phrase)] = "Changed phrase",
        [nameof(TriggerTimer.Volume)] = 30,
        [nameof(TriggerTimer.HasFired)] = true,
        [nameof(TriggerTimer.RecurrenceAnchor)] = new DateTime(2030, 4, 4, 12, 0, 0),
        [nameof(TriggerTimer.Recurrence)] = new RecurrenceRule(),
        [nameof(TriggerTimer.Color)] = "teal"
    };

    private static readonly Dictionary<string, object> RuleChanges = new()
    {
        [nameof(RecurrenceRule.Frequency)] = RecurrenceFrequency.Weekly,
        [nameof(RecurrenceRule.Interval)] = 2,
        [nameof(RecurrenceRule.DaysOfWeek)] = new List<DayOfWeek> { DayOfWeek.Friday },
        [nameof(RecurrenceRule.DayOfMonth)] = 15,
        [nameof(RecurrenceRule.Month)] = 4,
        [nameof(RecurrenceRule.MonthDayRule)] = MonthDayRule.RelativeDay,
        [nameof(RecurrenceRule.WeekOfMonth)] = WeekOfMonth.Last,
        [nameof(RecurrenceRule.RelativeDay)] = RelativeDay.Weekday,
        [nameof(RecurrenceRule.EndType)] = RecurrenceEndType.AfterOccurrences,
        [nameof(RecurrenceRule.OccurrenceCount)] = 20,
        [nameof(RecurrenceRule.EndDate)] = new DateTime(2031, 4, 5),
        [nameof(RecurrenceRule.OccurrencesSoFar)] = 1
    };

    public static IEnumerable<object[]> PersistedChanges =>
        ReminderChanges.Keys.Select(name => new object[] { false, name })
            .Concat(RuleChanges.Keys.Select(name => new object[] { true, name }));

    [Fact]
    public void Every_property_written_to_JSON_has_a_save_scheduling_case()
    {
        Assert.Equal(JsonSerializer.SerializeToElement(new TriggerTimer()).EnumerateObject()
            .Select(p => p.Name).Order(), ReminderChanges.Keys.Order());
        Assert.Equal(JsonSerializer.SerializeToElement(new RecurrenceRule()).EnumerateObject()
            .Select(p => p.Name).Order(), RuleChanges.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(PersistedChanges))]
    public void Each_persisted_change_schedules_exactly_once_and_saves_its_value(bool nested, string property)
        => UiThread.Invoke(() =>
        {
            var clock = new SaveClock();
            var snapshots = new List<JsonElement>();
            var services = new AppServices((_, snapshot) => snapshots.Add(snapshot), clock);
            try
            {
                var trigger = new TriggerTimer
                {
                    Id = Guid.Empty,
                    Color = "amber",
                    TriggerAt = new DateTime(2029, 1, 1),
                    RecurrenceAnchor = new DateTime(2029, 1, 1),
                    Recurrence = new() { Frequency = RecurrenceFrequency.None }
                };
                services.AddTrigger(trigger);
                services.FlushPendingSaves();
                snapshots.Clear();
                clock.Requests = 0;
                object target = nested ? trigger.Recurrence : trigger;
                var before = JsonSerializer.SerializeToElement(target).GetProperty(property);
                var notifications = new List<string?>();
                trigger.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

                var value = (nested ? RuleChanges : ReminderChanges)[property];
                target.GetType().GetProperty(property)!.SetValue(target, value);

                var expected = JsonSerializer.SerializeToElement(target).GetProperty(property);
                Assert.False(JsonElement.DeepEquals(before, expected), $"{property} must actually change");
                Assert.Equal(1, clock.Requests);
                Assert.Empty(snapshots);
                services.FlushPendingSaves();
                var saved = Assert.Single(snapshots)[0];
                if (nested) saved = saved.GetProperty(nameof(TriggerTimer.Recurrence));
                Assert.True(JsonElement.DeepEquals(expected, saved.GetProperty(property)));

                // These notifications really occur, but must not request additional saves.
                if (!nested && property is nameof(TriggerTimer.Id) or nameof(TriggerTimer.Color))
                    Assert.Contains(nameof(TriggerTimer.DisplayColor), notifications);
                if (!nested && property is nameof(TriggerTimer.Enabled) or nameof(TriggerTimer.HasFired))
                    Assert.Contains(nameof(TriggerTimer.StatusSortOrder), notifications);
                if ((!nested && property == nameof(TriggerTimer.Recurrence)) ||
                    (nested && property == nameof(RecurrenceRule.Frequency)))
                    Assert.Contains(nameof(TriggerTimer.IsScheduled), notifications);
            }
            finally { services.Shutdown(); }
        });

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
                    services.Update(trigger, () => TriggerReconciler.Snooze(trigger, at, 5, TimeZoneInfo.Utc));
                }
                else
                {
                    // The properties applied by the editor's Save, in one owned update.
                    services.Update(trigger, () =>
                    {
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
                    });
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
            Assert.Equal("tea", new Pendulum.Core.Persistence.SettingsRepository().LoadWithOutcome().Value!.ReminderFilterName);
            Assert.Equal("Saved before exit", Assert.Single(new Pendulum.Core.Persistence.TriggerRepository().LoadWithOutcome().Value!).Name);
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
