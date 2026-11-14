using System.Threading;
using Pendulum.App.Services;

namespace Pendulum.App.Tests;

/// Named objects are unique to each test. No App, real Pendulum names, window or focus.
public sealed class SingleInstanceTests
{
    [Fact]
    public void A_second_copy_wakes_the_primary_and_the_listener_ends_on_dispose()
    {
        var name = "pendulum-test-" + Guid.NewGuid().ToString("N");
        using var woke = new ManualResetEventSlim();
        using var primary = new SingleInstance(name, name + "-wake", woke.Set);
        Assert.True(primary.IsPrimary);
        Assert.True(primary.IsListening);
        using (var second = new SingleInstance(name, name + "-wake", () => { }))
        {
            Assert.False(second.IsPrimary);
            Assert.False(second.IsListening);
            Assert.True(woke.Wait(TimeSpan.FromSeconds(30)), "The primary was not woken");
        }
        primary.Dispose();
        Assert.False(primary.IsListening);
        Assert.Throws<WaitHandleCannotBeOpenedException>(() =>
        {
            using var unexpected = EventWaitHandle.OpenExisting(name + "-wake");
        });
    }

    [Fact]
    public void Disposing_releases_the_names_for_a_new_primary_copy()
    {
        var name = "pendulum-test-" + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name, name + "-wake", () => { }))
            Assert.True(first.IsPrimary);
        using var replacement = new SingleInstance(name, name + "-wake", () => { });
        Assert.True(replacement.IsPrimary);
    }

    [Fact]
    public void The_primary_can_handle_more_than_one_wake()
    {
        var name = "pendulum-test-" + Guid.NewGuid().ToString("N");
        using var woke = new ManualResetEventSlim();
        var count = 0;
        using var primary = new SingleInstance(name, name + "-wake", () => { Interlocked.Increment(ref count); woke.Set(); });
        for (var i = 1; i <= 2; i++)
        {
            woke.Reset();
            using var second = new SingleInstance(name, name + "-wake", () => { });
            Assert.True(woke.Wait(TimeSpan.FromSeconds(30)));
            Assert.Equal(i, Volatile.Read(ref count));
        }
    }
}
