using CommunityToolkit.Mvvm.ComponentModel;

namespace Pendulum.Core.Models;

public enum RecurrenceFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public enum RecurrenceEndType
{
    Never,
    AfterOccurrences,
    ByDate
}

/// How a monthly or yearly rule picks its day: by number ("day 15"), or Outlook's relative way
/// ("the last Friday", "the second weekday").
public enum MonthDayRule
{
    DayNumber,
    RelativeDay
}

/// Which one of the matching days in the month: the first to the fourth, or the last.
public enum WeekOfMonth
{
    First = 1,
    Second = 2,
    Third = 3,
    Fourth = 4,
    Last = 5
}

/// The kind of day a relative rule counts: a weekday by name (numbered as DayOfWeek), or any
/// day, any weekday (Monday to Friday), or any weekend day.
public enum RelativeDay
{
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Day = 7,
    Weekday = 8,
    WeekendDay = 9
}

public partial class RecurrenceRule : ObservableObject
{
    [ObservableProperty] private RecurrenceFrequency frequency = RecurrenceFrequency.Daily;
    [ObservableProperty] private int interval = 1;

    /// Used when Frequency == Weekly. Empty means "same weekday as the start date".
    [ObservableProperty] private List<DayOfWeek> daysOfWeek = new();

    /// Used when Frequency == Monthly or Yearly.
    [ObservableProperty] private int dayOfMonth = 1;

    /// Used when Frequency == Yearly (1-12).
    [ObservableProperty] private int month = 1;

    /// Used when Frequency == Monthly or Yearly: DayNumber uses DayOfMonth; RelativeDay uses
    /// WeekOfMonth and RelativeDay. Files saved before these existed don't name them and load as
    /// DayNumber, exactly as they worked then.
    [ObservableProperty] private MonthDayRule monthDayRule = MonthDayRule.DayNumber;
    [ObservableProperty] private WeekOfMonth weekOfMonth = WeekOfMonth.First;
    [ObservableProperty] private RelativeDay relativeDay = RelativeDay.Monday;

    [ObservableProperty] private RecurrenceEndType endType = RecurrenceEndType.Never;
    [ObservableProperty] private int occurrenceCount = 10;
    [ObservableProperty] private DateTime? endDate;

    /// How many times this reminder has actually fired since the recurrence was set up.
    [ObservableProperty] private int occurrencesSoFar;
}
