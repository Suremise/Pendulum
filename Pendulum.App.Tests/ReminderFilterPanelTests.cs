using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

/// The Reminders list's filter controls, bound to the one ReminderFilter: each opens as the
/// settings were saved, and a change made through it reaches the settings and the summaries.
///
/// No window is ever shown: the panel is built on the tests' UI thread and laid out in memory, and
/// the boxes are ticked through their UI Automation providers, not with keyboard or mouse input.
/// Nothing is saved: the settings are the test's own, in memory.
[Collection(nameof(SettingsScreen))]
public sealed class ReminderFilterPanelTests : IDisposable
{
    private readonly TriggerTimer _reminder = new() { Name = "Dentist", TriggerAt = new DateTime(2026, 3, 10, 9, 0, 0) };

    // The list (and its column headers, where the filters are) shows only with a reminder in it.
    public ReminderFilterPanelTests() => UiThread.Invoke(() => AppServices.Instance.Triggers.Add(_reminder));

    public void Dispose()
    {
        UiThread.Invoke(() =>
        {
            AppServices.Instance.Triggers.Remove(_reminder);
            Assert.Empty(Application.Current.Windows);
        });
        Assert.Equal(IntPtr.Zero, Process.GetCurrentProcess().MainWindowHandle);
    }

    [Fact]
    public void Every_filter_control_shows_its_saved_setting()
    {
        var settings = new AppSettings
        {
            ReminderFilterRowVisible = true,
            ReminderFilterName = "dent",
            ReminderFilterTypeScheduled = true,
            ReminderFilterStatusSpent = true,
            ReminderFilterModeSpeechOnly = true,
            ReminderFilterFrom = new DateTime(2026, 3, 1),
        };

        UiThread.Invoke(() =>
        {
            var panel = Build(settings);

            // Every binding to the filter finds its property: none has a broken path.
            var bound = FilterBindings(panel).ToList();
            Assert.Equal(16, bound.Count); // 1 name box, 8 boxes, 2 dates, 5 shown with the row
            Assert.All(bound, b => Assert.Equal(BindingStatus.Active, b.Expression.Status));

            Assert.Contains(bound, b => b.Path == "Filter.Name" && ((TextBox)b.Target).Text == "dent");
            Assert.Equal(
                ["Filter.ModeSpeechOnly", "Filter.StatusSpent", "Filter.TypeScheduled"],
                bound.Where(b => b.Target is CheckBox { IsChecked: true }).Select(b => b.Path).Order());
            Assert.Contains(bound, b => b.Path == "Filter.From" && ((DatePicker)b.Target).SelectedDate == new DateTime(2026, 3, 1));
            Assert.All(bound.Where(b => b.Path == "Filter.RowVisible"), b => Assert.Equal(Visibility.Visible, b.Target.Visibility));
        });
    }

    [Fact]
    public void Ticking_a_box_saves_it_and_updates_the_summary()
    {
        var settings = new AppSettings { ReminderFilterRowVisible = true };

        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            var vm = (TriggersViewModel)panel.DataContext;
            Assert.False(vm.HasActiveFilters);

            var box = (CheckBox)FilterBindings(panel).Single(b => b.Path == "Filter.ModeSoundOnly").Target;
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box).GetPattern(PatternInterface.Toggle);
            toggle.Toggle();

            Assert.True(settings.ReminderFilterModeSoundOnly);
            Assert.True(vm.HasActiveFilters);
            Assert.Equal(Pendulum.App.Resources.Strings.Reminders_SoundOnly, ((ToggleButton)panel.FindName("ModeFilterToggle")).Content);
        });
    }

    [Fact]
    public void The_Filters_button_shows_and_hides_the_row_and_saves_it()
    {
        var settings = new AppSettings();

        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            var vm = (TriggersViewModel)panel.DataContext;
            var typeToggle = (ToggleButton)panel.FindName("TypeFilterToggle");
            Assert.Equal(Visibility.Collapsed, typeToggle.Visibility);

            vm.ToggleFilterRowCommand.Execute(null);

            Assert.True(settings.ReminderFilterRowVisible);
            Assert.Equal(Visibility.Visible, typeToggle.Visibility);
        });
    }

    [Fact]
    public void Using_a_column_filter_never_changes_the_sort_but_clicking_the_header_does()
    {
        var settings = new AppSettings { ReminderFilterRowVisible = true };

        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            // Sorting is set up when the panel loads, which a panel never shown doesn't: by hand.
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var view = ((CollectionViewSource)panel.FindResource("SortedTriggers")).View;
            var arrow = (TextBlock)panel.FindName("TypeSortArrow");
            var sortBefore = view.SortDescriptions.Single();
            Assert.Equal(nameof(TriggerTimer.TriggerAt), sortBefore.PropertyName);
            Assert.Equal(string.Empty, arrow.Text);

            // The Type column's filter: its dropdown button and a box in its popup. A mouse click
            // on either raises Click, which bubbles to the column header; it's raised here as the
            // click would (the popup itself is never opened: it would be a window).
            var dropdown = (ToggleButton)panel.FindName("TypeFilterToggle");
            dropdown.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, dropdown));
            var box = (CheckBox)FilterBindings(panel).Single(b => b.Path == "Filter.TypeScheduled").Target;
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box).GetPattern(PatternInterface.Toggle)).Toggle();
            box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, box));
            box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, box));  // and unticked

            Assert.True(settings.ReminderFilterTypeScheduled);
            Assert.Equal(sortBefore, view.SortDescriptions.Single());
            Assert.Equal(string.Empty, arrow.Text);

            // The header itself still sorts, clicked through UI Automation.
            var header = Elements(panel).OfType<GridViewColumnHeader>().Single(h => (h.Tag as string) == nameof(TriggerTimer.IsScheduled));
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(header).GetPattern(PatternInterface.Invoke)).Invoke();

            Assert.Equal(new SortDescription(nameof(TriggerTimer.IsScheduled), ListSortDirection.Ascending), view.SortDescriptions.Single());
            Assert.Equal("▲", arrow.Text);
        });
    }

    [Fact]
    public void Each_filter_dropdown_stays_reachable_and_named_for_keyboard_and_screen_readers()
    {
        var settings = new AppSettings { ReminderFilterRowVisible = true };

        UiThread.Invoke(() =>
        {
            var panel = Build(settings);
            foreach (var name in new[] { "TypeFilterToggle", "DateFilterToggle", "ModeFilterToggle", "StatusFilterToggle" })
            {
                var dropdown = (ToggleButton)panel.FindName(name);
                Assert.True(dropdown.Focusable, name);
                Assert.True(System.Windows.Input.KeyboardNavigation.GetIsTabStop(dropdown), name);
                Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(dropdown).GetName()), name);
            }
        });
    }

    [Fact]
    public void A_reminders_on_off_button_shows_its_state_by_shape_and_name_not_colour_alone()
    {
        UiThread.Invoke(() =>
        {
            var panel = Build(new AppSettings());
            var style = (Style)panel.FindResource("EnableToggleButton");
            Button Toggle() => Elements(panel).OfType<Button>().Single(b => ReferenceEquals(b.Style, style));
            string Name(Button b) => UIElementAutomationPeer.CreatePeerForElement(b).GetName();

            // On: a tick, and named for what a click does.
            Assert.Equal("", Toggle().Content);
            Assert.Equal(Pendulum.App.Resources.Strings.Reminders_Disable, Name(Toggle()));
            Assert.Equal(Pendulum.App.Resources.Strings.Reminders_Disable, Toggle().ToolTip);

            try
            {
                _reminder.Enabled = false;
                panel.UpdateLayout();

                // Off: no tick, an edge to see it by, and the other name.
                Assert.Equal(string.Empty, Toggle().Content);
                Assert.Equal(1, Toggle().BorderThickness.Left);
                Assert.Equal(Pendulum.App.Resources.Strings.Reminders_Enable, Name(Toggle()));
                Assert.Equal(Pendulum.App.Resources.Strings.Reminders_Enable, Toggle().ToolTip);
            }
            finally
            {
                _reminder.Enabled = true;
            }
        });
    }

    [Fact]
    public void In_select_mode_each_checkbox_fits_its_column_whole_and_is_named_for_its_reminder()
    {
        UiThread.Invoke(() =>
        {
            var panel = Build(new AppSettings());
            var vm = (TriggersViewModel)panel.DataContext;
            try
            {
                vm.IsSelecting = true;
                panel.UpdateLayout();

                var box = Elements(panel).OfType<CheckBox>().Single(c => ReferenceEquals(c.DataContext, _reminder));
                var row = Elements(panel).OfType<GridViewRowPresenter>().Single(r => ReferenceEquals(r.Content, _reminder));
                var column = ((GridView)Elements(panel).OfType<ListView>().Single().View).Columns[0];
                var left = box.TranslatePoint(new Point(0, 0), row).X;

                // The whole box inside the select column, none of it cut off, with room after it.
                Assert.True(box.ActualWidth > 0);
                Assert.True(left >= 0, $"the box starts at {left}");
                Assert.True(left + box.ActualWidth + 4 <= column.ActualWidth,
                    $"the box ends at {left + box.ActualWidth} in a {column.ActualWidth}-wide column");
                Assert.True(box.ActualWidth <= box.DesiredSize.Width + 0.5, "the box is laid out wider than it draws");

                Assert.Equal(_reminder.Name, UIElementAutomationPeer.CreatePeerForElement(box).GetName());
            }
            finally
            {
                vm.IsSelecting = false;
            }
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    public void The_date_column_is_never_narrower_than_its_widest_date_and_time(string culture)
    {
        var settings = new AppSettings { Use24HourTime = false };

        UiThread.Invoke(() =>
        {
            var before = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            try
            {
                var panel = Build(settings);
                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));   // as when the tab first shows
                var list = (ListView)panel.FindName("RemindersList");
                var column = (GridViewColumn)panel.FindName("DateColumn");
                const double cellPaddingAndMargin = 6 + 6 + 5;

                // 12-hour: the widest date and time, AM/PM and all, fits.
                var widest = TriggersPanel.WidestDateText(list, use24Hour: false);
                Assert.True(column.Width >= widest + cellPaddingAndMargin,
                    $"the column is {column.Width} wide for a {widest}-wide date");

                // Dragged narrower: the width comes back to what the dates need.
                column.Width = 90;
                Assert.Equal(panel.DateColumnMinWidth, column.Width);
                Assert.True(column.Width >= widest + cellPaddingAndMargin);

                // 24-hour: measured again for that format, and still fits.
                settings.Use24HourTime = true;
                Assert.True(column.Width >= TriggersPanel.WidestDateText(list, use24Hour: true) + cellPaddingAndMargin);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        });
    }

    private static TriggersPanel Build(AppSettings settings)
    {
        AppServices.Instance.UseSettingsForTests(settings);
        var panel = new TriggersPanel { Width = 1100, Height = 700 };
        panel.Measure(new Size(panel.Width, panel.Height));
        panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
        panel.UpdateLayout();
        return panel;
    }

    private sealed record Bound(FrameworkElement Target, string Path, BindingExpression Expression);

    /// Each binding in the panel to a property of its ReminderFilter.
    private static IEnumerable<Bound> FilterBindings(DependencyObject root)
    {
        DependencyProperty[] properties =
        [
            TextBox.TextProperty, ToggleButton.IsCheckedProperty, DatePicker.SelectedDateProperty, UIElement.VisibilityProperty,
        ];

        foreach (var element in Elements(root).OfType<FrameworkElement>())
        {
            foreach (var property in properties)
            {
                if (BindingOperations.GetBindingExpression(element, property) is { ParentBinding.Path.Path: var path } expression
                    && path.StartsWith("Filter.", StringComparison.Ordinal))
                    yield return new Bound(element, path, expression);
            }
        }
    }

    /// Every element in the panel, through both trees: a popup's content is only in the logical one.
    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node))
                continue;
            yield return node;

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                stack.Push(child);
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }
}
