using System.IO;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class ShutdownFailureTests
{
    [Fact]
    public void Failed_saves_are_both_attempted_logged_and_disposed_without_stopping_shutdown() => UiThread.Invoke(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "pendulum-shutdown-tests-" + Guid.NewGuid().ToString("N"));
        var previous = AppPaths.BaseDirectoryOverride;
        AppPaths.BaseDirectoryOverride = folder;
        var clock = new Clock();
        var attempts = new List<string>();
        var failing = true;
        var services = new AppServices((path, _) =>
        {
            attempts.Add(Path.GetFileName(path));
            if (failing) throw new IOException("Cannot save " + Path.GetFileName(path));
        }, clock);
        try
        {
            services.UseSettingsForTests(new AppSettings(), saveOnChange: true);
            services.Settings.TtsVolume = 42;
            var trigger = new TriggerTimer { Name = "Pending" };
            services.AddTrigger(trigger);

            var error = Record.Exception(services.Shutdown);
            Assert.Null(error);
            Assert.Equal(new[] { "settings.json", "triggers.json" }, attempts);
            Assert.Equal(2, clock.Timers.Count);
            Assert.All(clock.Timers, timer => Assert.Equal(1, timer.Disposals));
            var log = File.ReadAllText(Path.Combine(AppPaths.DataDirectory, "crash.log"));
            Assert.Contains("Cannot save settings.json", log);
            Assert.Contains("Cannot save triggers.json", log);
            Assert.False(File.Exists(AppPaths.SettingsFile));
            Assert.False(File.Exists(AppPaths.TriggersFile));

            trigger.Name = "Changed after shutdown"; // Store subscriptions have been removed.
            clock.Fire();
            services.Shutdown();
            Assert.Equal(2, attempts.Count);
            Assert.All(clock.Timers, timer => Assert.Equal(1, timer.Disposals));
        }
        finally
        {
            failing = false;
            services.Shutdown();
            AppPaths.BaseDirectoryOverride = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void Cleanup_attempts_later_resources_even_if_disposal_and_logging_throw()
    {
        var attempted = new List<string>();
        var reported = new List<string>();
        ShutdownCleanup.Run((context, _) =>
        {
            reported.Add(context);
            throw new IOException("log unavailable");
        },
        ("save", () => { attempted.Add("save"); throw new IOException("disk full"); }
        ),
        ("tray", () => { attempted.Add("tray"); throw new InvalidOperationException("cleanup failed"); }
        ),
        ("instance", () => attempted.Add("instance")));
        Assert.Equal(new[] { "save", "tray", "instance" }, attempted);
        Assert.Equal(new[] { "save", "tray" }, reported);
    }

    private sealed class Clock : TimeProvider
    {
        public List<Timer> Timers { get; } = [];
        public void Fire()
        {
            foreach (var timer in Timers) timer.Tick();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(() => callback(state));
            Timers.Add(timer);
            return timer;
        }
        public sealed class Timer(Action tick) : ITimer
        {
            public int Disposals;
            public void Tick() => tick(); // Simulate an already-in-flight callback even after disposal.
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => Disposals++;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
