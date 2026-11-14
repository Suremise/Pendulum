using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

public sealed class DebouncedSaveTests
{
    [Fact]
    public void Changes_are_coalesced_and_written_in_background()
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        var owner = Environment.CurrentManagedThreadId;
        var value = 0;
        var captures = 0;
        var writes = 0;
        using var finished = new ManualResetEventSlim();
        using var save = new DebouncedSave<int>(() => { captures++; return value; }, snapshot =>
        {
            Assert.NotEqual(owner, Environment.CurrentManagedThreadId);
            Assert.Equal(3, snapshot);
            Interlocked.Increment(ref writes);
            finished.Set();
        }, posted.Enqueue, _ => { }, clock);

        for (value = 1; value <= 3; value++) save.MarkDirty();
        value = 3;
        Assert.Equal(0, captures);
        clock.Fire();
        Assert.Single(posted)();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
        save.Flush();
        Assert.Equal(1, writes);
        Assert.Equal(1, captures);
    }

    [Fact]
    public void Closing_flushes_latest_change_and_a_posted_callback_cannot_save_again()
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        var writes = new List<int>();
        var value = 1;
        using var save = new DebouncedSave<int>(() => value, writes.Add, posted.Enqueue, _ => { }, clock);
        save.MarkDirty();
        clock.Fire();
        value = 2;
        save.MarkDirty();
        save.Flush();
        Assert.Single(posted)();
        Assert.Equal(new[] { 2 }, writes);
    }

    [Fact]
    public void A_change_during_a_write_is_flushed_after_it_without_overlap()
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writes = new List<int>();
        var value = 1;
        using var save = new DebouncedSave<int>(() => value, snapshot =>
        {
            if (snapshot == 1)
            {
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            writes.Add(snapshot);
        }, posted.Enqueue, _ => { }, clock);
        try
        {
            save.MarkDirty();
            clock.Fire();
            posted.Dequeue()();
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            value = 2;
            save.MarkDirty();
        }
        finally { release.Set(); }
        save.Flush();
        Assert.Equal(new[] { 1, 2 }, writes);
    }

    [Fact]
    public void Failed_background_write_is_reported_and_retried_on_flush()
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        using var failed = new ManualResetEventSlim();
        var attempts = 0;
        using var save = new DebouncedSave<int>(() => 42, snapshot =>
        {
            Assert.Equal(42, snapshot);
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException();
        }, posted.Enqueue, _ => failed.Set(), clock);
        save.MarkDirty();
        clock.Fire();
        posted.Dequeue()();
        Assert.True(failed.Wait(TimeSpan.FromSeconds(10)));
        save.Flush();
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void A_timer_callback_from_before_the_latest_change_waits_for_its_quiet_period()
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        var writes = 0;
        using var save = new DebouncedSave<int>(() => 1, _ => writes++, posted.Enqueue, _ => { }, clock);
        save.MarkDirty();
        clock.Fire(advance: false);
        posted.Dequeue()();
        Assert.Equal(0, writes);
        save.Flush();
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_final_snapshot_or_write_still_disposes_the_timer_and_rejects_later_changes(bool snapshotFails)
    {
        var clock = new SaveClock();
        var posted = new Queue<Action>();
        var failing = true;
        var captures = 0;
        var save = new DebouncedSave<int>(() =>
        {
            captures++;
            if (failing && snapshotFails) throw new IOException("snapshot failed");
            return 42;
        }, _ => { if (failing) throw new IOException("write failed"); }, posted.Enqueue, _ => { }, clock);
        try
        {
            save.MarkDirty();
            clock.Fire();
            Assert.Throws<IOException>(save.Dispose);
            Assert.Equal(1, clock.Disposals);
            var captured = captures;
            Assert.Single(posted)();
            Assert.Equal(captured, captures);
            Assert.Throws<ObjectDisposedException>(save.MarkDirty);
            save.Dispose(); // A failed final save must not make cleanup run twice.
            Assert.Equal(1, clock.Disposals);
        }
        finally
        {
            failing = false;
            save.Dispose();
        }
    }

    // A manually fired timer: tests advance the quiet period without sleeping.
    private sealed class SaveClock : TimeProvider
    {
        private Action? _tick;
        private long _timestamp;
        public int Disposals;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Fire(bool advance = true)
        {
            if (advance) _timestamp += TimeSpan.FromMilliseconds(400).Ticks;
            _tick!();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _tick = () => callback(state);
            return new TestTimer(this);
        }
        private sealed class TestTimer(SaveClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => clock.Disposals++;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
