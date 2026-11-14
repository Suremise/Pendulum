using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Pendulum.App;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Wpf.Ui.Controls;
using static Pendulum.Screenshots.Program;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using ThemeMode = Pendulum.Core.Models.ThemeMode;

namespace Pendulum.Screenshots;

/// Every view of the app as it is, in light and dark side by side (--views &lt;folder&gt;): for
/// checking a change to the look before and after, across the whole app. Drawn off screen like
/// the README's pictures, with the same invented reminders; nothing is shown or saved.
internal static class Views
{
    /// Which theme the pictures being drawn are in (the frame round a window follows it).
    private static bool _dark;

    private static readonly (string File, string Title, Func<FrameworkElement> Build)[] Shots =
    [
        ("01-reminders.png", "Reminders", () => MainWindowShot(tab: 0)),
        ("01-badge-values.png", "Every reminder type, mode and status", BadgeValues),
        ("01-badge-filters.png", "Type, mode and status filter options (laid out without opening popups)", BadgeFilters),
        ("02-calendar.png", "Calendar: a busy month, each reminder in its own colour", () => MainWindowShot(tab: 1)),
        ("02-calendar-small.png", "Calendar in the smallest window (820 × 560): \"+N more\" beside the day's number", () => MainWindowShot(tab: 1, 820, 560)),
        ("03-settings.png", "Settings", Settings),
        ("03-settings-alerts.png", "Settings: alerts", () => SettingsSection(1)),
        ("03-settings-voice.png", "Settings: Windows speech", () => SettingsSection(2)),
        ("03-settings-piper.png", "Settings: Piper speech", () => SettingsSection(2, piper: true)),
        ("03-settings-quick.png", "Settings: Quick Reminder", () => SettingsSection(3)),
        ("03-settings-whisper.png", "Settings: Whisper speech recognition", () => SettingsSection(3, whisper: true)),
        ("03-settings-data.png", "Settings: backup and data", () => SettingsSection(4)),
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
                Save(SideBySide(shot.Title, Load(Path.Combine(parts, "light", shot.File)), Load(Path.Combine(parts, "dark", shot.File))),
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
        _dark = dark;

        // A busy day, so the calendar shows "+N more".
        var busy = DateTime.Today.AddDays(3);
        foreach (var (name, hour) in new[] { ("School run", 8), ("Parcel collection", 13), ("Pick up prescription", 16) })
        {
            var at = busy.AddHours(hour);
            AppServices.Instance.Reminders.Add(new TriggerTimer
            {
                Name = name,
                TriggerAt = at,
                RecurrenceAnchor = at,
                SoundFileName = "chime.wav", // not user-visible: invented
                Color = ReminderColors.ForNew(AppServices.Instance.Triggers),
            });
        }
        AppServices.Instance.Settings.Theme = dark ? ThemeMode.Dark : ThemeMode.Light;
        AppThemeManager.Apply(dark ? ThemeMode.Dark : ThemeMode.Light);
        foreach (var shot in Shots)
        {
            var settings = AppServices.Instance.Settings;
            var tts = settings.TextToSpeechEngine;
            var stt = settings.SpeechToTextEngine;
            var originalReminders = shot.File == "01-badge-values.png" ? AppServices.Instance.Triggers.ToArray() : null; // not user-visible: a fixture file
            try { Save(Draw(shot.Build()), Path.Combine(output, shot.File)); }
            finally
            {
                settings.TextToSpeechEngine = tts;
                settings.SpeechToTextEngine = stt;
                if (originalReminders is not null) AppServices.Instance.Reminders.ReplaceAll(originalReminders);
            }
        }
    }

    private static FrameworkElement MainWindowShot(int tab, double width = 1100, double height = 720)
    {
        var window = new MainWindow();
        var host = Detach(window, width, height);
        Inherit(host);
        window.MainTabControl.SelectedIndex = tab;
        Layout(host);
        // What the window does when it first shows: the list sizes its columns, the calendar
        // builds its grid.
        foreach (var panel in Descendants(host).OfType<TriggersPanel>())
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        foreach (var calendar in Descendants(host).OfType<CalendarPanel>())
            (calendar.DataContext as CalendarViewModel)?.RefreshForDisplay();
        Layout(host);
        return Framed(host);
    }

    private static FrameworkElement BadgeValues()
    {
        var reminders = new List<TriggerTimer>();
        foreach (var scheduled in new[] { false, true })
            foreach (var mode in Enum.GetValues<AlertMode>())
                foreach (var status in new[] { 0, 1, 2 })
                {
                    var at = DateTime.Today.AddDays(3).AddMinutes(reminders.Count);
                    reminders.Add(new TriggerTimer
                    {
                        Name = $"{(scheduled ? "Scheduled" : "Fixed")} / {mode} / {status}", // not user-visible: invented fixture labels
                        TriggerAt = at,
                        RecurrenceAnchor = at,
                        Recurrence = scheduled ? new RecurrenceRule { Frequency = RecurrenceFrequency.Daily } : null,
                        Mode = mode,
                        Enabled = status == 0,
                        HasFired = status == 2,
                        Color = "teal", // not user-visible: stable palette key
                        SoundFileName = "chime.wav" // not user-visible: invented fixture
                    });
                }
        AppServices.Instance.Reminders.ReplaceAll(reminders);
        var surface = MainWindowShot(0, 1100, 1280);
        if (Descendants(surface).OfType<System.Windows.Controls.ListViewItem>().Count(item => item.DataContext is TriggerTimer) != reminders.Count)
            throw new InvalidOperationException("The badge matrix did not lay out every reminder."); // not user-visible: renderer validation
        return surface;
    }

    private static FrameworkElement BadgeFilters()
    {
        var panel = new TriggersPanel { Width = 1100, Height = 720 };
        Layout(panel);
        var options = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var property in new[] { nameof(TriggerTimer.IsScheduled), nameof(TriggerTimer.Mode), nameof(TriggerTimer.StatusSortOrder) })
        {
            var header = Descendants(panel).OfType<GridViewColumnHeader>().Single(item => Equals(item.Tag, property));
            var popup = ((StackPanel)header.Content).Children.OfType<Popup>().Single();
            var content = (FrameworkElement)popup.Child;
            popup.Child = null;
            content.DataContext = panel.DataContext;
            content.Margin = new Thickness(8);
            options.Children.Add(content);
        }
        var host = new Border { Child = options, Padding = new Thickness(16) };
        host.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush"); // not user-visible: a theme resource
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Width = host.DesiredSize.Width;
        host.Height = host.DesiredSize.Height;
        Layout(host);
        return host;
    }

    private static FrameworkElement Settings() => Window(new SettingsWindow());

    private static FrameworkElement SettingsSection(int section, bool piper = false, bool whisper = false)
    {
        var settings = AppServices.Instance.Settings;
        settings.TextToSpeechEngine = piper ? TextToSpeechEngine.Piper : TextToSpeechEngine.WindowsSpeechSynthesis;
        settings.SpeechToTextEngine = whisper ? SpeechToTextEngine.Whisper : SpeechToTextEngine.WindowsSpeechRecognition;
        var host = Window(new SettingsWindow());
        var panel = Descendants(host).OfType<SettingsPanel>().Single();
        ((ListBox)panel.FindName("SectionList")).SelectedIndex = section; // not user-visible: the view's own element
        Layout(host);
        return host;
    }

    private static FrameworkElement Editor()
    {
        var at = DateTime.Today.AddDays(3).AddHours(10).AddMinutes(15);
        var reminder = new TriggerTimer
        {
            Name = "Dentist",
            Color = "teal", // not user-visible: stable palette key for this invented reminder
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
        var window = new AlertWindow("Take a break", DisplayFormat.DateAndTime(DateTime.Today.AddHours(11), use24Hour: true));
        var host = Detach(window, window.Width, double.NaN, background: Brushes.Transparent, margin: new Thickness(16, 12, 16, 22));
        Layout(host);
        return host;
    }

    private static FrameworkElement About() => Window(new AboutWindow(), height: double.NaN);

    private static FrameworkElement Shortcut() => Window(new HotkeyCaptureWindow("Ctrl+Alt+R"), height: double.NaN); // not user-visible: a shortcut

    private static FrameworkElement Window(Window window, double? height = null)
    {
        var host = Detach(window, window.Width, height ?? window.Height);
        Inherit(host);
        Layout(host);
        return Framed(host);
    }

    // --- Framing and sheets ------------------------------------------------------------------------

    private static BitmapSource Load(string path)
    {
        using var file = File.OpenRead(path);
        var frame = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// The text colour a window gives what's in it (its theme's), which content moved out of the
    /// window would otherwise lose: the menu's words and the title bar's buttons inherit it.
    private static void Inherit(FrameworkElement host)
    {
        host.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush"); // not user-visible
        foreach (var bar in Descendants(host).OfType<TitleBar>().Concat(LogicalTitleBars(host)))
            bar.SetResourceReference(TitleBar.ButtonsForegroundProperty, "TextFillColorPrimaryBrush"); // not user-visible
    }

    private static IEnumerable<TitleBar> LogicalTitleBars(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()
            .SelectMany(c => (c is TitleBar t ? [t] : Enumerable.Empty<TitleBar>()).Concat(LogicalTitleBars(c)));

    /// A window as it sits on the desktop: rounded corners, a hairline edge and a soft shadow.
    private static FrameworkElement Framed(Border host)
    {
        // The title bar's buttons take their glyphs' colour as they load, before the content is
        // in a theme: given the theme's text colour here.
        foreach (var button in Descendants(host).OfType<TitleBarButton>())
            button.SetValue(TitleBarButton.RenderButtonsForegroundProperty, Application.Current.Resources["TextFillColorPrimaryBrush"]); // not user-visible
        Layout(host);
        var (width, height) = (host.ActualWidth, host.ActualHeight);
        host.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 8, 8);
        var shadow = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["ApplicationBackgroundBrush"], // not user-visible
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = _dark ? 0.6 : 0.25 },
        };
        var edge = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Frozen(_dark ? "#FF454545" : "#33000000"), // not user-visible: the tool's own frame
        };
        var stack = new Grid { Margin = new Thickness(28, 20, 28, 36) };
        stack.Children.Add(shadow);
        stack.Children.Add(host);
        stack.Children.Add(edge);
        var frame = new Border { Child = stack, Background = Brushes.Transparent, UseLayoutRounding = true, Width = width + 56 };
        TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Ideal);
        Layout(frame);
        return frame;
    }

    /// The light and dark renderings side by side, each on a desktop of its theme, under a title.
    private static BitmapSource SideBySide(string title, BitmapSource light, BitmapSource dark)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(28, 16, 28, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        var head = new Border { Background = Frozen("#F3F3F3"), Child = caption }; // not user-visible: the tool's own sheet
        Grid.SetColumnSpan(head, 2);
        grid.Children.Add(head);
        foreach (var (bitmap, col, back, label) in new[] { (light, 0, "#E4E6EA", "Light"), (dark, 1, "#141417", "Dark") }) // not user-visible
        {
            var side = new StackPanel { Background = Frozen(back) };
            side.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(28, 12, 0, 0),
                Foreground = col == 0 ? Brushes.Black : Brushes.White,
            });
            side.Children.Add(new Image { Source = bitmap, Stretch = Stretch.None, Margin = new Thickness(0, 4, 0, 0) });
            if (bitmap.PixelWidth < 900)
                ((Image)side.Children[1]).Margin = new Thickness(28, 12, 28, 28);
            Grid.SetRow(side, 1);
            Grid.SetColumn(side, col);
            grid.Children.Add(side);
        }
        var page = new Border { Child = grid, UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(page, TextFormattingMode.Ideal);
        page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        page.Width = page.DesiredSize.Width;
        Layout(page);
        return Draw(page);
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
