using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
            FitDateColumn();
            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn))
                .AddValueChanged(DateColumn, (_, _) => KeepDateColumnWide());
            AppServices.Instance.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.Use24HourTime))
                    FitDateColumn();
            };
        };
    }

    // ---- The date column: never narrower than its widest date and time ----

    /// The width the XAML gives the date column, kept when the dates fit in it.
    private const double DateColumnDefaultWidth = 177;

    /// A cell's padding (6 each side), the date's own left margin (5) and room after it (8).
    private const double DateCellChrome = 6 + 6 + 5 + 8;

    private double _dateColumnAutoWidth = DateColumnDefaultWidth;

    /// The narrowest the date column may be: its widest date and time in the current format.
    internal double DateColumnMinWidth { get; private set; }

    /// Sizes the date column for the widest date and time the current format can show (the
    /// culture's day and month names, 12- or 24-hour), and widens it if the dates don't fit. A
    /// width the user dragged wider is kept.
    internal void FitDateColumn()
    {
        DateColumnMinWidth = Math.Ceiling(WidestDateText(RemindersList, AppServices.Instance.Settings.Use24HourTime) + DateCellChrome);
        var auto = Math.Max(DateColumnDefaultWidth, DateColumnMinWidth);
        var dragged = Math.Abs(DateColumn.Width - _dateColumnAutoWidth) > 0.5;
        _dateColumnAutoWidth = auto;
        DateColumn.Width = dragged ? Math.Max(DateColumn.Width, DateColumnMinWidth) : auto;
    }

    // A GridViewColumn has no MinWidth: a drag narrower than the dates is undone.
    private void KeepDateColumnWide()
    {
        if (DateColumn.Width < DateColumnMinWidth)
            DateColumn.Width = DateColumnMinWidth;
    }

    /// The widest date and time the list can show, in the list's own font: every day of a year
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
                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, list.FlowDirection, typeface, list.FontSize, Brushes.Black, pixelsPerDip);
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
