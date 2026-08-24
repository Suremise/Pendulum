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
        };
    }

    private void EnableLiveSorting()
    {
        if (FindResource("SortedTriggers") is not CollectionViewSource cvs)
            return;

        if (cvs.View is ICollectionViewLiveShaping liveShaping && liveShaping.CanChangeLiveSorting)
        {
            liveShaping.LiveSortingProperties.Add(nameof(TriggerTimer.TriggerAt));
            liveShaping.IsLiveSorting = true;

            if (liveShaping.CanChangeLiveFiltering)
            {
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Name));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.HasFired));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Recurrence));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.Mode));
                liveShaping.LiveFilteringProperties.Add(nameof(TriggerTimer.TriggerAt));
                liveShaping.IsLiveFiltering = true;
            }
        }
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
