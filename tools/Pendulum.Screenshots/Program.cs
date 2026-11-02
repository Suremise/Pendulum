using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pendulum.App;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Wpf.Ui.Markup;
using ThemeMode = Pendulum.Core.Models.ThemeMode;

namespace Pendulum.Screenshots;

/// Renders the README's screenshots (docs/screenshots) from the app's own views, with invented
/// reminders and settings in a temp folder: never captured from a screen, so nothing private can
/// show, and run again whenever the look changes.
///
/// No window is ever shown. Each window is built but not opened; its content is moved into an
/// element laid out in memory and drawn with RenderTargetBitmap. So nothing takes the foreground
/// or focus, there is no keyboard or mouse input, and Pendulum's App class (which takes the
/// single-instance lock and would wake a running copy) is never used. The app's own Data\ and
/// Sounds\ are never touched, and nothing is saved.
public static class Program
{
    private const int WindowWidth = 1200;
    private const int WindowHeight = 800;

    [STAThread]
    public static int Main(string[] args)
    {
        // --views <folder>: every view, light and dark side by side (Views.cs). --mockups <folder>:
        // the redesign's mock-ups (Mockups.cs). Otherwise the README's pictures. For the first two,
        // each theme is drawn in a process of its own (--views-theme or --mockups-theme, then
        // light or dark, then the folder): some of WPF-UI's styles keep the colours of the theme
        // they first loaded in.
        var mode = "readme"; // not user-visible: the modes are the switches' own words
        bool? theme = null;
        if (args.Length > 0 && args[0] is "--views" or "--mockups") // not user-visible
        {
            mode = args[0][2..];
            args = args[1..];
        }
        else if (args.Length > 1 && args[0] is "--views-theme" or "--mockups-theme") // not user-visible
        {
            mode = args[0][2..^6];
            theme = args[1] == "dark"; // not user-visible
            args = args[2..];
        }
        var output = Path.GetFullPath(args.Length > 0 ? args[0]
            : mode != "readme" ? Path.Combine(Path.GetTempPath(), "pendulum-" + mode) // not user-visible
            : Path.Combine(RepoRoot(), "docs", "screenshots"));
        Directory.CreateDirectory(output);

        var temp = Path.Combine(Path.GetTempPath(), "pendulum-screenshots-" + Guid.NewGuid().ToString("N"));
        var foregroundBefore = GetForegroundWindow();
        try
        {
            // Dates as the README has always shown them, whatever this PC's region.
            var culture = CultureInfo.GetCultureInfo("en-GB");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en");

            AppPaths.BaseDirectoryOverride = temp;
            AppPaths.EnsureDirectories();
            foreach (var sound in new[] { "alert.wav", "bell.wav", "chime.wav" })
                File.WriteAllBytes(Path.Combine(AppPaths.SoundsDirectory, sound), []);

            StartApplication(theme == false ? ThemeMode.Light : ThemeMode.Dark);
            AppServices.Instance.UseSettingsForTests(InventedSettings());
            // Each with the colour the app gives a new reminder, in the order they were made.
            foreach (var reminder in InventedReminders(DateTime.Now))
            {
                reminder.Color = ReminderColors.ForNew(AppServices.Instance.Triggers);
                AppServices.Instance.Triggers.Add(reminder);
            }

            if (mode == "views") // not user-visible
            {
                if (theme is { } dark)
                    Views.RenderTheme(dark, output);
                else
                    Views.Render(output);
            }
            else if (mode == "mockups") // not user-visible
            {
                if (theme is { } dark)
                    Mockups.RenderTheme(dark, output);
                else
                    Mockups.Render(output);
            }
            else
            {
                RenderMainWindow(output);
                RenderSettings(output);
                RenderQuickReminder(output);
                RenderAlert(output);
            }

            // Proof that nothing reached the screen: no window of ours ever got a handle, and the
            // foreground was never ours. (It may still change while this runs: the user is
            // working, so a change to another app is named, not failed.)
            if (Application.Current.Windows.Cast<Window>().Any(w => new WindowInteropHelper(w).Handle != IntPtr.Zero)
                || Process.GetCurrentProcess().MainWindowHandle != IntPtr.Zero)
                throw new InvalidOperationException("A window was shown."); // not user-visible: the tool's own message
            var foregroundAfter = GetForegroundWindow();
            if (OwnerOf(foregroundAfter) == Environment.ProcessId)
                throw new InvalidOperationException("This tool took the foreground."); // not user-visible
            if (foregroundAfter != foregroundBefore)
                Console.Error.WriteLine($"Note: the foreground moved from {ProcessName(foregroundBefore)} to {ProcessName(foregroundAfter)} while rendering (not this tool)."); // not user-visible

            Console.WriteLine($"{(mode == "readme" ? "Screenshots" : mode == "views" ? "Views" : "Mock-ups")} written to {output}"); // not user-visible
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e); // not user-visible
            return 1;
        }
        finally
        {
            AppPaths.BaseDirectoryOverride = null;
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // --- The application, as App.xaml and the dark theme set it up -----------------------------

    private static void StartApplication(ThemeMode theme)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ThemesDictionary
        {
            Theme = theme == ThemeMode.Light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark,
        });
        app.Resources.MergedDictionaries.Add(new ControlsDictionary());
        var linkStyle = new Style(typeof(Hyperlink));
        linkStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(AppThemeManager.LinkBrushKey)));
        app.Resources.Add(typeof(Hyperlink), linkStyle);

        AppThemeManager.Apply(theme);
    }

    // --- Invented data -------------------------------------------------------------------------

    private static AppSettings InventedSettings() => new()
    {
        Theme = ThemeMode.Dark,
        Use24HourTime = true,
        DefaultSoundFileName = "chime.wav",
        LaunchOnWindowsStartup = true,
        StartMinimized = true,
        CheckForUpdatesOnStartup = true,
    };

    /// A plausible week or so of reminders around today: one already spent, the rest to come.
    internal static List<TriggerTimer> InventedReminders(DateTime now)
    {
        var today = now.Date;
        TriggerTimer Reminder(string name, int day, int hour, int minute, AlertMode mode, string? sound,
            RecurrenceRule? repeat = null, string? phrase = null)
        {
            var at = today.AddDays(day).AddHours(hour).AddMinutes(minute);
            return new TriggerTimer
            {
                Name = name,
                TriggerAt = at,
                RecurrenceAnchor = at,
                Mode = mode,
                SoundFileName = sound,
                Phrase = phrase,
                Recurrence = repeat,
                HasFired = at < now,
            };
        }

        var weekdays = new RecurrenceRule
        {
            Frequency = RecurrenceFrequency.Weekly,
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        };

        return
        [
            Reminder("Renew the car insurance", -1, 9, 0, AlertMode.SoundOnly, "chime.wav"),
            Reminder("Team stand-up", 1, 9, 30, AlertMode.SoundOnly, "chime.wav", weekdays),
            Reminder("Take a break", 1, 11, 0, AlertMode.SoundAndSpeech, "bell.wav", phrase: "Time for a break: stand up and stretch"),
            Reminder("Water the plants", 2, 8, 0, AlertMode.SoundOnly, "bell.wav", new RecurrenceRule { Frequency = RecurrenceFrequency.Weekly, Interval = 1 }),
            Reminder("Dentist", 3, 10, 15, AlertMode.SoundAndSpeech, "chime.wav", phrase: "Leave for the dentist"),
            Reminder("Pay the electricity bill", 4, 12, 0, AlertMode.SoundOnly, "alert.wav", new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly, DayOfMonth = today.AddDays(4).Day }),
            Reminder("Gym", 4, 18, 0, AlertMode.SoundAndSpeech, "bell.wav", phrase: "Gym in fifteen minutes"),
            Reminder("Call Mum", 6, 17, 30, AlertMode.SpeechOnly, null, phrase: "Call Mum"),
            Reminder("Book club", 9, 19, 0, AlertMode.SoundOnly, "chime.wav"),
            Reminder("Submit the expense report", 11, 16, 0, AlertMode.SoundOnly, "alert.wav"),
            Reminder("Library books due", 13, 10, 0, AlertMode.SoundOnly, "chime.wav"),
        ];
    }

    // --- The views ------------------------------------------------------------------------------

    private static void RenderMainWindow(string output)
    {
        var window = new MainWindow();
        var host = Detach(window, window.Width, window.Height);

        var tabs = window.MainTabControl;
        (string File, int Tab)[] shots = [("reminders.png", 0), ("calendar.png", 1)];
        foreach (var (file, tab) in shots)
        {
            tabs.SelectedIndex = tab;
            Layout(host);
            // The Reminders list sizes its columns when it loads, which a view never shown doesn't:
            // raised here, as the app's window does when the tab first shows.
            foreach (var panel in Descendants(host).OfType<TriggersPanel>())
                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Layout(host);
            // The calendar builds its grid once it's on screen (its Loaded): here, by hand.
            foreach (var calendar in Descendants(host).OfType<CalendarPanel>())
                (calendar.DataContext as CalendarViewModel)?.RefreshForDisplay();
            Layout(host);
            Save(host, Path.Combine(output, file));
        }
    }

    /// Tools > Settings…: the Settings window, at its own size.
    private static void RenderSettings(string output)
    {
        var window = new SettingsWindow();
        var host = Detach(window, window.Width, window.Height);
        Layout(host);
        Save(host, Path.Combine(output, "settings.png"));
    }

    private static void RenderQuickReminder(string output)
    {
        var window = new QuickAddWindow();
        window.InputBox.Text = "call Mum tomorrow 5:30pm";
        var host = Detach(window, window.Width, double.NaN);
        Layout(host);
        Save(host, Path.Combine(output, "quick-reminder.png"));
    }

    private static void RenderAlert(string output)
    {
        var at = DateTime.Today.AddHours(11);
        var window = new AlertWindow("Take a break", DisplayFormat.DateAndTime(at, use24Hour: true), canSnooze: true);
        // The alert draws its own rounded card and shadow on a transparent window: room for the
        // shadow round it, and transparent beyond, as on the desktop.
        var host = Detach(window, window.Width, double.NaN, background: Brushes.Transparent, margin: new Thickness(16, 12, 16, 22));
        Layout(host);
        Save(host, Path.Combine(output, "alert.png"));
    }

    // --- Laying out and drawing off screen -----------------------------------------------------

    /// Moves a window's content into a border of the window's size and background, never shown.
    internal static Border Detach(Window window, double width, double height, Brush? background = null, Thickness margin = default)
    {
        var content = (UIElement)window.Content;
        window.Content = null;

        // A Fluent window's own background is the Mica Windows draws behind it: here, the theme's.
        var fill = background ?? (Brush)Application.Current.Resources["ApplicationBackgroundBrush"]; // not user-visible: a resource key
        var inner = new Border { Child = content, Background = fill == Brushes.Transparent ? null : fill };
        // Resources the window held (its styles) stay reachable from the content.
        inner.Resources.MergedDictionaries.Add(window.Resources);
        // What the window passed down to its content: the theme's text colour (the menu bar, the
        // headings). Without it, text that takes its colour from the window draws black on the
        // dark theme.
        inner.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush"); // not user-visible: a resource key
        var host = new Border
        {
            Child = inner,
            Padding = margin,
            Background = Brushes.Transparent,
            Width = width + margin.Left + margin.Right,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        if (!double.IsNaN(height))
            host.Height = height + margin.Top + margin.Bottom;
        TextOptions.SetTextFormattingMode(host, TextFormattingMode.Ideal);
        return host;
    }

    internal static void Layout(FrameworkElement element)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var available = new Size(element.Width, double.IsNaN(element.Height) ? double.PositiveInfinity : element.Height);
            element.Measure(available);
            var size = new Size(element.Width, double.IsNaN(element.Height) ? element.DesiredSize.Height : element.Height);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
            // Bindings, item containers and templates that WPF finishes later, at a lower priority.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    internal static void Save(FrameworkElement element, string path)
    {
        // A title bar's buttons take their colour from events a window never shown doesn't get:
        // here, the theme's text colour, as the app's title bars draw them.
        var ink = Application.Current.Resources["TextFillColorPrimaryBrush"]; // not user-visible: a resource key
        foreach (var button in Descendants(element).OfType<Wpf.Ui.Controls.TitleBarButton>())
            button.SetValue(Wpf.Ui.Controls.TitleBarButton.RenderButtonsForegroundProperty, ink);
        Layout(element);
        Save(Draw(element), path);
    }

    internal static BitmapSource Draw(FrameworkElement element)
    {
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    internal static void Save(BitmapSource bitmap, string path)
    {
        var (width, height) = (bitmap.PixelWidth, bitmap.PixelHeight);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"  {Path.GetFileName(path)}  {width}x{height}"); // not user-visible
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pendulum.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository, or pass the output folder."); // not user-visible
    }

    private static int OwnerOf(IntPtr window)
    {
        _ = GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    private static string ProcessName(IntPtr window)
    {
        try { return Process.GetProcessById(OwnerOf(window)).ProcessName; }
        catch (ArgumentException) { return "(none)"; } // not user-visible
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
