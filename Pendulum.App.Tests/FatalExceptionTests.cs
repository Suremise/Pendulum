using System.IO;
using System.Windows.Threading;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class FatalExceptionTests
{
    [Fact]
    public void An_owner_thread_failure_flushes_both_pending_files_even_if_logging_fails() => UiThread.Invoke(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "pendulum-fatal-tests-" + Guid.NewGuid().ToString("N"));
        var previous = AppPaths.BaseDirectoryOverride;
        AppPaths.BaseDirectoryOverride = folder;
        var services = new AppServices(timeProvider: new FrozenClock());
        try
        {
            services.UseSettingsForTests(new AppSettings(), saveOnChange: true);
            services.Settings.TtsVolume = 42;
            services.AddTrigger(new TriggerTimer { Name = "Saved on failure" });
            Assert.False(File.Exists(AppPaths.SettingsFile));
            Assert.False(File.Exists(AppPaths.TriggersFile));

            FatalExceptionHandler.Handle(new InvalidOperationException("fatal"), Dispatcher.CurrentDispatcher,
                services.FlushPendingSaves, (_, _) => throw new IOException("log unavailable"));

            Assert.True(File.Exists(AppPaths.SettingsFile));
            Assert.True(File.Exists(AppPaths.TriggersFile));
            Assert.Equal(42, new SettingsRepository().LoadWithOutcome().Value!.TtsVolume);
            Assert.Equal("Saved on failure", Assert.Single(new TriggerRepository().LoadWithOutcome().Value!).Name);
        }
        finally
        {
            services.Shutdown();
            AppPaths.BaseDirectoryOverride = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void A_background_failure_neither_snapshots_nor_waits_for_a_blocked_owner() => UiThread.Invoke(() =>
    {
        var owner = Dispatcher.CurrentDispatcher;
        var writes = 0;
        var flushes = 0;
        var logs = 0;
        var services = new AppServices((_, _) => writes++, new FrozenClock());
        try
        {
            services.UseSettingsForTests(new AppSettings(), saveOnChange: true);
            services.Settings.TtsVolume = 42;
            services.AddTrigger(new TriggerTimer { Name = "Still pending" });
            // Hold the owner here until the worker returns: dispatcher Invoke would deadlock.
            Task.Run(() => FatalExceptionHandler.Handle(new InvalidOperationException("background fatal"), owner,
                () => { flushes++; services.FlushPendingSaves(); }, (_, _) => logs++))
                .WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

            Assert.Equal(0, flushes);
            Assert.Equal(0, writes);
            Assert.Equal(1, logs);
            services.FlushPendingSaves();
            Assert.Equal(2, writes); // Pending data was left for its owner, never read by the worker.
        }
        finally { services.Shutdown(); }
    });

    [Fact]
    public void Flush_and_log_failures_cannot_escape_the_fatal_handler() => UiThread.Invoke(() =>
    {
        var fatal = new InvalidOperationException("fatal");
        var save = new IOException("save failed");
        var errors = new List<Exception>();
        FatalExceptionHandler.Handle(fatal, Dispatcher.CurrentDispatcher, () => throw save,
            (_, error) => { errors.Add(error); throw new IOException("log failed"); });
        Assert.Equal([fatal, save], errors);
    });

    private sealed class FrozenClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
