using System.Diagnostics;
using Pendulum.Core.Speech;

namespace Pendulum.Core.Tests;

/// piper.exe as Pendulum runs it, with batch files standing in for piper. They write no audio, so
/// nothing is played; they run hidden (no window), and nothing is left running afterwards.
public sealed class PiperProcessTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string FakePiper(string body)
    {
        var path = _temp.File("piper.cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.Replace("\n", "\r\n") + "\r\n");
        return path;
    }

    private PiperSpeechService Piper(string exe, TimeSpan? timeout = null) =>
        new(exe, _temp.File("voice.onnx"), 1f, 1f) { Timeout = timeout ?? TimeSpan.FromSeconds(60) };

    [Fact]
    public async Task A_piper_that_writes_a_lot_of_errors_still_finishes()
    {
        // About 300 KB on stderr, far more than the pipe holds: unread, piper would wait forever.
        var exe = FakePiper("for /L %%i in (1,1,4000) do echo warning: 0123456789012345678901234567890123456789012345678901234567890123 1>&2\nexit /b 0");
        using var piper = Piper(exe);

        await piper.SpeakAndWaitAsync("Dentist", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_piper_that_fails_says_why()
    {
        var exe = FakePiper("echo [error] Unable to load the voice model 1>&2\nexit /b 3");
        using var piper = Piper(exe);

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => piper.SpeakAndWaitAsync("Dentist", CancellationToken.None));
        Assert.Contains("3", e.Message, StringComparison.Ordinal);
        Assert.Contains("Unable to load the voice model", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_path_with_spaces_reaches_piper_whole()
    {
        var folder = _temp.File("my voices");
        Directory.CreateDirectory(folder);
        var model = Path.Combine(folder, "en GB voice.onnx");
        var got = _temp.File("model.txt");
        var exe = FakePiper($"set /p phrase=\n>\"{got}\" echo %~4\nexit /b 0");
        using var piper = new PiperSpeechService(exe, model, 0.8f, 1f) { Timeout = TimeSpan.FromSeconds(60) };

        await piper.SpeakAndWaitAsync("Dentist", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(model, File.ReadAllText(got).TrimEnd());
    }

    [Fact]
    public async Task A_missing_piper_says_it_couldnt_start()
    {
        using var piper = Piper(_temp.File("piper.exe"));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => piper.SpeakAndWaitAsync("Dentist", CancellationToken.None));
        Assert.Contains(_temp.File("piper.exe"), e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_piper_that_hangs_is_stopped_after_the_timeout()
    {
        var pidFile = _temp.File("child.pid");
        var exe = FakePiper(HangingChild(pidFile));
        using var piper = Piper(exe, TimeSpan.FromSeconds(3));

        await Assert.ThrowsAsync<TimeoutException>(() => piper.SpeakAndWaitAsync("Dentist", CancellationToken.None));

        // The child it started, if it got that far, was stopped with it.
        if (File.Exists(pidFile))
            await WaitForExit(ReadPid(pidFile));
    }

    [Fact]
    public async Task Dismissing_while_piper_runs_stops_it_and_what_it_started()
    {
        var pidFile = _temp.File("child.pid");
        var exe = FakePiper(HangingChild(pidFile));
        using var piper = Piper(exe);
        using var dismiss = new CancellationTokenSource();

        var speaking = piper.SpeakAndWaitAsync("Dentist", dismiss.Token);
        var clock = Stopwatch.StartNew();
        while (ReadPidOrZero(pidFile) == 0)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "the stand-in never started its child");
            Assert.False(speaking.IsCompleted, "piper ended before it was dismissed");
            await Task.Delay(50);
        }

        dismiss.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaking);
        await WaitForExit(ReadPid(pidFile));
    }

    [Fact]
    public async Task A_piper_that_exits_leaving_a_child_on_its_pipes_doesnt_keep_the_alert_waiting()
    {
        // piper exits at once; the child it started keeps its pipes (stdout and stderr) open for
        // two minutes. The phrase ends when piper does, well inside the time limit, and the child
        // is ended with it.
        var pidFile = _temp.File("child.pid");
        var exe = FakePiper($"start \"\" /b {HangingChild(pidFile)}\n{WaitFor(pidFile)}\nexit /b 0");
        using var piper = Piper(exe, TimeSpan.FromSeconds(90));
        var clock = Stopwatch.StartNew();

        await piper.SpeakAndWaitAsync("Dentist", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60));
        await WaitForExit(ReadPid(pidFile));
    }

    [Fact]
    public async Task A_piper_that_fails_and_leaves_a_child_still_says_why()
    {
        var pidFile = _temp.File("child.pid");
        var exe = FakePiper($"start \"\" /b {HangingChild(pidFile)}\n{WaitFor(pidFile)}\necho [error] Out of memory 1>&2\nexit /b 4");
        using var piper = Piper(exe, TimeSpan.FromSeconds(90));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => piper.SpeakAndWaitAsync("Dentist", CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Contains("4", e.Message, StringComparison.Ordinal);
        Assert.Contains("Out of memory", e.Message, StringComparison.Ordinal);
        await WaitForExit(ReadPid(pidFile));
    }

    /// Batch lines that wait until the child has written its id, so it's surely running when the
    /// stand-in exits.
    private static string WaitFor(string pidFile) => $":wait\nif not exist \"{pidFile}\" goto wait";

    /// A child process that writes its id, then waits two minutes: it runs hidden, as piper does.
    private static string HangingChild(string pidFile) =>
        $"powershell.exe -NoProfile -NonInteractive -Command \"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 120\"";

    private static int ReadPidOrZero(string pidFile)
    {
        try
        {
            return File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) ? pid : 0;
        }
        catch (IOException)
        {
            return 0;  // still being written
        }
    }

    private static int ReadPid(string pidFile)
    {
        var clock = Stopwatch.StartNew();
        int pid;
        while ((pid = ReadPidOrZero(pidFile)) == 0)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30));
            Thread.Sleep(20);
        }

        return pid;
    }

    private static async Task WaitForExit(int pid)
    {
        Process child;
        try
        {
            child = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return;  // already gone
        }

        using (child)
        {
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                child.Kill(entireProcessTree: true);  // tidy up, then fail
                Assert.Fail($"piper's child {pid} was left running");
            }
        }
    }
}
