using Pendulum.App.Resources;
using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

/// One cell in the Calendar tab's month grid. UI-only, not persisted, and rebuilt wholesale
/// (never mutated in place) every time CalendarViewModel.RebuildGrid runs — Reminders is fully
/// populated before the cell is ever exposed to the bound Days collection, so plain properties
/// (no INotifyPropertyChanged) are enough here.
public sealed class CalendarDayCell
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public List<TriggerTimer> Reminders { get; init; } = new();

    /// How many lines (a chip, or "+N more") fit under the day's number at the cell's size. None
    /// in a small window: then "+N more" goes beside the number (<see cref="IsCompact"/>).
    public int Lines { get; init; } = 2;

    /// Too short for a line under the number: every reminder is under "+N more", beside it.
    public bool IsCompact => Lines == 0;

    /// The chips shown: all of the day's reminders when they fit, otherwise one line fewer than
    /// fits, keeping the last line for "+N more", so it's never cut off at the cell's foot.
    public IReadOnlyList<TriggerTimer> VisibleReminders =>
        Reminders.Count <= Lines ? Reminders : Reminders.Take(Math.Max(0, Lines - 1)).ToList();

    public int OverflowCount => Reminders.Count - VisibleReminders.Count;
    public bool HasOverflow => OverflowCount > 0;

    /// What the overflow says: "+2 more" under the chips, or beside the number when there's no
    /// room for any, how many the day has ("3 reminders").
    public string OverflowText =>
        !IsCompact ? string.Format(Strings.Calendar_MoreFormat, OverflowCount)
        : Reminders.Count == 1 ? Strings.Calendar_OneReminder
        : string.Format(Strings.Calendar_RemindersFormat, Reminders.Count);
}
