using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

/// The Reminders list as the window resizes, and its rows' ways to edit and delete: built on the
/// tests' UI thread and laid out in memory, never shown. Keys and clicks are routed to the
/// panel's own handlers in-process (no keyboard or mouse input), and editing and deleting are
/// recorded rather than opening the editor or a confirmation.
[Collection(nameof(SettingsScreen))]
public sealed class RemindersListTests : IDisposable
{
    private readonly TriggerTimer[] _reminders =
    [
        new() { Name = "Dentist", TriggerAt = new DateTime(2026, 9, 30, 22, 45, 0), Mode = AlertMode.SoundAndSpeech, SoundFileName = "chime.wav" },
        new() { Name = "Renew the car insurance", TriggerAt = new DateTime(2026, 9, 2, 10, 15, 0), HasFired = true, SoundFileName = "alert.wav" },
        new() { Name = "Book club", TriggerAt = new DateTime(2026, 11, 25, 19, 0, 0), Enabled = false, Mode = AlertMode.SpeechOnly,
                Recurrence = new RecurrenceRule { Frequency = RecurrenceFrequency.Monthly } },
    ];

    public RemindersListTests() => UiThread.Invoke(() =>
    {
        foreach (var r in _reminders)
            AppServices.Instance.Triggers.Add(r);
    });

    public void Dispose()
    {
        UiThread.Invoke(() =>
        {
            foreach (var r in _reminders)
                AppServices.Instance.Triggers.Remove(r);
            Assert.Empty(Application.Current.Windows);
        });
        Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
    }

    private static T WithPanel<T>(double width, Func<TriggersPanel, T> read, AppSettings? settings = null) =>
        UiThread.Invoke(() =>
        {
            var before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");   // "AM"/"PM", the longer
            try
            {
                AppServices.Instance.UseSettingsForTests(settings ?? new AppSettings { Use24HourTime = false, ReminderFilterRowVisible = true });
                var panel = new TriggersPanel();
                Resize(panel, width);
                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));   // as when the tab first shows
                Resize(panel, width);
                return read(panel);
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        });

    private static void Resize(TriggersPanel panel, double width)
    {
        panel.Width = width;
        panel.Height = 700;
        for (var pass = 0; pass < 3; pass++)
        {
            panel.Measure(new Size(width, 700));
            panel.Arrange(new Rect(0, 0, width, 700));
            panel.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    private static ListView List(TriggersPanel p) => (ListView)p.FindName("RemindersList");
    private static GridViewColumnCollection Columns(TriggersPanel p) => ((GridView)List(p).View).Columns;
    private static GridViewColumn Column(TriggersPanel p, string name) => (GridViewColumn)p.FindName(name);

    private static double Viewport(TriggersPanel p) =>
        Descendants(List(p)).OfType<ScrollViewer>().First().ViewportWidth;

    [Fact]
    public void Name_takes_the_spare_width_as_the_window_widens()
    {
        var (narrow, wide, wideViewport, wideTotal) = WithPanel(1000, p =>
        {
            var narrowName = Column(p, "NameColumn").Width;
            Resize(p, 1600);
            return (narrowName, Column(p, "NameColumn").Width, Viewport(p), Columns(p).Sum(c => c.ActualWidth));
        });

        Assert.Equal(600, wide - narrow, 1);   // every extra pixel goes to Name
        Assert.InRange(wideTotal, wideViewport - 4, wideViewport);   // the columns fill the rows, no wider
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1600)]
    public void No_column_cuts_off_its_text_and_the_toggle_sits_at_the_right_edge(double width)
    {
        var (clipped, total, viewport, toggleRight) = WithPanel(width, p =>
        {
            var cols = Columns(p);
            var cut = new List<string>();
            foreach (var row in Descendants(List(p)).OfType<GridViewRowPresenter>())
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(row); i++)
                {
                    var cell = (FrameworkElement)VisualTreeHelper.GetChild(row, i);
                    if (cols[i].ActualWidth < 1)
                        continue;
                    foreach (var text in Descendants(cell).OfType<TextBlock>().Where(t => t.Text.Length > 0 && Shown(t, cell)))
                    {
                        var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
                        natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var x = text.TranslatePoint(new Point(0, 0), row).X;
                        var columnRight = cols.Take(i + 1).Sum(c => c.ActualWidth) - 6;
                        if (x + natural.DesiredSize.Width > Math.Min(x + text.ActualWidth, columnRight) + 0.5)
                            cut.Add($"{text.Text} (col {i}: needs {natural.DesiredSize.Width:F1}, has {Math.Min(text.ActualWidth, columnRight - x):F1})");
                    }
                }
            }
            return (cut, cols.Sum(c => c.ActualWidth), Viewport(p), cols.Sum(c => c.ActualWidth));
        });

        Assert.Empty(clipped);
        Assert.True(total <= viewport, $"columns {total} in a {viewport} viewport");
        Assert.InRange(toggleRight, viewport - 4, viewport);
    }

    [Fact]
    public void In_a_narrow_window_Sound_then_Mode_step_aside_so_Name_keeps_its_room()
    {
        var (soundAside, modeAside, sound, mode, name, total, viewport) = WithPanel(700, p =>
            (p.SoundStepsAside, p.ModeStepsAside, Column(p, "SoundColumn").Width, Column(p, "ModeColumn").Width,
             Column(p, "NameColumn").Width, Columns(p).Sum(c => c.ActualWidth), Viewport(p)));

        Assert.True(soundAside);
        Assert.True(modeAside);
        Assert.Equal(0, sound);
        Assert.Equal(0, mode);
        Assert.True(name >= TriggersPanel.NameMinWidth);
        Assert.True(total <= viewport + 0.5, $"columns {total} in a {viewport} viewport");
    }

    [Fact]
    public void Editing_a_reminders_sound_to_a_longer_name_widens_the_Sound_column()
    {
        var dentist = _reminders[0];
        var (before, after, column, afterRemoval) = WithPanel(1400, p =>
        {
            var sound = Column(p, "SoundColumn");
            var was = p.FittedWidth(sound);

            // As an edit does: the reminder changes in place, and the list is laid out again.
            dentist.SoundFileName = "longer reminder sound.wav";
            Resize(p, 1400);
            var widened = p.FittedWidth(sound);
            var shown = sound.Width;

            // A reminder taken out of the list is no longer watched: its changes refit nothing.
            AppServices.Instance.Triggers.Remove(dentist);
            Resize(p, 1400);
            var removed = p.FittedWidth(sound);
            dentist.SoundFileName = "an even longer name for a sound file.wav";
            Resize(p, 1400);
            AppServices.Instance.Triggers.Add(dentist);
            return (was, widened, shown, (removed, p.FittedWidth(sound)));
        });
        dentist.SoundFileName = "chime.wav";

        Assert.True(after > before + 50, $"{before} to {after}");
        Assert.Equal(after, column);
        Assert.True(afterRemoval.removed < after);   // back to the others' sounds
    }

    [Fact]
    public void A_fitted_column_dragged_narrower_than_its_content_comes_back()
    {
        var (width, fitted) = WithPanel(1200, p =>
        {
            var status = Column(p, "StatusColumn");
            status.Width = 20;
            return (status.Width, p.FittedWidth(status));
        });

        Assert.True(fitted > 20);
        Assert.Equal(fitted, width);
    }

    [Fact]
    public void Double_click_or_Enter_edits_a_row_and_Delete_deletes_it()
    {
        var (edited, deleted, onButton, inFilterBox) = WithPanel(1200, p =>
        {
            var edits = new List<TriggerTimer>();
            var deletes = new List<TriggerTimer>();
            p.EditRow = edits.Add;
            p.DeleteRow = deletes.Add;
            var dentist = _reminders[0];
            var row = Descendants(List(p)).OfType<ListViewItem>().Single(r => r.DataContext == dentist);
            var nameText = Descendants(row).OfType<TextBlock>().First(t => t.Text == dentist.Name);
            var toggle = Descendants(row).OfType<Button>().Single();

            p.OpenRow(row, nameText);                       // a double-click on the row
            var buttonOpens = p.OpenRow(row, toggle);       // a double-click on its on/off button
            p.RowKey(Key.Enter, nameText);
            p.RowKey(Key.Delete, nameText);
            var filterBox = Descendants(List(p)).OfType<TextBox>().First();   // the filter row's name box
            var fromFilterBox = p.RowKey(Key.Delete, filterBox);
            return (edits, deletes, buttonOpens, fromFilterBox);
        });

        Assert.Equal([_reminders[0], _reminders[0]], edited);
        Assert.Equal([_reminders[0]], deleted);
        Assert.False(onButton);
        Assert.False(inFilterBox);
    }

    [Fact]
    public void The_right_click_menu_edits_turns_off_and_deletes_and_rows_are_named()
    {
        var (headers, gestures, turnOnShown, edited, deleted, turnedOff, rowName, toggleName) = WithPanel(1200, p =>
        {
            var edits = new List<TriggerTimer>();
            var deletes = new List<TriggerTimer>();
            p.EditRow = edits.Add;
            p.DeleteRow = deletes.Add;
            var dentist = _reminders[0];
            var row = Descendants(List(p)).OfType<ListViewItem>().Single(r => r.DataContext == dentist);
            p.PrepareRowMenu(row);   // as the menu opens
            var items = row.ContextMenu.Items.OfType<MenuItem>().ToList();
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));   // Edit…
            items[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));   // Delete
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));   // Turn off
            var off = !dentist.Enabled;
            dentist.Enabled = true;
            return (items.Select(i => (string)i.Header).ToList(), items.Select(i => i.InputGestureText).ToList(),
                items[2].Visibility, edits, deletes, off,
                UIElementAutomationPeer.CreatePeerForElement(row).GetName(),
                UIElementAutomationPeer.CreatePeerForElement(Descendants(row).OfType<Button>().Single()).GetName());
        });

        Assert.Equal([Strings.Reminders_RowEdit, Strings.Reminders_RowTurnOff, Strings.Reminders_RowTurnOn, Strings.Reminders_RowDelete], headers);
        Assert.Equal(["Enter", "", "", "Del"], gestures);
        Assert.Equal(Visibility.Collapsed, turnOnShown);   // it's on: only "Turn off" shows
        Assert.Equal([_reminders[0]], edited);
        Assert.Equal([_reminders[0]], deleted);
        Assert.True(turnedOff);
        Assert.Equal("Dentist", rowName);
        Assert.Equal(Strings.Reminders_Disable, toggleName);
    }

    [Fact]
    public void Rows_have_no_Edit_or_Delete_buttons_any_more_only_the_on_off_button()
    {
        var buttons = WithPanel(1200, p =>
            Descendants(List(p)).OfType<ListViewItem>().Select(r => Descendants(r).OfType<Button>().Count()).ToList());

        Assert.All(buttons, n => Assert.Equal(1, n));
    }

    private static bool Shown(DependencyObject e, DependencyObject top)
    {
        for (var d = e; d is not null && d != top; d = VisualTreeHelper.GetParent(d))
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
