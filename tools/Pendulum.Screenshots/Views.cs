using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Pendulum.App;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using static Pendulum.Screenshots.Program;
using ThemeMode = Pendulum.Core.Models.ThemeMode;

namespace Pendulum.Screenshots;

/// Every view of the app as it is, in light and dark side by side (--views &lt;folder&gt;): for
/// checking a change to the look before and after, across the whole app. Drawn off screen like
/// the README's pictures, with the same invented reminders; nothing is shown or saved.
internal static class Views
{
    private static readonly (string File, string Title, Func<FrameworkElement> Build)[] Shots =
    [
        ("01-reminders.png", "Reminders", () => MainWindowShot(tab: 0)),
        ("02-calendar.png", "Calendar", () => MainWindowShot(tab: 1)),
        ("03-settings.png", "Settings", Settings),
        ("04-editor.png", "A reminder's editor (a reminder that's turned off)", Editor),
        ("05-repeat.png", "Repeat", Repeat),
        ("06-quick-reminder.png", "Quick Reminder, with a message", QuickReminder),
        ("07-alert.png", "An alert", Alert),
        ("08-about.png", "About", About),
        ("09-shortcut.png", "Choosing the Quick Reminder shortcut", Shortcut),
    ];

    /// Draws each theme in a process of its own (this tool again: some of WPF-UI's styles keep
    /// the colours of the theme they first loaded in), then puts light and dark side by side.
    public static void Render(string output)
    {
        var parts = Path.Combine(Path.GetTempPath(), "pendulum-views-" + Guid.NewGuid().ToString("N")); // not user-visible
        try
        {
            foreach (var theme in new[] { "light", "dark" }) // not user-visible: command-line values
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (var arg in new[] { "--views-theme", theme, Path.Combine(parts, theme) }) // not user-visible
                    start.ArgumentList.Add(arg);
                using var child = Process.Start(start)!;
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException($"Drawing the {theme} theme failed."); // not user-visible
            }

            foreach (var shot in Shots)
                Save(Mockups.SideBySide(shot.Title, Mockups.Load(Path.Combine(parts, "light", shot.File)), Mockups.Load(Path.Combine(parts, "dark", shot.File))),
                    Path.Combine(output, shot.File));
        }
        finally
        {
            try { Directory.Delete(parts, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// One theme's pictures, each on its own, into a folder.
    public static void RenderTheme(bool dark, string output)
    {
        Directory.CreateDirectory(output);
        Mockups.Dark = dark;
        AppServices.Instance.Settings.Theme = dark ? ThemeMode.Dark : ThemeMode.Light;
        AppThemeManager.Apply(dark ? ThemeMode.Dark : ThemeMode.Light);
        foreach (var shot in Shots)
            Save(Draw(shot.Build()), Path.Combine(output, shot.File));
    }

    private static FrameworkElement MainWindowShot(int tab)
    {
        var window = new MainWindow();
        var host = Detach(window, 1100, 720);
        Mockups.Inherit(host);
        window.MainTabControl.SelectedIndex = tab;
        Layout(host);
        // What the window does when it first shows: the list sizes its columns, the calendar
        // builds its grid.
        foreach (var panel in Descendants(host).OfType<TriggersPanel>())
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        foreach (var calendar in Descendants(host).OfType<CalendarPanel>())
            (calendar.DataContext as CalendarViewModel)?.RefreshForDisplay();
        Layout(host);
        return Mockups.Framed(host);
    }

    private static FrameworkElement Settings() => Window(new SettingsWindow());

    private static FrameworkElement Editor()
    {
        var at = DateTime.Today.AddDays(3).AddHours(10).AddMinutes(15);
        var reminder = new TriggerTimer
        {
            Name = "Dentist",
            TriggerAt = at,
            RecurrenceAnchor = at,
            Mode = AlertMode.SoundAndSpeech,
            SoundFileName = "chime.wav",
            Phrase = "Leave for the dentist",
            Enabled = false,
        };
        return Window(new TriggerEditWindow(reminder, isNew: false), height: double.NaN);
    }

    private static FrameworkElement Repeat()
    {
        var at = DateTime.Today.AddDays(1).AddHours(9).AddMinutes(30);
        var weekdays = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        };
        return Window(new RecurrenceWindow(at, weekdays), height: double.NaN);
    }

    private static FrameworkElement QuickReminder()
    {
        var window = new QuickAddWindow();
        window.InputBox.Text = "report today 9am";
        window.ErrorText.Text = Strings.QuickAdd_TimePassedToday;
        window.ErrorText.Visibility = Visibility.Visible;
        return Window(window, height: double.NaN);
    }

    private static FrameworkElement Alert()
    {
        var window = new AlertWindow("Take a break", DisplayFormat.DateAndTime(DateTime.Today.AddHours(11), use24Hour: true), canSnooze: true);
        var host = Detach(window, window.Width, double.NaN, background: Brushes.Transparent, margin: new Thickness(16, 12, 16, 22));
        Layout(host);
        return host;
    }

    private static FrameworkElement About() => Window(new AboutWindow(), height: double.NaN);

    private static FrameworkElement Shortcut() => Window(new HotkeyCaptureWindow("Ctrl+Alt+R"), height: double.NaN); // not user-visible: a shortcut

    private static FrameworkElement Window(Window window, double? height = null)
    {
        var host = Detach(window, window.Width, height ?? window.Height);
        Mockups.Inherit(host);
        Layout(host);
        return Mockups.Framed(host);
    }
}
