using Pendulum.Core.Startup;

namespace Pendulum.Core.Tests;

/// The "launch on Windows startup" entry against a store of the test's own: no test reads or
/// writes the real registry.
public class StartupEntryTests
{
    private const string Installed = @"C:\Users\someone\AppData\Local\Programs\Pendulum\Pendulum.exe";
    private const string DevBuild = @"D:\src\Pendulum\Pendulum.App\bin\Debug\net10.0-windows\win-x64\Pendulum.exe";

    private sealed class FakeStore : IStartupValueStore
    {
        public string? Value { get; set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }

        public string? Read() => Value;
        public void Write(string commandLine) { Value = commandLine; Writes++; }
        public void Delete() { Value = null; Deletes++; }
    }

    private static string Quoted(string path) => $"\"{path}\"";

    // ---- First launch ----

    [Fact]
    public void A_fresh_install_registers_itself()
    {
        var store = new FakeStore();

        var registered = new StartupEntry(store, Installed).RegisterOnFirstLaunch(isInstalledCopy: true);

        Assert.True(registered);
        Assert.Equal(Quoted(Installed), store.Value);
    }

    [Fact]
    public void A_second_copys_first_launch_leaves_the_installed_copys_entry_alone()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        var registered = new StartupEntry(store, DevBuild).RegisterOnFirstLaunch(isInstalledCopy: false);

        Assert.False(registered);
        Assert.Equal(Quoted(Installed), store.Value);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void A_second_install_elsewhere_leaves_the_first_ones_entry_alone()
    {
        var other = @"E:\Apps\Pendulum\Pendulum.exe";
        var store = new FakeStore { Value = Quoted(other) };

        var registered = new StartupEntry(store, Installed).RegisterOnFirstLaunch(isInstalledCopy: true);

        Assert.False(registered);
        Assert.Equal(Quoted(other), store.Value);
    }

    [Fact]
    public void A_copy_that_isnt_installed_never_registers_itself_even_with_no_entry()
    {
        var store = new FakeStore();

        var registered = new StartupEntry(store, DevBuild).RegisterOnFirstLaunch(isInstalledCopy: false);

        Assert.False(registered);
        Assert.Null(store.Value);
    }

    [Fact]
    public void A_reinstall_with_its_data_gone_keeps_its_own_entry()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        Assert.True(new StartupEntry(store, Installed).RegisterOnFirstLaunch(isInstalledCopy: true));
        Assert.Equal(Quoted(Installed), store.Value);
    }

    [Fact]
    public void An_entry_written_without_quotes_or_in_other_case_still_counts_as_this_copys()
    {
        var store = new FakeStore { Value = Installed.ToUpperInvariant() };

        Assert.True(new StartupEntry(store, Installed).RegisterOnFirstLaunch(isInstalledCopy: true));
        Assert.Equal(Quoted(Installed), store.Value);
    }

    // ---- The user's toggle ----

    [Fact]
    public void Turning_it_on_makes_windows_start_this_copy()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        new StartupEntry(store, DevBuild).SetEnabled(true);

        Assert.Equal(Quoted(DevBuild), store.Value);
    }

    [Fact]
    public void Turning_it_off_removes_this_copys_entry()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        new StartupEntry(store, Installed).SetEnabled(false);

        Assert.Null(store.Value);
    }

    [Fact]
    public void Turning_it_off_in_another_copy_leaves_the_installed_copys_entry_alone()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        new StartupEntry(store, DevBuild).SetEnabled(false);

        Assert.Equal(Quoted(Installed), store.Value);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void Turning_it_off_with_no_entry_does_nothing()
    {
        var store = new FakeStore();

        new StartupEntry(store, Installed).SetEnabled(false);

        Assert.Null(store.Value);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void Enabled_means_any_copy_and_points_here_means_this_one()
    {
        var store = new FakeStore { Value = Quoted(Installed) };

        Assert.True(new StartupEntry(store, DevBuild).IsEnabled);
        Assert.False(new StartupEntry(store, DevBuild).PointsHere);
        Assert.True(new StartupEntry(store, Installed).PointsHere);
        Assert.False(new StartupEntry(new FakeStore(), Installed).IsEnabled);
    }

    // ---- Reading a Run command line ----

    [Theory]
    [InlineData("\"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe\"", true)]
    [InlineData("C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe", true)]
    [InlineData("\"c:\\users\\SOMEONE\\appdata\\local\\programs\\pendulum\\pendulum.exe\"", true)]
    [InlineData("\"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe\" --minimized", true)]
    [InlineData("C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe --minimized", true)]
    [InlineData("  \"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe\"  ", true)]
    [InlineData("\"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\..\\Pendulum\\Pendulum.exe\"", true)]
    [InlineData("\"D:\\src\\Pendulum\\Pendulum.App\\bin\\Debug\\net10.0-windows\\win-x64\\Pendulum.exe\"", false)]
    [InlineData("\"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\\Pendulum.exe.old\"", false)]
    [InlineData("\"C:\\Users\\someone\\AppData\\Local\\Programs\\Pendulum\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("", false)]
    [InlineData("\"C:\\bad<path>|\\Pendulum.exe\"", false)]
    public void Recognises_a_command_line_that_starts_the_installed_copy(string commandLine, bool expected)
    {
        Assert.Equal(expected, StartupEntry.PointsAt(commandLine, Installed));
    }

    [Fact]
    public void An_unquoted_path_with_spaces_reads_whole()
    {
        Assert.True(StartupEntry.PointsAt(@"C:\Program Files\Pendulum\Pendulum.exe", @"C:\Program Files\Pendulum\Pendulum.exe"));
        Assert.False(StartupEntry.PointsAt(@"C:\Program Files\Pendulum\Pendulum.exe", @"C:\Program Files\Other\Pendulum.exe"));
    }
}
