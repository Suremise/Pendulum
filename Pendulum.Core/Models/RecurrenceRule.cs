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

    [ObservableProperty] private RecurrenceEndType endType = RecurrenceEndType.Never;
    [ObservableProperty] private int occurrenceCount = 10;
    [ObservableProperty] private DateTime? endDate;

    /// How many times this reminder has actually fired since the recurrence was set up.
    [ObservableProperty] private int occurrencesSoFar;
}
