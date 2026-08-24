using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public partial class TriggersViewModel : ObservableObject
{
    public ObservableCollection<TriggerTimer> Triggers => AppServices.Instance.Triggers;

    public bool HasTriggers => Triggers.Count > 0;
    public bool HasSelection => Triggers.Any(t => t.IsSelectedForBulk);

    [ObservableProperty] private TriggerTimer? selectedTrigger;
    [ObservableProperty] private bool isSelecting;

    public string SelectToggleLabel => IsSelecting ? "Cancel" : "Select";

    [ObservableProperty] private bool isFilterRowVisible;
    [ObservableProperty] private string nameFilter = string.Empty;

    [ObservableProperty] private bool typeFilterFixed;
    [ObservableProperty] private bool typeFilterScheduled;

    [ObservableProperty] private bool statusFilterUpcoming;
    [ObservableProperty] private bool statusFilterSpent;

    [ObservableProperty] private bool modeFilterSoundOnly;
    [ObservableProperty] private bool modeFilterSoundAndSpeech;
    [ObservableProperty] private bool modeFilterSpeechOnly;

    [ObservableProperty] private DateTime? triggerFromFilter;
    [ObservableProperty] private DateTime? triggerToFilter;

    private bool HasActiveTypeFilter => TypeFilterFixed || TypeFilterScheduled;
    private bool HasActiveStatusFilter => StatusFilterUpcoming || StatusFilterSpent;
    private bool HasActiveModeFilter => ModeFilterSoundOnly || ModeFilterSoundAndSpeech || ModeFilterSpeechOnly;

    /// Whether any filter is currently narrowing the list — drives the Filters button's
    /// active (blue + dot) styling, regardless of whether the filter row is expanded or
    /// collapsed.
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || HasActiveTypeFilter || HasActiveStatusFilter || HasActiveModeFilter
        || TriggerFromFilter is not null || TriggerToFilter is not null;

    private void NotifyFilterActivityChanged() => OnPropertyChanged(nameof(HasActiveFilters));

    public string TriggerFilterSummary => (TriggerFromFilter, TriggerToFilter) switch
    {
        (null, null) => "Any date",
        ({ } from, null) => $"From {from:dd MMM}",
        (null, { } to) => $"Until {to:dd MMM}",
        ({ } from, { } to) => $"{from:dd MMM} – {to:dd MMM}"
    };

    partial void OnTriggerFromFilterChanged(DateTime? value)
    {
        AppServices.Instance.Settings.ReminderFilterFrom = value;
        OnPropertyChanged(nameof(TriggerFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnTriggerToFilterChanged(DateTime? value)
    {
        AppServices.Instance.Settings.ReminderFilterTo = value;
        OnPropertyChanged(nameof(TriggerFilterSummary));
        NotifyFilterActivityChanged();
    }

    [RelayCommand]
    private void ClearTriggerFilter()
    {
        TriggerFromFilter = null;
        TriggerToFilter = null;
    }

    public string TypeFilterSummary => (TypeFilterFixed, TypeFilterScheduled) switch
    {
        (false, false) => "All types",
        (true, false) => "Fixed",
        (false, true) => "Scheduled",
        _ => "Multiple"
    };

    public string StatusFilterSummary => (StatusFilterUpcoming, StatusFilterSpent) switch
    {
        (false, false) => "All status",
        (true, false) => "Upcoming",
        (false, true) => "Spent",
        _ => "Multiple"
    };

    public string ModeFilterSummary => (ModeFilterSoundOnly, ModeFilterSoundAndSpeech, ModeFilterSpeechOnly) switch
    {
        (false, false, false) => "All modes",
        (true, false, false) => "Sound only",
        (false, true, false) => "Sound + speech",
        (false, false, true) => "Speech only",
        _ => "Multiple"
    };

    partial void OnTypeFilterFixedChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterTypeFixed = value;
        OnPropertyChanged(nameof(TypeFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnTypeFilterScheduledChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterTypeScheduled = value;
        OnPropertyChanged(nameof(TypeFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnStatusFilterUpcomingChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterStatusUpcoming = value;
        OnPropertyChanged(nameof(StatusFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnStatusFilterSpentChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterStatusSpent = value;
        OnPropertyChanged(nameof(StatusFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnModeFilterSoundOnlyChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterModeSoundOnly = value;
        OnPropertyChanged(nameof(ModeFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnModeFilterSoundAndSpeechChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterModeSoundAndSpeech = value;
        OnPropertyChanged(nameof(ModeFilterSummary));
        NotifyFilterActivityChanged();
    }

    partial void OnModeFilterSpeechOnlyChanged(bool value)
    {
        AppServices.Instance.Settings.ReminderFilterModeSpeechOnly = value;
        OnPropertyChanged(nameof(ModeFilterSummary));
        NotifyFilterActivityChanged();
    }

    [RelayCommand]
    private void ToggleFilterRow() => IsFilterRowVisible = !IsFilterRowVisible;

    partial void OnIsFilterRowVisibleChanged(bool value) =>
        AppServices.Instance.Settings.ReminderFilterRowVisible = value;

    partial void OnNameFilterChanged(string value)
    {
        AppServices.Instance.Settings.ReminderFilterName = value;
        NotifyFilterActivityChanged();
    }

    /// Whether a reminder should be visible in the (view-only) filtered list — the underlying
    /// Triggers collection, selection state, and bulk actions are all unaffected by filtering.
    public bool PassesFilter(TriggerTimer t)
    {
        if (!string.IsNullOrWhiteSpace(NameFilter) && t.Name.IndexOf(NameFilter, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (HasActiveTypeFilter)
        {
            var isScheduled = t.Recurrence is not null;
            var isFixed = t.Recurrence is null;

            if (!((TypeFilterScheduled && isScheduled) || (TypeFilterFixed && isFixed)))
                return false;
        }

        if (HasActiveStatusFilter)
        {
            if (!((StatusFilterUpcoming && !t.HasFired) || (StatusFilterSpent && t.HasFired)))
                return false;
        }

        if (HasActiveModeFilter)
        {
            if (!((ModeFilterSoundOnly && t.Mode == AlertMode.SoundOnly)
                  || (ModeFilterSoundAndSpeech && t.Mode == AlertMode.SoundAndSpeech)
                  || (ModeFilterSpeechOnly && t.Mode == AlertMode.SpeechOnly)))
                return false;
        }

        if (TriggerFromFilter is not null && t.TriggerAt.Date < TriggerFromFilter.Value.Date)
            return false;

        if (TriggerToFilter is not null && t.TriggerAt.Date > TriggerToFilter.Value.Date)
            return false;

        return true;
    }

    public TriggersViewModel()
    {
        // Restore the last-used filter state directly via the backing fields, bypassing the
        // generated setters (and their On*Changed side effects) since there's nothing new to
        // persist or notify about here — we're just loading what was already saved.
        var settings = AppServices.Instance.Settings;
        isFilterRowVisible = settings.ReminderFilterRowVisible;
        nameFilter = settings.ReminderFilterName;
        typeFilterFixed = settings.ReminderFilterTypeFixed;
        typeFilterScheduled = settings.ReminderFilterTypeScheduled;
        statusFilterUpcoming = settings.ReminderFilterStatusUpcoming;
        statusFilterSpent = settings.ReminderFilterStatusSpent;
        modeFilterSoundOnly = settings.ReminderFilterModeSoundOnly;
        modeFilterSoundAndSpeech = settings.ReminderFilterModeSoundAndSpeech;
        modeFilterSpeechOnly = settings.ReminderFilterModeSpeechOnly;
        triggerFromFilter = settings.ReminderFilterFrom;
        triggerToFilter = settings.ReminderFilterTo;

        Triggers.CollectionChanged += OnTriggersCollectionChanged;
        foreach (var t in Triggers)
            t.PropertyChanged += OnTriggerPropertyChanged;
    }

    private void OnTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTriggers));

        if (e.OldItems is not null)
            foreach (TriggerTimer t in e.OldItems)
                t.PropertyChanged -= OnTriggerPropertyChanged;
        if (e.NewItems is not null)
            foreach (TriggerTimer t in e.NewItems)
                t.PropertyChanged += OnTriggerPropertyChanged;
    }

    private void OnTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TriggerTimer.IsSelectedForBulk))
            OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnIsSelectingChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectToggleLabel));
        if (!value)
        {
            foreach (var t in Triggers)
                t.IsSelectedForBulk = false;
        }
    }

    [RelayCommand]
    private void Add()
    {
        var draft = new TriggerTimer
        {
            SoundFileName = AppServices.Instance.Settings.DefaultSoundFileName
        };

        var dialog = new TriggerEditWindow(draft, isNew: true) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            AppServices.Instance.AddTrigger(dialog.Result);
            SelectedTrigger = dialog.Result;
        }
    }

    [RelayCommand]
    private void Edit(TriggerTimer? trigger)
    {
        trigger ??= SelectedTrigger;
        if (trigger is null)
            return;

        var dialog = new TriggerEditWindow(trigger, isNew: false) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
            AppServices.Instance.RefreshTriggers();
    }

    [RelayCommand]
    private void Delete(TriggerTimer? trigger)
    {
        trigger ??= SelectedTrigger;
        if (trigger is null)
            return;

        var result = MessageBox.Show(
            Application.Current.MainWindow!,
            $"Delete \"{trigger.Name}\"?",
            "Pendulum",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            AppServices.Instance.RemoveTrigger(trigger);
    }

    [RelayCommand]
    private void ToggleEnabled(TriggerTimer? trigger)
    {
        if (trigger is null || trigger.HasFired)
            return;

        trigger.Enabled = !trigger.Enabled;
    }

    [RelayCommand]
    private void ToggleSelect() => IsSelecting = !IsSelecting;

    [RelayCommand]
    private void DeleteSelected()
    {
        var selected = Triggers.Where(t => t.IsSelectedForBulk).ToList();
        if (selected.Count == 0)
            return;

        var result = MessageBox.Show(
            Application.Current.MainWindow!,
            $"Delete {selected.Count} selected reminder(s)? This cannot be undone.",
            "Pendulum",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        AppServices.Instance.RemoveTriggers(selected);
        IsSelecting = false;
    }

    [RelayCommand]
    private void ExportSelected()
    {
        var selected = Triggers.Where(t => t.IsSelectedForBulk).ToList();
        if (selected.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "Pendulum reminders (*.json)|*.json",
            FileName = $"pendulum-reminders-{DateTime.Now:yyyy-MM-dd}.json",
            Title = "Export reminders"
        };

        if (dialog.ShowDialog() != true)
            return;

        AppServices.Instance.ExportTriggers(dialog.FileName, selected);
        IsSelecting = false;

        MessageBox.Show(
            Application.Current.MainWindow!,
            $"Exported {selected.Count} reminder(s) to {System.IO.Path.GetFileName(dialog.FileName)}.",
            "Pendulum",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Pendulum reminders (*.json)|*.json",
            Title = "Import reminders"
        };

        if (dialog.ShowDialog() != true)
            return;

        ImportResult result;
        try
        {
            result = AppServices.Instance.ImportTriggersMerge(dialog.FileName, name =>
                MessageBox.Show(
                    Application.Current.MainWindow!,
                    $"A reminder named \"{name}\" already exists. Replace it with the imported version?",
                    "Pendulum",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current.MainWindow!,
                $"Couldn't read that file as Pendulum reminders.\n\n{ex.Message}",
                "Pendulum",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var summary = $"Imported {result.Added + result.Replaced} reminder(s)";
        if (result.Replaced > 0)
            summary += $" ({result.Replaced} replaced)";
        if (result.Skipped > 0)
            summary += $", skipped {result.Skipped}";
        summary += ".";

        MessageBox.Show(
            Application.Current.MainWindow!,
            summary,
            "Pendulum",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
