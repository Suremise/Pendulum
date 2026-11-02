using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.Tests;

/// The calendar's day cells: each reminder's chip in its own colour, and "+N more" kept inside
/// its cell, with room below it, however big or small the window. Laid out in memory, never
/// shown.
[Collection(nameof(SettingsScreen))]
public sealed class CalendarCellTests : IDisposable
{
    private static readonly DateTime First = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // A busy day, a day of two, a day of one and a day of three, in the month the calendar opens on.
    private readonly TriggerTimer[] _reminders =
    [
        .. Enumerable.Range(0, 6).Select(i => Reminder($"Busy {i}", First.AddDays(9).AddHours(8 + i))),
        Reminder("Pair 1", First.AddDays(11).AddHours(9)),
        Reminder("Pair 2", First.AddDays(11).AddHours(10)),
        Reminder("Single", First.AddDays(13).AddHours(9)),
        .. Enumerable.Range(0, 3).Select(i => Reminder($"Three {i}", First.AddDays(16).AddHours(9 + i))),
    ];

    private readonly string _base = Path.Combine(Path.GetTempPath(), "pendulum-calendar-tests-" + Guid.NewGuid().ToString("N"));

    private static TriggerTimer Reminder(string name, DateTime at) =>
        new() { Name = name, TriggerAt = at, RecurrenceAnchor = at, HasFired = false };

    public CalendarCellTests()
    {
        AppPaths.BaseDirectoryOverride = _base;
        AppPaths.EnsureDirectories();
        UiThread.Invoke(() =>
        {
            AppServices.Instance.UseSettingsForTests(new AppSettings());
            foreach (var r in _reminders)
                AppServices.Instance.Triggers.Add(r);
        });
    }

    public void Dispose()
    {
        UiThread.Invoke(() =>
        {
            foreach (var r in _reminders)
                AppServices.Instance.Triggers.Remove(r);
            Assert.Empty(Application.Current.Windows);
        });
        Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
        AppPaths.BaseDirectoryOverride = null;
        try { Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static TheoryData<double, double> WindowSizes => new()
    {
        { 820, 560 },     // the main window's smallest
        { 1000, 640 },
        { 1200, 800 },    // its first size
        { 1440, 900 },
        { 1920, 1080 },
    };

    /// Every chip and "+N more" lies wholly inside its day's cell, "+N more" with room below it,
    /// and between them they account for every reminder that day.
    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void Every_chip_and_plus_n_more_fits_in_its_cell(double width, double height)
    {
        var problems = UiThread.Invoke(() => InMainWindow(width, height, calendar =>
        {
            var found = new List<string>();
            var vm = (CalendarViewModel)calendar.DataContext;
            for (var i = 0; i < vm.Days.Count; i++)
            {
                var cell = (FrameworkElement)calendar.DayGrid.ItemContainerGenerator.ContainerFromIndex(i);
                var day = vm.Days[i];
                var chips = Visible(cell).OfType<Border>().Where(b => ReferenceEquals(b.Style, calendar.Resources["ReminderPillBadge"])).ToList();
                var more = Visible(cell).OfType<ToggleButton>().SingleOrDefault(t => t.Name == "OverflowToggle");

                foreach (var element in chips.Cast<FrameworkElement>().Concat(more is null ? [] : [more]))
                {
                    var bounds = element.TransformToAncestor(cell).TransformBounds(new Rect(element.RenderSize));
                    // Inside the cell's edge (0.6) and padding (3): never touching its foot.
                    if (bounds.Top < 0 || bounds.Left < 0 || bounds.Right > cell.ActualWidth + 0.01
                        || bounds.Bottom > cell.ActualHeight - 3.6 + 0.01)
                        found.Add($"{day.Date:d MMM}: {element.GetType().Name} at {bounds} in a cell of {cell.ActualWidth:0.#}x{cell.ActualHeight:0.#}");
                }

                var hidden = more is null ? 0 : day.OverflowCount;
                if (chips.Count + hidden != day.Reminders.Count)
                    found.Add($"{day.Date:d MMM}: {chips.Count} chips and {hidden} more, of {day.Reminders.Count}");
                var expected = !day.IsCompact ? string.Format(Strings.Calendar_MoreFormat, day.OverflowCount)
                    : day.Reminders.Count == 1 ? Strings.Calendar_OneReminder
                    : string.Format(Strings.Calendar_RemindersFormat, day.Reminders.Count);
                if (more is not null && ((TextBlock)more.Content).Text != expected)
                    found.Add($"{day.Date:d MMM}: says \"{((TextBlock)more.Content).Text}\" for {day.OverflowCount} more");
            }
            return found;
        }));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void A_busy_day_shows_plus_n_more_and_a_quiet_one_does_not(double width, double height)
    {
        var (busyMore, singleMore, compact) = UiThread.Invoke(() => InMainWindow(width, height, calendar =>
        {
            var vm = (CalendarViewModel)calendar.DataContext;
            var busy = vm.Days.Single(d => d.Date == First.AddDays(9));
            var single = vm.Days.Single(d => d.Date == First.AddDays(13));
            return (busy.HasOverflow, single.HasOverflow, single.IsCompact);
        }));

        Assert.True(busyMore);
        // A day's one reminder has its chip, unless the window is too small for any (then it's
        // "+1 more", beside the day's number).
        Assert.Equal(compact, singleMore);
    }

    /// A chip is painted in its reminder's colour: the palette's tint, with a bar of its shade.
    [Fact]
    public void Each_chip_is_in_its_reminder_s_colour()
    {
        var wrong = UiThread.Invoke(() =>
        {
            AppThemeManager.Apply(Pendulum.Core.Models.ThemeMode.Light);
            return InMainWindow(1200, 800, calendar =>
            {
                var found = new List<string>();
                foreach (var chip in Visible(calendar.DayGrid).OfType<Border>().Where(b => ReferenceEquals(b.Style, calendar.Resources["ReminderPillBadge"])))
                {
                    var reminder = (TriggerTimer)chip.DataContext;
                    var tint = Application.Current.Resources[PaletteBrush.KeyFor(reminder.DisplayColor, tint: true)!];
                    var shade = Application.Current.Resources[PaletteBrush.KeyFor(reminder.DisplayColor, tint: false)!];
                    if (!ReferenceEquals(chip.Background, tint) || !ReferenceEquals(chip.BorderBrush, shade))
                        found.Add($"{reminder.Name} ({reminder.DisplayColor})");
                }
                return found;
            });
        });

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData(2, 3, 2, 0)]   // fewer than fit: all shown
    [InlineData(3, 3, 3, 0)]   // exactly as many as fit: all shown
    [InlineData(4, 3, 2, 2)]   // one more than fits: a line kept for "+2 more"
    [InlineData(6, 2, 1, 5)]
    [InlineData(2, 1, 0, 2)]   // room for one line: just "+2 more"
    [InlineData(1, 1, 1, 0)]
    [InlineData(3, 0, 0, 3)]   // no room under the number: "+3 more" beside it
    public void A_day_shows_what_fits_and_keeps_a_line_for_the_rest(int reminders, int lines, int shown, int more)
    {
        var day = new CalendarDayCell
        {
            Lines = lines,
            Reminders = Enumerable.Range(0, reminders).Select(i => new TriggerTimer { Name = $"R{i}" }).ToList(),
        };

        Assert.Equal(shown, day.VisibleReminders.Count);
        Assert.Equal(more, day.OverflowCount);
        Assert.Equal(more > 0, day.HasOverflow);
    }

    // --- Laying the main window out in memory -----------------------------------------------------

    private static T InMainWindow<T>(double width, double height, Func<CalendarPanel, T> read)
    {
        var window = new MainWindow();
        try
        {
            window.MainTabControl.SelectedIndex = 1;
            var content = (UIElement)window.Content;
            window.Content = null;
            var host = new Border { Child = content, Width = width, Height = height };
            host.Resources.MergedDictionaries.Add(window.Resources);
            Layout(host);
            var calendar = Descendants(host).OfType<CalendarPanel>().Single();
            ((CalendarViewModel)calendar.DataContext).RefreshForDisplay();   // as when the tab first shows
            Layout(host);
            return read(calendar);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Layout(FrameworkElement element)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            element.Measure(new Size(element.Width, element.Height));
            element.Arrange(new Rect(0, 0, element.Width, element.Height));
            element.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    private static IEnumerable<DependencyObject> Visible(DependencyObject root) =>
        Descendants(root).Where(d => d is not UIElement e || e.IsVisible || e.Visibility == Visibility.Visible && VisibleUpTo(e, root));

    // Not shown on screen, IsVisible is false throughout: what's laid out and not collapsed counts.
    private static bool VisibleUpTo(UIElement element, DependencyObject root)
    {
        for (DependencyObject? d = element; d is not null && d != root; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible })
                return false;
        return true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }
}
