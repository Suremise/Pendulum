using Pendulum.Core.Models;

namespace Pendulum.App.ViewModels;

/// One cell in the Calendar tab's month grid. UI-only, not persisted, and rebuilt wholesale
/// (never mutated in place) every time CalendarViewModel.RebuildGrid runs — Reminders is fully
/// populated before the cell is ever exposed to the bound Days collection, so plain properties
/// (no INotifyPropertyChanged) are enough here.
public sealed class CalendarDayCell
{
    private const int MaxVisiblePills = 2;

    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public List<TriggerTimer> Reminders { get; init; } = new();

    public IReadOnlyList<TriggerTimer> VisibleReminders => Reminders.Take(MaxVisiblePills).ToList();
    public int OverflowCount => Math.Max(0, Reminders.Count - MaxVisiblePills);
    public bool HasOverflow => OverflowCount > 0;
}
