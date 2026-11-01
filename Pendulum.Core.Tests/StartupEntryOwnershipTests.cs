using Pendulum.Core.Startup;

namespace Pendulum.Core.Tests;

/// Entries that only look like this copy's belong to another: no copy replaces or removes them.
/// A store of the test's own, never the real registry.
public class StartupEntryOwnershipTests
{
    private const string Exe = @"C:\Apps\Pendulum.exe";

    private sealed class FakeStore : IStartupValueStore
    {
        public string? Value { get; set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }

        public string? Read() => Value;
        public void Write(string commandLine) { Value = commandLine; Writes++; }
        public void Delete() { Value = null; Deletes++; }
    }

    public static TheoryData<string> LookalikeEntries => new()
    {
        @"C:\Apps\Pendulum.exe.backup\Pendulum.exe",
        @"C:\Apps\Pendulum.exe.backup\Pendulum.exe --minimized",
        @"C:\Apps\Pendulum.exe.old",
        @"C:\Apps\Pendulum.exeX",
        @"""C:\Apps\Pendulum.exe.backup\Pendulum.exe""",
        @"""C:\Apps\Pendulum.exe.old""",
        @"""C:\Apps\Pendulum.exe",
        @"""C:\Apps\Pendulum.exe"".old",
        @"""C:\Apps\Pendulum.exe""X --minimized",
    };

    [Theory]
    [MemberData(nameof(LookalikeEntries))]
    public void Isnt_read_as_this_copys(string entry)
    {
        Assert.False(StartupEntry.PointsAt(entry, Exe));
    }

    [Theory]
    [MemberData(nameof(LookalikeEntries))]
    public void A_first_launch_leaves_it_alone(string entry)
    {
        var store = new FakeStore { Value = entry };

        var registered = new StartupEntry(store, Exe).RegisterOnFirstLaunch(isInstalledCopy: true);

        Assert.False(registered);
        Assert.Equal(entry, store.Value);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [MemberData(nameof(LookalikeEntries))]
    public void Turning_it_off_leaves_it_alone(string entry)
    {
        var store = new FakeStore { Value = entry };

        new StartupEntry(store, Exe).SetEnabled(false);

        Assert.Equal(entry, store.Value);
        Assert.Equal(0, store.Deletes);
    }

    public static TheoryData<string, string> OwnEntries => new()
    {
        { @"C:\Apps\Pendulum.exe", Exe },
        { @"C:\Apps\Pendulum.exe --minimized", Exe },
        { "C:\\Apps\\Pendulum.exe\t--minimized", Exe },
        { @"""C:\Apps\Pendulum.exe"" --minimized", Exe },
        { @"C:\Program Files\Pendulum\Pendulum.exe", @"C:\Program Files\Pendulum\Pendulum.exe" },
        { @"C:\Program Files\Pendulum\Pendulum.exe --minimized", @"C:\Program Files\Pendulum\Pendulum.exe" },
        { @"""C:\Program Files\Pendulum\Pendulum.exe"" --minimized", @"C:\Program Files\Pendulum\Pendulum.exe" },
    };

    [Theory]
    [MemberData(nameof(OwnEntries))]
    public void This_copys_own_entry_with_spaces_or_arguments_is_still_its_own(string entry, string exe)
    {
        Assert.True(StartupEntry.PointsAt(entry, exe));

        var store = new FakeStore { Value = entry };
        Assert.True(new StartupEntry(store, exe).RegisterOnFirstLaunch(isInstalledCopy: true));
        Assert.Equal($"\"{exe}\"", store.Value);

        store = new FakeStore { Value = entry };
        new StartupEntry(store, exe).SetEnabled(false);
        Assert.Null(store.Value);
    }

    [Fact]
    public void An_unquoted_entry_for_another_folder_with_spaces_isnt_this_copys()
    {
        Assert.False(StartupEntry.PointsAt(@"C:\Program Files\Other\Pendulum.exe --minimized", @"C:\Program Files\Pendulum\Pendulum.exe"));
    }
}
