using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

public partial class TriggersViewModel : ObservableObject
{
    public ObservableCollection<TriggerTimer> Triggers => AppServices.Instance.Triggers;

    public bool HasTriggers => Triggers.Count > 0;
    public bool HasSelection => Triggers.Any(t => t.IsSelectedForBulk);

    [ObservableProperty] private TriggerTimer? selectedTrigger;
    [ObservableProperty] private bool isSelecting;

    public string SelectToggleLabel => IsSelecting ? Strings.Reminders_Cancel : Strings.Reminders_Select;

    /// The list's filters, saved in the settings as they change.
    public ReminderFilter Filter { get; }

    /// Whether any filter is currently narrowing the list — drives the Filters button's
    /// active (blue + dot) styling, regardless of whether the filter row is expanded or
    /// collapsed.
    public bool HasActiveFilters => Filter.IsActive;

    public string TriggerFilterSummary => (Filter.From, Filter.To) switch
    {
        (null, null) => Strings.Filter_AnyDate,
        ({ } from, null) => string.Format(Strings.Filter_From, DisplayFormat.ShortDate(from)),
        (null, { } to) => string.Format(Strings.Filter_Until, DisplayFormat.ShortDate(to)),
        ({ } from, { } to) => string.Format(Strings.Filter_Range, DisplayFormat.ShortDate(from), DisplayFormat.ShortDate(to))
    };

    [RelayCommand]
    private void ClearTriggerFilter() => Filter.ClearDates();

    public string TypeFilterSummary => (Filter.TypeFixed, Filter.TypeScheduled) switch
    {
        (false, false) => Strings.Filter_AllTypes,
        (true, false) => Strings.Reminders_Fixed,
        (false, true) => Strings.Reminders_Scheduled,
        _ => Strings.Filter_Multiple
    };

    public string StatusFilterSummary
    {
        get
        {
            var active = new List<string>();
            if (Filter.StatusUpcoming) active.Add(Strings.Reminders_Upcoming);
            if (Filter.StatusSpent) active.Add(Strings.Reminders_Spent);
            if (Filter.StatusDisabled) active.Add(Strings.Reminders_Disabled);

            return active.Count switch
            {
                0 => Strings.Filter_AllStatus,
                1 => active[0],
                _ => Strings.Filter_Multiple
            };
        }
    }

    public string ModeFilterSummary => (Filter.ModeSoundOnly, Filter.ModeSoundAndSpeech, Filter.ModeSpeechOnly) switch
    {
        (false, false, false) => Strings.Filter_AllModes,
        (true, false, false) => Strings.Reminders_SoundOnly,
        (false, true, false) => Strings.Reminders_SoundSpeech,
        (false, false, true) => Strings.Reminders_SpeechOnly,
        _ => Strings.Filter_Multiple
    };

    [RelayCommand]
    private void ToggleFilterRow() => Filter.RowVisible = !Filter.RowVisible;

    /// Whether a reminder should be visible in the (view-only) filtered list — the underlying
    /// Triggers collection, selection state, and bulk actions are all unaffected by filtering.
    public bool PassesFilter(TriggerTimer t) => Filter.Matches(t);

    public TriggersViewModel()
    {
        Filter = new ReminderFilter(AppServices.Instance.Settings);
        Filter.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(TriggerFilterSummary));
            OnPropertyChanged(nameof(TypeFilterSummary));
            OnPropertyChanged(nameof(StatusFilterSummary));
            OnPropertyChanged(nameof(ModeFilterSummary));
        };

        Triggers.CollectionChanged += OnTriggersCollectionChanged;
        foreach (var t in Triggers)
            t.PropertyChanged += OnTriggerPropertyChanged;
    }

    private void OnTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTriggers));

        // Deleting selected items removes them from Triggers entirely rather than clearing
        // their IsSelectedForBulk flag, so OnTriggerPropertyChanged never fires for them —
        // without this, HasSelection (and the bulk Delete button's "active" styling bound to
        // it) stays stuck on whatever it was before the deletion.
        OnPropertyChanged(nameof(HasSelection));

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

    /// A new reminder, before the user edits it: its name from the resources (the model's own
    /// default is only for a saved reminder with no name), and the default sound.
    internal static TriggerTimer NewDraft(AppSettings settings) => new()
    {
        Name = Strings.Reminders_NewReminderName,
        SoundFileName = settings.DefaultSoundFileName,
    };

    [RelayCommand]
    private void Add()
    {
        var draft = NewDraft(AppServices.Instance.Settings);

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
            string.Format(Strings.Reminders_ConfirmDelete, trigger.Name),
            Strings.App_Name,
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

        if (trigger.Enabled)
        {
            trigger.Enabled = false;
            return;
        }

        // Back on: never for a time that went by while it was off.
        var message = EnableMessage(TriggerReconciler.Enable(trigger, DateTime.Now), trigger);
        if (message is not null)
            MessageBox.Show(Application.Current.MainWindow!, message, Strings.App_Name, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// What to tell the user after turning a reminder back on, or null when it simply went on.
    /// The time is shown as everywhere else, by the 12/24-hour setting (a caller can pass it).
    internal static string? EnableMessage(TriggerReconciler.EnableOutcome outcome, TriggerTimer trigger, bool? use24Hour = null) =>
        outcome switch
        {
            TriggerReconciler.EnableOutcome.PastOneOff =>
                string.Format(Strings.Reminders_EnablePastOneOff, trigger.Name, DisplayFormat.DateAndTime(trigger.TriggerAt, use24Hour)),
            TriggerReconciler.EnableOutcome.SeriesEnded => string.Format(Strings.Reminders_EnableSeriesEnded, trigger.Name),
            _ => null,
        };

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
            string.Format(Strings.Reminders_ConfirmDeleteSelected, selected.Count),
            Strings.App_Name,
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

        ExportReminders(selected);
    }

    /// File > Export…: the reminders ticked in Select mode, or every reminder when none is.
    [RelayCommand]
    private void Export()
    {
        var chosen = Triggers.Where(t => t.IsSelectedForBulk).ToList();
        if (chosen.Count == 0)
            chosen = Triggers.ToList();
        if (chosen.Count == 0)
            return;

        ExportReminders(chosen);
    }

    private void ExportReminders(List<TriggerTimer> selected)
    {
        var dialog = new SaveFileDialog
        {
            Filter = Strings.Dialog_RemindersFilter,
            FileName = $"pendulum-reminders-{DateTime.Now:yyyy-MM-dd}.json",
            Title = Strings.Dialog_ExportRemindersTitle
        };

        if (dialog.ShowDialog() != true)
            return;

        AppServices.Instance.ExportTriggers(dialog.FileName, selected);
        IsSelecting = false;

        MessageBox.Show(
            Application.Current.MainWindow!,
            string.Format(Strings.Reminders_Exported, selected.Count, System.IO.Path.GetFileName(dialog.FileName)),
            Strings.App_Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = Strings.Dialog_RemindersFilter,
            Title = Strings.Dialog_ImportRemindersTitle
        };

        if (dialog.ShowDialog() != true)
            return;

        ImportResult result;
        try
        {
            result = AppServices.Instance.ImportTriggersMerge(dialog.FileName, name =>
                MessageBox.Show(
                    Application.Current.MainWindow!,
                    string.Format(Strings.Reminders_ConfirmReplace, name),
                    Strings.App_Name,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current.MainWindow!,
                string.Format(Strings.Reminders_CouldNotImport, ex.Message),
                Strings.App_Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var summary = string.Format(Strings.Reminders_Imported, result.Added + result.Replaced);
        if (result.Replaced > 0)
            summary += string.Format(Strings.Reminders_ImportedReplaced, result.Replaced);
        if (result.Skipped > 0)
            summary += string.Format(Strings.Reminders_ImportedSkipped, result.Skipped);
        summary += ".";
        if (result.CaughtUp > 0)
            summary += string.Format(Strings.Reminders_ImportedCaughtUp, result.CaughtUp);

        MessageBox.Show(
            Application.Current.MainWindow!,
            summary,
            Strings.App_Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
