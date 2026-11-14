using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Pendulum.App.Converters;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.App.Views;

public partial class TriggersPanel : UserControl
{
    private bool _initialized;

    public TriggersPanel()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // This panel instance is reused across every Reminders-tab revisit (the tab control
            // keeps it alive rather than recreating it), so Loaded fires again on each revisit —
            // only wire sorting/filtering once, or every revisit stacks another duplicate
            // CollectionChanged/PropertyChanged subscription on top of the last.
            if (_initialized)
                return;
            _initialized = true;

            FitColumns();

            var width = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            foreach (var column in FittedColumns)
                width.AddValueChanged(column, (_, _) => KeepColumnWide(column));
            AppServices.Instance.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.Use24HourTime))
                    RefitDateColumn();
            };
            if (DataContext is TriggersViewModel vm)
            {
                WeakEventManager<ReminderStore, RemindersChangedEventArgs>.AddHandler(AppServices.Instance.Reminders,
                    nameof(ReminderStore.RemindersChanged), OnRemindersChanged);
                vm.Filter.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ReminderFilter.RowVisible))    // the headers' dropdowns
                        Dispatcher.BeginInvoke(RefitHeaders, System.Windows.Threading.DispatcherPriority.Loaded);
                };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TriggersViewModel.IsSelecting))  // the select column
                        Dispatcher.BeginInvoke(LayoutColumns, System.Windows.Threading.DispatcherPriority.Loaded);
                };
            }
        };
    }

    private void OnRemindersChanged(object? sender, RemindersChangedEventArgs e)
    {
        if (!(e.MembershipChanged || e.Properties.Contains(nameof(TriggerTimer.SoundFileName))) || _refitQueued) return;
        _refitQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refitQueued = false;
            RefitSoundColumn();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private bool _refitQueued;

    // ---- Rows: double-click or Enter edits, Delete deletes, a right-click menu offers both ----

    private TriggersViewModel? ViewModel => DataContext as TriggersViewModel;

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem row && OpenRow(row, e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    /// A double-click on a row edits its reminder, unless it was on one of the row's own buttons
    /// (the on/off button, Select mode's tick box).
    internal bool OpenRow(ListViewItem row, DependencyObject? clicked)
    {
        if (row.DataContext is not TriggerTimer reminder)
            return false;
        for (var d = clicked; d is not null && d != row; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase)
                return false;
        ViewModel?.EditCommand.Execute(reminder);
        return true;
    }

    private void RemindersList_KeyDown(object sender, KeyEventArgs e)
    {
        if (RowKey(e.Key, e.OriginalSource as DependencyObject))
            e.Handled = true;
    }

    /// Enter edits the selected reminder; Delete deletes it (or, in Select mode, the ticked ones),
    /// each asking first as before. Only from the rows: a key typed in the filter row's name box
    /// is the box's.
    internal bool RowKey(Key key, DependencyObject? source)
    {
        if (key is not (Key.Enter or Key.Delete) || source is null
            || ItemsControl.ContainerFromElement(RemindersList, source) is not ListViewItem row
            || row.DataContext is not TriggerTimer reminder)
            return false;

        if (key == Key.Enter)
            ViewModel?.EditCommand.Execute(reminder);
        else if (ViewModel is { IsSelecting: true, HasSelection: true } vm)
            vm.DeleteSelectedCommand.Execute(null);
        else
            ViewModel?.DeleteCommand.Execute(reminder);
        return true;
    }

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListViewItem row)
            PrepareRowMenu(row);
    }

    /// Sets a row's menu up for its reminder: its items act on it, and only "Turn off" (for one
    /// that's on) or "Turn on" (for one that's off) shows, greyed out for a spent reminder.
    internal void PrepareRowMenu(ListViewItem row)
    {
        if (row.ContextMenu is not { } menu || row.DataContext is not TriggerTimer reminder)
            return;
        menu.DataContext = reminder;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string tag)
                continue;
            var shown = tag == "TurnOff" ? reminder.Enabled : !reminder.Enabled;  // not user-visible: the items' tags
            item.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            item.IsEnabled = !reminder.HasFired;
        }
    }

    private static TriggerTimer? MenuReminder(object sender) => (sender as FrameworkElement)?.DataContext as TriggerTimer;

    private void RowEdit_Click(object sender, RoutedEventArgs e)
    {
        if (MenuReminder(sender) is { } reminder)
            ViewModel?.EditCommand.Execute(reminder);
    }

    private void RowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MenuReminder(sender) is { } reminder)
            ViewModel?.ToggleEnabledCommand.Execute(reminder);
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuReminder(sender) is { } reminder)
            ViewModel?.DeleteCommand.Execute(reminder);
    }

    // ---- Columns: Name takes the spare width, the others fit their widest content ----

    /// A cell's padding (6 each side) and room after its content.
    private const double CellChrome = 6 + 6 + 8;

    /// The narrowest Name may be before Sound, then Mode, step aside to leave it room.
    internal const double NameMinWidth = 160;

    private GridViewColumn[] FittedColumns => [TypeColumn, DateColumn, ModeColumn, SoundColumn, StatusColumn];

    /// Each fitted column's width: its header (with its filter, when the filter row shows) or its
    /// widest value, whichever is wider. Measured in the list's own fonts as it loads and whenever
    /// what it shows can change.
    private readonly Dictionary<GridViewColumn, double> _fitted = new();
    private readonly Dictionary<GridViewColumn, double> _contentWidths = new();
    private readonly Dictionary<(bool Use24, Typeface Font, double Size, FlowDirection Flow, double Dpi, string Culture), double> _dateWidths = new();
    private (bool Use24, Typeface Font, double Size, FlowDirection Flow, double Dpi, string Culture)? _measuredMetrics;

    private (bool, Typeface, double, FlowDirection, double, string) MeasurementKey(Control font) =>
        (AppServices.Instance.Settings.Use24HourTime,
            new Typeface(font.FontFamily, font.FontStyle, font.FontWeight, font.FontStretch), font.FontSize,
            font.FlowDirection, VisualTreeHelper.GetDpi(font).PixelsPerDip, CultureInfo.CurrentCulture.Name);

    /// The narrowest the date column may be: its widest date and time in the current format.
    internal double DateColumnMinWidth => _fitted.TryGetValue(DateColumn, out var w) ? w : 0;

    /// The width a fitted column needs for its content and header (0 before the list loads).
    internal double FittedWidth(GridViewColumn column) => _fitted.TryGetValue(column, out var w) ? w : 0;

    internal int DateMeasurementCount { get; private set; }
    internal int BadgeMeasurementCount { get; private set; }

    /// Whether Sound and Mode have stepped aside to leave Name its room (a narrow window).
    internal bool SoundStepsAside { get; private set; }
    internal bool ModeStepsAside { get; private set; }

    /// Measures every fitted column, then lays the columns out across the list's width.
    internal void FitColumns()
    {
        var modes = new EnumSpacedDisplayConverter();

        Fit(TypeColumn, Badges(Strings.Reminders_Fixed, Strings.Reminders_Scheduled));
        var font = RowFont();
        Fit(DateColumn, DateContentWidth(font));
        Fit(ModeColumn, Badges(Enum.GetValues<AlertMode>().Select(m => (string)modes.Convert(m, typeof(string), null!, CultureInfo.CurrentCulture)).ToArray()));
        Fit(SoundColumn, SoundContentWidth(font));
        Fit(StatusColumn, Badges(Strings.Reminders_Spent, Strings.Reminders_Upcoming, Strings.Reminders_Disabled));
        _measuredMetrics = MeasurementKey(font);
        LayoutColumns();
    }

    private double DateContentWidth(Control font)
    {
        var key = MeasurementKey(font);
        if (!_dateWidths.TryGetValue(key, out var width))
        {
            DateMeasurementCount++;
            _dateWidths[key] = width = WidestDateText(font, AppServices.Instance.Settings.Use24HourTime);
        }
        return width + 5;
    }

    private double SoundContentWidth(Control font)
    {
        var sounds = ViewModel?.Triggers.Select(t => t.SoundFileName).Where(n => !string.IsNullOrEmpty(n)).Cast<string>() ?? [];
        return Math.Min(SoundMaxWidth, sounds.Select(n => TextWidth(font, n)).DefaultIfEmpty(0).Max() + 6);
    }

    private void RefitDateColumn()
    {
        var font = RowFont();
        Fit(DateColumn, DateContentWidth(font));
        _measuredMetrics = MeasurementKey(font);
        LayoutColumns();
    }

    private void RefitSoundColumn()
    {
        var font = RowFont();
        if (_measuredMetrics is not { } metrics || !metrics.Equals(MeasurementKey(font))) { FitColumns(); return; }
        Fit(SoundColumn, SoundContentWidth(font));
        LayoutColumns();
    }

    private void RefitHeaders()
    {
        foreach (var column in FittedColumns)
            if (_contentWidths.TryGetValue(column, out var content)) Fit(column, content);
        LayoutColumns();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_initialized) FitColumns();
    }

    /// A long sound file name is cut short (its full name in a tooltip) past this.
    private const double SoundMaxWidth = 200;

    private void Fit(GridViewColumn column, double content)
    {
        _contentWidths[column] = content;
        var header = column.Header is FrameworkElement h ? Measured(h) : 0;
        _fitted[column] = Math.Ceiling(Math.Max(content + CellChrome, header));
    }

    private static double Measured(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Width;
    }

    // The widest of these badges, drawn as a row draws them (its style, padding and edge), and the
    // cell's own margin.
    private double Badges(params string[] labels)
    {
        BadgeMeasurementCount++;
        return labels.Max(text => Measured(new Border
        {
            Style = (Style)FindResource("TagBadge"),   // not user-visible: a resource key
            Child = new TextBlock { Text = text, Style = (Style)FindResource("TagBadgeText") },  // not user-visible
        })) + 6;
    }

    /// The font a row's text is drawn in: a row's own once there is one (its style can set a size
    /// of its own), the list's until then.
    private Control RowFont() =>
        RemindersList.Items.Count > 0 && RemindersList.ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem row ? row : RemindersList;

    private static double TextWidth(Control font, string text) =>
        new FormattedText(text, CultureInfo.CurrentCulture, font.FlowDirection,
            new Typeface(font.FontFamily, font.FontStyle, font.FontWeight, font.FontStretch),
            font.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(font).PixelsPerDip).WidthIncludingTrailingWhitespace;  // not a colour: the text is only measured

    private void RemindersList_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutColumns();

    // The rows' width changes with the list's, and when the vertical scrollbar comes or goes.
    private void RemindersList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0)
            LayoutColumns();
    }

    /// The fitted columns at their widths, the on/off button's at the right edge, and Name across
    /// the rest. In a window too narrow for Name's minimum, Sound steps aside, then Mode.
    internal void LayoutColumns()
    {
        if (_fitted.Count == 0)
            return;

        var available = ViewportWidth() - 2;   // a hair spare: no horizontal scrollbar from rounding
        var others = SelectColumnWidth() + ToggleColumn.Width;
        double Fixed(bool withoutSound, bool withoutMode) =>
            others + FittedColumns.Where(c => !(withoutSound && c == SoundColumn) && !(withoutMode && c == ModeColumn)).Sum(c => _fitted[c]);

        SoundStepsAside = available - Fixed(false, false) < NameMinWidth;
        ModeStepsAside = SoundStepsAside && available - Fixed(true, false) < NameMinWidth;

        _layingOut = true;
        try
        {
            foreach (var column in FittedColumns)
                column.Width = (column == SoundColumn && SoundStepsAside) || (column == ModeColumn && ModeStepsAside) ? 0 : _fitted[column];
            NameColumn.Width = Math.Max(NameMinWidth, Math.Floor(available - Fixed(SoundStepsAside, ModeStepsAside)));
        }
        finally
        {
            _layingOut = false;
        }
    }

    private bool _layingOut;

    private double SelectColumnWidth() =>
        ((GridView)RemindersList.View).Columns[0].Width is var w && !double.IsNaN(w) ? w : 0;

    /// The width the rows have: the list's own, less its vertical scrollbar when it shows (the
    /// columns are laid out again when it comes or goes: RemindersList_ScrollChanged).
    private double ViewportWidth()
    {
        var viewer = FindChild<ScrollViewer>(RemindersList);
        if (viewer is { ViewportWidth: > 0 })
            return viewer.ViewportWidth;
        var width = RemindersList.ActualWidth > 0 ? RemindersList.ActualWidth : RemindersList.Width;
        return double.IsNaN(width) ? 0 : width - SystemParameters.VerticalScrollBarWidth;
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
                return found;
            if (FindChild<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    // A GridViewColumn has no MinWidth: a drag narrower than a column's content is undone.
    private void KeepColumnWide(GridViewColumn column)
    {
        if (_layingOut || !_fitted.TryGetValue(column, out var min))
            return;
        var aside = (column == SoundColumn && SoundStepsAside) || (column == ModeColumn && ModeStepsAside);
        if (!aside && column.Width < min)
            column.Width = min;
    }

    /// The widest date and time the list can show, in the given control's font (a row's): every day of a year
    /// (so every day name with every month name, and two-digit days), at a two-digit hour in the
    /// morning and the evening (AM and PM).
    internal static double WidestDateText(Control list, bool use24Hour)
    {
        var typeface = new Typeface(list.FontFamily, list.FontStyle, list.FontWeight, list.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(list).PixelsPerDip;
        var widest = 0.0;
        for (var day = new DateTime(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            foreach (var hour in new[] { 10, 22 })
            {
                var text = DisplayFormat.DateAndTime(day.AddHours(hour).AddMinutes(58), use24Hour);
                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, list.FlowDirection, typeface, list.FontSize, Brushes.Black, pixelsPerDip);  // not a colour: only measured
                widest = Math.Max(widest, formatted.WidthIncludingTrailingWhitespace);
            }
        }
        return widest;
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        // Filter controls also bubble Click through their header; only its own click sorts.
        if (sender is GridViewColumnHeader { Tag: string propertyName }
            && ReferenceEquals(e.OriginalSource, sender))
            ViewModel?.SortByCommand.Execute(propertyName);
    }

    public void OpenAddTimerDialog()
    {
        (DataContext as TriggersViewModel)?.AddCommand.Execute(null);
    }
}
