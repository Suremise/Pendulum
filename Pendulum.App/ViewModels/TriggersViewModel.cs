using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Services;

namespace Pendulum.App.ViewModels;

public partial class TriggersViewModel : ObservableObject
{
    private readonly IDialogs _dialogs;

    private ReminderStore Store => AppServices.Instance.Reminders;
    public ReadOnlyObservableCollection<TriggerTimer> Triggers => Store.Items;

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

    public TriggersViewModel() : this(WpfDialogs.Instance) { }

    public TriggersViewModel(IDialogs dialogs)
    {
        _dialogs = dialogs;
        Filter = new ReminderFilter(AppServices.Instance.Settings);
        Filter.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(TriggerFilterSummary));
            OnPropertyChanged(nameof(TypeFilterSummary));
            OnPropertyChanged(nameof(StatusFilterSummary));
            OnPropertyChanged(nameof(ModeFilterSummary));
        };

        WeakEventManager<ReminderStore, RemindersChangedEventArgs>.AddHandler(Store, nameof(ReminderStore.RemindersChanged), OnRemindersChanged);
    }

    private void OnRemindersChanged(object? sender, RemindersChangedEventArgs e)
    {
        if (e.MembershipChanged) OnPropertyChanged(nameof(HasTriggers));
        if (e.MembershipChanged || e.Properties.Contains(nameof(TriggerTimer.IsSelectedForBulk)))
            OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnIsSelectingChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectToggleLabel));
        if (!value)
        {
            Store.ChangeTogether(() =>
            {
                foreach (var t in Triggers) t.IsSelectedForBulk = false;
            });
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

        if (_dialogs.EditReminder(draft, isNew: true) is { } result)
        {
            AppServices.Instance.AddTrigger(result);
            SelectedTrigger = result;
        }
    }

    [RelayCommand]
    private void Edit(TriggerTimer? trigger)
    {
        trigger ??= SelectedTrigger;
        if (trigger is null)
            return;

        _dialogs.EditReminder(trigger);
    }

    [RelayCommand]
    private void Delete(TriggerTimer? trigger)
    {
        trigger ??= SelectedTrigger;
        if (trigger is null)
            return;

        if (_dialogs.Confirm(string.Format(Strings.Reminders_ConfirmDelete, trigger.Name)))
            AppServices.Instance.RemoveTrigger(trigger);
    }

    [RelayCommand]
    private void ToggleEnabled(TriggerTimer? trigger)
    {
        if (trigger is null || trigger.HasFired)
            return;

        if (trigger.Enabled)
        {
            AppServices.Instance.Update(trigger, () => trigger.Enabled = false);
            return;
        }

        // Back on: never for a time that went by while it was off.
        var outcome = TriggerReconciler.EnableOutcome.Enabled;
        if (!AppServices.Instance.Update(trigger, () => outcome = TriggerReconciler.Enable(trigger, DateTime.Now))) return;
        var message = EnableMessage(outcome, trigger);
        if (message is not null)
            _dialogs.Inform(message);
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

        if (!_dialogs.Confirm(string.Format(Strings.Reminders_ConfirmDeleteSelected, selected.Count), warning: true))
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
        var path = _dialogs.PickSaveFile(Strings.Dialog_ExportRemindersTitle, Strings.Dialog_RemindersFilter,
            $"pendulum-reminders-{DateTime.Now:yyyy-MM-dd}.json"); // not user-visible: suggested file name
        if (path is null)
            return;

        AppServices.Instance.ExportTriggers(path, selected);
        IsSelecting = false;
        _dialogs.Inform(string.Format(Strings.Reminders_Exported, selected.Count, System.IO.Path.GetFileName(path)));
    }

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.PickOpenFile(Strings.Dialog_ImportRemindersTitle, Strings.Dialog_RemindersFilter);
        if (path is null)
            return;

        ImportResult result;
        try
        {
            result = AppServices.Instance.ImportTriggersMerge(path, name =>
                _dialogs.Confirm(string.Format(Strings.Reminders_ConfirmReplace, name)));
        }
        catch (Exception ex)
        {
            _dialogs.Warn(string.Format(Strings.Reminders_CouldNotImport, ex.Message), error: true);
            return;
        }

        _dialogs.Inform(string.Format(Strings.Reminders_Imported, result.Added + result.Replaced,
            result.Replaced > 0 ? string.Format(Strings.Reminders_ImportedReplaced, result.Replaced) : string.Empty,
            result.Skipped > 0 ? string.Format(Strings.Reminders_ImportedSkipped, result.Skipped) : string.Empty,
            result.CaughtUp > 0 ? string.Format(Strings.Reminders_ImportedCaughtUp, result.CaughtUp) : string.Empty));
    }
}
