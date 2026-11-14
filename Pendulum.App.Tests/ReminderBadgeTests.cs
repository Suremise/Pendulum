using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using ThemeMode = Pendulum.Core.Models.ThemeMode;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class ReminderBadgeTests
{
    [Fact]
    public void Each_badge_cell_constructs_one_border_and_one_label() => WithRow((panel, row) =>
    {
        foreach (var column in new[] { "TypeColumn", "ModeColumn", "StatusColumn" })
        {
            var cell = Cell(panel, row, column);
            Assert.Single(Elements(cell).OfType<Border>());
            Assert.Single(Elements(cell).OfType<TextBlock>());
            var presenter = Assert.Single(Elements(cell).OfType<ContentControl>());
            Assert.False(presenter.Focusable);
            Assert.False(presenter.IsTabStop);
        }
    });

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void All_badge_values_follow_live_changes_with_the_same_border_and_theme_brushes(ThemeMode theme) => WithRow((panel, row) =>
    {
        var reminder = (TriggerTimer)row.DataContext;
        var type = Cell(panel, row, "TypeColumn");
        var mode = Cell(panel, row, "ModeColumn");
        var status = Cell(panel, row, "StatusColumn");
        var typeBorder = Assert.Single(Elements(type).OfType<Border>());
        var modeBorder = Assert.Single(Elements(mode).OfType<Border>());
        var statusBorder = Assert.Single(Elements(status).OfType<Border>());

        foreach (var scheduled in new[] { false, true })
            foreach (var alertMode in Enum.GetValues<AlertMode>())
                foreach (var state in new[] { 0, 1, 2 })
                {
                    reminder.Recurrence = new RecurrenceRule { Frequency = scheduled ? RecurrenceFrequency.Daily : RecurrenceFrequency.None };
                    reminder.Mode = alertMode;
                    reminder.HasFired = state == 2;
                    reminder.Enabled = state == 0;
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    panel.UpdateLayout();
                    Assert.Same(typeBorder, Assert.Single(Elements(type).OfType<Border>()));
                    Assert.Same(modeBorder, Assert.Single(Elements(mode).OfType<Border>()));
                    Assert.Same(statusBorder, Assert.Single(Elements(status).OfType<Border>()));
                    Assert.Equal(scheduled ? Strings.Reminders_Scheduled : Strings.Reminders_Fixed, Label(type).Text);
                    Assert.Equal(new Pendulum.App.Converters.EnumSpacedDisplayConverter().Convert(alertMode, typeof(string), null!, System.Globalization.CultureInfo.CurrentCulture), Label(mode).Text);
                    Assert.Equal(state switch { 0 => Strings.Reminders_Upcoming, 1 => Strings.Reminders_Disabled, _ => Strings.Reminders_Spent }, Label(status).Text);
                    var foreground = Application.Current.FindResource(state == 2 ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
                    Assert.Same(foreground, Label(type).Foreground);
                    Assert.Same(foreground, Label(mode).Foreground);
                    Assert.Same(foreground, Label(status).Foreground);
                    Assert.Same(Application.Current.FindResource(alertMode switch
                    {
                        AlertMode.SoundOnly => "PendulumBadgeSoundOnlyBrush",
                        AlertMode.SoundAndSpeech => "PendulumBadgeSoundAndSpeechBrush",
                        _ => "PendulumBadgeSpeechOnlyBrush"
                    }), modeBorder.Background);
                    Assert.Same(Application.Current.FindResource(state switch
                    {
                        0 => "PendulumChipBrush",
                        1 => "PendulumBadgeDisabledBrush",
                        _ => "ControlFillColorSecondaryBrush"
                    }), statusBorder.Background);
                }
    }, theme);

    private static TextBlock Label(DependencyObject cell) => Assert.Single(Elements(cell).OfType<TextBlock>());

    private static DependencyObject Cell(TriggersPanel panel, ListViewItem row, string name)
    {
        var list = (ListView)panel.FindName("RemindersList");
        var index = ((GridView)list.View).Columns.IndexOf((GridViewColumn)panel.FindName(name));
        var presenter = Elements(row).OfType<GridViewRowPresenter>().Single();
        return VisualTreeHelper.GetChild(presenter, index);
    }

    private static void WithRow(Action<TriggersPanel, ListViewItem> check, ThemeMode? theme = null) => UiThread.Invoke(() =>
    {
        if (theme is { } selectedTheme) AppThemeManager.Apply(selectedTheme);
        var reminder = new TriggerTimer { Name = "Badge fixture", TriggerAt = new DateTime(2090, 1, 1) };
        AppServices.Instance.UseSettingsForTests(new AppSettings());
        AppServices.Instance.Reminders.Add(reminder);
        try
        {
            var panel = new TriggersPanel { Width = 1400, Height = 700 };
            panel.Measure(new Size(1400, 700));
            panel.Arrange(new Rect(0, 0, 1400, 700));
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            panel.UpdateLayout();
            var row = Elements(panel).OfType<ListViewItem>().Single(item => ReferenceEquals(item.DataContext, reminder));
            check(panel, row);
        }
        finally
        {
            AppServices.Instance.Reminders.Remove(reminder);
            Assert.Empty(Application.Current.Windows);
        }
    });

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
