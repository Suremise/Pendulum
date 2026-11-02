using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// The main window's menu bar and the Settings window, read through UI Automation (the providers a
/// screen reader uses) on windows built but never shown: nothing takes the foreground or focus,
/// and menu items are invoked through their Invoke pattern, not with keyboard or mouse input.
[Collection(nameof(SettingsScreen))]
public sealed class MainMenuTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-menu-tests-" + Guid.NewGuid().ToString("N"));

    public MainMenuTests()
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

    private static void WithWindow(Action<MainWindow> read) =>
        UiThread.Invoke(() =>
        {
            AppServices.Instance.UseSettingsForTests(new AppSettings());
            var window = new MainWindow();
            try { read(window); }
            finally
            {
                window.Close();
            }
            Assert.Equal([], Application.Current.Windows.Cast<Window>().Select(x => x.GetType().Name));
            Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
        });

    private static string Name(UIElement e) => UIElementAutomationPeer.CreatePeerForElement(e).GetName();

    private static MenuItem[] Items(MenuItem menu) => menu.Items.OfType<MenuItem>().ToArray();

    /// A menu item's Invoke pattern, as a screen reader or UI Automation client uses it; WPF
    /// carries out the click a moment later, so the UI thread's queue is let run first.
    private static void Invoke(MenuItem item)
    {
        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.Invoke)).Invoke();
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void The_menu_bar_has_File_View_Tools_and_Help_with_their_items_and_shortcuts()
    {
        WithWindow(w =>
        {
            var menus = w.MainMenu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal([Strings.Menu_File, Strings.Menu_View, Strings.Menu_Tools, Strings.Menu_Help], menus.Select(m => (string)m.Header));
            // Each has an access key (Alt+F, Alt+V, Alt+T, Alt+H): the letter after the underscore.
            Assert.All(menus, m => Assert.Contains('_', (string)m.Header));

            Assert.Equal([w.NewReminderMenuItem, w.ImportMenuItem, w.ExportMenuItem, w.ExitMenuItem], Items(menus[0]));
            Assert.Equal([w.ViewRemindersMenuItem, w.ViewCalendarMenuItem], Items(menus[1]));
            Assert.Equal([w.SettingsMenuItem], Items(menus[2]));
            Assert.Equal([w.CheckForUpdatesMenuItem, w.AboutMenuItem], Items(menus[3]));

            // Shown beside their items, and bound on the window.
            Assert.Equal("Ctrl+N", w.NewReminderMenuItem.InputGestureText);
            Assert.Equal("Ctrl+,", w.SettingsMenuItem.InputGestureText);
            var gestures = w.InputBindings.OfType<KeyBinding>().Select(b => (b.Key, b.Modifiers)).ToList();
            Assert.Contains((Key.N, ModifierKeys.Control), gestures);
            Assert.Contains((Key.OemComma, ModifierKeys.Control), gestures);

            // Screen readers read each item by its words, without the access key's underscore.
            Assert.Equal("About Pendulum", Name(w.AboutMenuItem));
            Assert.Equal("Settings…", Name(w.SettingsMenuItem));
        });
    }

    [Fact]
    public void View_switches_between_the_two_tabs_and_Settings_and_About_are_no_longer_on_the_window()
    {
        WithWindow(w =>
        {
            Assert.Equal(2, w.MainTabControl.Items.Count);

            Invoke(w.ViewCalendarMenuItem);
            Assert.Same(w.CalendarTab, w.MainTabControl.SelectedItem);
            Invoke(w.ViewRemindersMenuItem);
            Assert.Same(w.FixedTriggersTab, w.MainTabControl.SelectedItem);

            // The status bar holds the next reminder (and an update's link) only: no About button.
            var statusBar = (Panel)w.StatusText.Parent;
            Assert.Empty(statusBar.Children.OfType<Button>());
            Assert.DoesNotContain(LogicalTreeHelper.GetChildren(w.MainTabControl).OfType<TabItem>(), t => t.Content is SettingsPanel);
        });
    }

    [Fact]
    public void Settings_opens_in_its_own_window_over_the_main_one_with_every_section()
    {
        WithWindow(w =>
        {
            var settings = w.SettingsWindow;   // made, never shown
            try
            {
                Assert.Same(settings, w.SettingsWindow);   // one, kept
                Assert.Equal(WindowStartupLocation.CenterOwner, settings.WindowStartupLocation);
                Assert.False(settings.ShowInTaskbar);
                Assert.Equal(Strings.Main_Settings, settings.Title);

                var sections = ((ListBox)settings.Panel.FindName("SectionList")).Items.Count;
                Assert.Equal(5, sections);
            }
            finally
            {
                settings.Close();
            }
        });
    }
}
