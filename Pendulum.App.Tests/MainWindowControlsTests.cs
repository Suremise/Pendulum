using System.Diagnostics;
using System.IO;
using System.Windows;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// The main window's standard controls and where it opens, read from a window that is built but
/// never shown: nothing takes the foreground or focus, and there's no keyboard or mouse input.
[Collection(nameof(SettingsScreen))]
public sealed class MainWindowControlsTests : IDisposable
{
    // Saving the window's place writes the settings file: here, in a temp folder of the test's own.
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-window-tests-" + Guid.NewGuid().ToString("N"));

    public MainWindowControlsTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
    }

    public void Dispose()
    {
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static T WithWindow<T>(AppSettings settings, Func<MainWindow, T> read) =>
        UiThread.Invoke(() =>
        {
            AppServices.Instance.UseSettingsForTests(settings);
            var window = new MainWindow();
            try { return read(window); }
            finally
            {
                window.Close();
                Assert.Empty(Application.Current.Windows);
                Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
            }
        });

    [Fact]
    public void It_has_minimise_maximise_and_close_and_can_be_resized()
    {
        var (resize, minimize, maximize, close, canMaximize, minWidth) = WithWindow(new AppSettings(), w =>
            (w.ResizeMode, w.WindowTitleBar.ShowMinimize, w.WindowTitleBar.ShowMaximize, w.WindowTitleBar.ShowClose,
             w.WindowTitleBar.CanMaximize, w.MinWidth));

        // CanResize: maximise and restore, edge resizing, snap and Win+arrows, as Windows gives any
        // resizable window; the title bar's maximise and double-click follow CanMaximize. (UI
        // Automation's Window pattern reads Maximizable and Minimizable from ResizeMode too; it
        // needs a window handle, which these tests never create.)
        Assert.Equal(ResizeMode.CanResize, resize);
        Assert.True(minimize);
        Assert.True(maximize);
        Assert.True(close);
        Assert.True(canMaximize);
        Assert.True(minWidth > 0);
    }

    [Theory]
    [InlineData(WindowState.Maximized, true)]
    [InlineData(WindowState.Normal, false)]
    public void Minimised_then_closed_it_reopens_as_it_was_before_it_was_minimised(WindowState before, bool reopensMaximized)
    {
        var settings = new AppSettings();

        // Set to its state, then minimised, then closed: what it remembers.
        var remembered = WithWindow(settings, w =>
        {
            w.WindowState = before;
            w.WindowState = WindowState.Minimized;
            MainWindow.SavePlacement(new Rect(100, 80, 1000, 700), w.MaximizedToRemember);
            return w.MaximizedToRemember;
        });

        // Opened again from what was saved.
        var reopened = WithWindow(settings, w =>
        {
            w.RestorePlacement();
            return w.WindowState;
        });

        Assert.Equal(reopensMaximized, remembered);
        Assert.Equal(reopensMaximized, settings.MainWindowMaximized);
        Assert.Equal(reopensMaximized ? WindowState.Maximized : WindowState.Normal, reopened);
        Assert.Equal(1000, settings.MainWindowWidth);
    }

    [Fact]
    public void Maximised_then_restored_then_minimised_it_reopens_normal()
    {
        var remembered = WithWindow(new AppSettings(), w =>
        {
            w.WindowState = WindowState.Maximized;
            w.WindowState = WindowState.Normal;
            w.WindowState = WindowState.Minimized;
            return w.MaximizedToRemember;
        });

        Assert.False(remembered);
    }

    [Fact]
    public void With_nothing_saved_it_opens_at_its_default_size_centred()
    {
        var (location, width, height) = WithWindow(new AppSettings(), w =>
        {
            w.RestorePlacement();
            return (w.WindowStartupLocation, w.Width, w.Height);
        });

        Assert.Equal(WindowStartupLocation.CenterScreen, location);
        Assert.Equal(1200, width);
        Assert.Equal(800, height);
    }

    [Fact]
    public void A_place_off_every_screen_comes_back_onto_one_and_maximised_stays_maximised()
    {
        var settings = new AppSettings
        {
            MainWindowLeft = -50000,
            MainWindowTop = -50000,
            MainWindowWidth = 1000,
            MainWindowHeight = 700,
            MainWindowMaximized = true,
        };

        var (left, top, width, height, state) = WithWindow(settings, w =>
        {
            w.RestorePlacement();
            return (w.Left, w.Top, w.Width, w.Height, w.WindowState);
        });

        Assert.Equal(1000, width);
        Assert.Equal(700, height);
        Assert.InRange(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120);
        Assert.InRange(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 32);
        Assert.Equal(WindowState.Maximized, state);
    }
}
