using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pendulum.Core.Models;

public partial class TriggerTimer : ObservableObject
{
    [ObservableProperty] private Guid id = Guid.NewGuid();
    [ObservableProperty] private string name = "Timer";
    [ObservableProperty] private DateTime triggerAt = DateTime.Now;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private AlertMode mode = AlertMode.SoundOnly;
    [ObservableProperty] private string? soundFileName;
    [ObservableProperty] private string? phrase;
    [ObservableProperty] private int volume = 100;
    [ObservableProperty] private bool hasFired;

    /// The date/time originally set for this reminder — the anchor a recurrence pattern is computed from.
    /// TriggerAt moves forward on each recurrence; this stays fixed until the user re-edits the schedule.
    [ObservableProperty] private DateTime recurrenceAnchor = DateTime.Now;

    /// Null means this is a one-shot reminder with no repeat schedule.
    [ObservableProperty] private RecurrenceRule? recurrence;

    /// UI-only: whether this row is checked in the Reminders list's bulk-select mode.
    /// Never persisted.
    [ObservableProperty]
    [property: JsonIgnore]
    private bool isSelectedForBulk;

    /// UI-only: a stable, sortable proxy for Fixed (false) vs Scheduled (true) in the
    /// Reminders list — Recurrence itself is a reference type CollectionView sorting can't
    /// compare directly. Never persisted.
    [JsonIgnore]
    public bool IsScheduled => Recurrence is not null;

    /// UI-only: a stable, sortable proxy for the Reminders list's three-way status badge
    /// (0=Upcoming, 1=Disabled, 2=Spent — matches the order they'd naturally read left to
    /// right if laid out as a spectrum). Never persisted.
    [JsonIgnore]
    public int StatusSortOrder => HasFired ? 2 : (Enabled ? 0 : 1);

    partial void OnRecurrenceChanged(RecurrenceRule? value) => OnPropertyChanged(nameof(IsScheduled));
    partial void OnHasFiredChanged(bool value) => OnPropertyChanged(nameof(StatusSortOrder));
    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(StatusSortOrder));
}
