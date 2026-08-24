using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        };
    }

    private void EnableLiveSorting()
    {
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)
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
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)
            return;

        _view = cvs.View;
        _view.Filter = FilterItem;
        _view.CollectionChanged += (_, __) => UpdateNoResultsVisibility();
        UpdateNoResultsVisibility();

        if (DataContext is TriggersViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TriggersViewModel.NameFilter)
                    or nameof(TriggersViewModel.TypeFilterFixed)
                    or nameof(TriggersViewModel.TypeFilterScheduled)
                    or nameof(TriggersViewModel.StatusFilterUpcoming)
                    or nameof(TriggersViewModel.StatusFilterSpent)
                    or nameof(TriggersViewModel.StatusFilterDisabled)
                    or nameof(TriggersViewModel.ModeFilterSoundOnly)
                    or nameof(TriggersViewModel.ModeFilterSoundAndSpeech)
                    or nameof(TriggersViewModel.ModeFilterSpeechOnly)
                    or nameof(TriggersViewModel.TriggerFromFilter)
                    or nameof(TriggersViewModel.TriggerToFilter))
                    _view?.Refresh();
            };
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
