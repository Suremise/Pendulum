using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Models;

public partial class TriggerTimer : ObservableObject, IJsonOnDeserializing, IJsonOnDeserialized
{
    [ObservableProperty] private Guid id = Guid.NewGuid();
    /// "Timer" only for a saved reminder that has no name; a new one is named by the App, from its
    /// resources.
    [ObservableProperty] private string name = "Timer";
    /// When it next goes off, as a wall-clock time (saved with no time zone).
    [ObservableProperty]
    [property: JsonConverter(typeof(WallClockDateTimeConverter))]
    private DateTime triggerAt = DateTime.Now;

    /// For a time set as a span from now (a snooze, "in 20 min"), the real instant it's due, in
    /// UTC: the engine goes by this when it's set, so the span is right across the clocks changing.
    /// Null for a time set on the clock (most reminders). Any other change to TriggerAt clears it.
    [ObservableProperty] private DateTime? dueUtc;

    /// For a snoozed repeating reminder, the occurrence the snooze is of (a wall-clock time): its
    /// series moves on from this, not from the snooze's time, which across the clocks going back
    /// can read earlier than it. Null otherwise; any other change to TriggerAt clears it.
    [ObservableProperty]
    [property: JsonConverter(typeof(WallClockNullableDateTimeConverter))]
    private DateTime? snoozedOccurrence;

    /// Where its series goes on from: the snoozed occurrence, or its time.
    [JsonIgnore]
    public DateTime ScheduleFrom => SnoozedOccurrence ?? TriggerAt;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private AlertMode mode = AlertMode.SoundOnly;
    [ObservableProperty] private string? soundFileName;
    [ObservableProperty] private string? phrase;
    [ObservableProperty] private int volume = 100;
    [ObservableProperty] private bool hasFired;

    /// The date/time originally set for this reminder — the anchor a recurrence pattern is computed from.
    /// TriggerAt moves forward on each recurrence; this stays fixed until the user re-edits the schedule.
    [ObservableProperty]
    [property: JsonConverter(typeof(WallClockDateTimeConverter))]
    private DateTime recurrenceAnchor = DateTime.Now;

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

    private bool _settingDue;
    private bool _loading;

    /// Sets when it next goes off: a wall-clock time, and for a span from now, the real instant
    /// too (see <see cref="DueUtc"/>), and for a snooze, the occurrence it's of.
    public void SetDue(DateTime wall, DateTime? dueUtc, DateTime? snoozedOccurrence = null)
    {
        _settingDue = true;
        try
        {
            TriggerAt = wall;
            DueUtc = dueUtc;
            SnoozedOccurrence = snoozedOccurrence;
        }
        finally
        {
            _settingDue = false;
        }
    }

    // A time set any other way is on the clock: an earlier span's instant and snooze no longer
    // apply. Not while loading, where the saved values are set in whatever order the file has.
    partial void OnTriggerAtChanged(DateTime value)
    {
        if (_settingDue || _loading)
            return;
        DueUtc = null;
        SnoozedOccurrence = null;
    }

    void IJsonOnDeserializing.OnDeserializing() => _loading = true;

    void IJsonOnDeserialized.OnDeserialized() => _loading = false;

    partial void OnRecurrenceChanged(RecurrenceRule? value) => OnPropertyChanged(nameof(IsScheduled));
    partial void OnHasFiredChanged(bool value) => OnPropertyChanged(nameof(StatusSortOrder));
    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(StatusSortOrder));
}
