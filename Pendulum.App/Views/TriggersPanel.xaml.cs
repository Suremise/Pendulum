using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Pendulum.App.Converters;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.ViewModels;
using Pendulum.Core.Models;

namespace Pendulum.App.Views;

public partial class TriggersPanel : UserControl
{
    private ICollectionView? _view;
    private bool _initialized;
    private string _sortProperty = nameof(TriggerTimer.TriggerAt);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

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

            EnableLiveSorting();
            SetupFiltering();
            ApplySort();
            FitColumns();

            var width = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            foreach (var column in FittedColumns)
                width.AddValueChanged(column, (_, _) => KeepColumnWide(column));
            AppServices.Instance.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.Use24HourTime))
                    FitColumns();
            };
            if (DataContext is TriggersViewModel vm)
            {
                // The longest sound's name: as reminders come and go, and as one's sound is changed
                // (an edit changes the reminder in place).
                foreach (var reminder in vm.Triggers)
                    Watch(reminder);
                vm.Triggers.CollectionChanged += Reminders_CollectionChanged;
                vm.Filter.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ReminderFilter.RowVisible))    // the headers' dropdowns
                        Dispatcher.BeginInvoke(FitColumns, System.Windows.Threading.DispatcherPriority.Loaded);
                };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TriggersViewModel.IsSelecting))  // the select column
                        Dispatcher.BeginInvoke(LayoutColumns, System.Windows.Threading.DispatcherPriority.Loaded);
                };
            }
        };

        EditRow = t => (DataContext as TriggersViewModel)?.EditCommand.Execute(t);
        DeleteRow = t => (DataContext as TriggersViewModel)?.DeleteCommand.Execute(t);
    }

    // The reminders whose sound is watched: every one in the list, each once.
    private readonly HashSet<TriggerTimer> _watched = new();

    private void Watch(TriggerTimer reminder)
    {
        if (_watched.Add(reminder))
            reminder.PropertyChanged += Reminder_PropertyChanged;
    }

    private void Unwatch(TriggerTimer reminder)
    {
        if (_watched.Remove(reminder))
            reminder.PropertyChanged -= Reminder_PropertyChanged;
    }

    private void Reminders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Compared with the list as it is now, so a reset (Restore, Import) is handled as well as
        // an add or a remove.
        var current = ViewModel?.Triggers.ToHashSet() ?? [];
        foreach (var gone in _watched.Where(r => !current.Contains(r)).ToList())
            Unwatch(gone);
        foreach (var reminder in current)
            Watch(reminder);
        FitColumns();
    }

    private void Reminder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TriggerTimer.SoundFileName) && !_refitQueued)
        {
            _refitQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _refitQueued = false;
                FitColumns();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private bool _refitQueued;

    // ---- Rows: double-click or Enter edits, Delete deletes, a right-click menu offers both ----

    /// What editing and deleting a row do: the view model's commands (each opens a window of its
    /// own, the editor or a confirmation). The tests put recorders here instead.
    internal Action<TriggerTimer> EditRow { get; set; }
    internal Action<TriggerTimer> DeleteRow { get; set; }

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
        EditRow(reminder);
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
            EditRow(reminder);
        else if (ViewModel is { IsSelecting: true, HasSelection: true } vm)
            vm.DeleteSelectedCommand.Execute(null);
        else
            DeleteRow(reminder);
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
            EditRow(reminder);
    }

    private void RowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MenuReminder(sender) is { } reminder)
            ViewModel?.ToggleEnabledCommand.Execute(reminder);
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MenuReminder(sender) is { } reminder)
            DeleteRow(reminder);
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

    /// The narrowest the date column may be: its widest date and time in the current format.
    internal double DateColumnMinWidth => _fitted.TryGetValue(DateColumn, out var w) ? w : 0;

    /// The width a fitted column needs for its content and header (0 before the list loads).
    internal double FittedWidth(GridViewColumn column) => _fitted.TryGetValue(column, out var w) ? w : 0;

    /// Whether Sound and Mode have stepped aside to leave Name its room (a narrow window).
    internal bool SoundStepsAside { get; private set; }
    internal bool ModeStepsAside { get; private set; }

    /// Measures every fitted column, then lays the columns out across the list's width.
    internal void FitColumns()
    {
        var use24 = AppServices.Instance.Settings.Use24HourTime;
        var modes = new EnumSpacedDisplayConverter();
        var sounds = ViewModel?.Triggers.Select(t => t.SoundFileName).Where(n => !string.IsNullOrEmpty(n)).Cast<string>() ?? [];

        Fit(TypeColumn, Badges(Strings.Reminders_Fixed, Strings.Reminders_Scheduled));
        var font = RowFont();
        Fit(DateColumn, WidestDateText(font, use24) + 5);
        Fit(ModeColumn, Badges(Enum.GetValues<AlertMode>().Select(m => (string)modes.Convert(m, typeof(string), null!, CultureInfo.CurrentCulture)).ToArray()));
        Fit(SoundColumn, Math.Min(SoundMaxWidth, sounds.Select(n => TextWidth(font, n)).DefaultIfEmpty(0).Max() + 6));
        Fit(StatusColumn, Badges(Strings.Reminders_Spent, Strings.Reminders_Upcoming, Strings.Reminders_Disabled));
        LayoutColumns();
    }

    /// A long sound file name is cut short (its full name in a tooltip) past this.
    private const double SoundMaxWidth = 200;

    private void Fit(GridViewColumn column, double content)
    {
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
    private double Badges(params string[] labels) =>
        labels.Max(text => Measured(new Border
        {
            Style = (Style)FindResource("TagBadge"),   // not user-visible: a resource key
            Child = new TextBlock { Text = text, Style = (Style)FindResource("TagBadgeText") },  // not user-visible
        })) + 6;

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

    private void EnableLiveSorting()
    {
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)  // not user-visible: a resource key
            return;

        if (cvs.View is ICollectionViewLiveShaping liveShaping && liveShaping.CanChangeLiveSorting)
        {
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.Name));
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.IsScheduled));
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.TriggerAt));
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.Mode));
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.SoundFileName));
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.StatusSortOrder));
            liveShaping.IsLiveSorting = true;

            if (liveShaping.CanChangeLiveFiltering)
            {
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Name));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.HasFired));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Enabled));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Recurrence));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Mode));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.TriggerAt));
                liveShaping.IsLiveFiltering = true;
            }
        }
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string propertyName })
            return;

        // Only the header's own click sorts. The filter in the header is made of buttons too (the
        // dropdown's toggle, the checkboxes in its popup, whose events route through the popup to
        // the header), and their Click bubbles up here: choosing a filter must not re-sort.
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;

        _sortDirection = _sortProperty == propertyName && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortProperty = propertyName;

        ApplySort();
    }

    private void ApplySort()
    {
        if (_view is null)
            return;

        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
        UpdateSortArrows();
    }

    private void UpdateSortArrows()
    {
        var arrow = _sortDirection == ListSortDirection.Ascending ? "▲" : "▼";
        NameSortArrow.Text = _sortProperty == nameof(TriggerTimer.Name) ? arrow : string.Empty;
        TypeSortArrow.Text = _sortProperty == nameof(TriggerTimer.IsScheduled) ? arrow : string.Empty;
        TriggersOnSortArrow.Text = _sortProperty == nameof(TriggerTimer.TriggerAt) ? arrow : string.Empty;
        ModeSortArrow.Text = _sortProperty == nameof(TriggerTimer.Mode) ? arrow : string.Empty;
        SoundSortArrow.Text = _sortProperty == nameof(TriggerTimer.SoundFileName) ? arrow : string.Empty;
        StatusSortArrow.Text = _sortProperty == nameof(TriggerTimer.StatusSortOrder) ? arrow : string.Empty;
    }

    private void SetupFiltering()
    {
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)  // not user-visible: a resource key
            return;

        _view = cvs.View;
        _view.Filter = FilterItem;
        _view.CollectionChanged += (_, __) => UpdateNoResultsVisibility();
        UpdateNoResultsVisibility();

        if (DataContext is TriggersViewModel vm)
        {
            vm.Filter.Changed += (_, _) => _view?.Refresh();
        }
    }

    private bool FilterItem(object obj) =>
        DataContext is not TriggersViewModel vm || obj is not TriggerTimer t || vm.PassesFilter(t);

    private void UpdateNoResultsVisibility()
    {
        if (_view is null || DataContext is not TriggersViewModel vm)
            return;

        var isEmpty = !_view.Cast<object>().Any();
        NoFilterResultsPanel.Visibility = vm.HasTriggers && isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public void OpenAddTimerDialog()
    {
        (DataContext as TriggersViewModel)?.AddCommand.Execute(null);
    }
}
