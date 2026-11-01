using System.Globalization;
using Pendulum.App.Resources;

namespace Pendulum.App.Services;

/// How dates and times are shown, everywhere in the app. The patterns are strings in
/// Resources\Strings.resx (Format_*), so a translation can reorder them; day and month names come
/// from the user's culture; and times follow the 12- or 24-hour setting.
public static class DisplayFormat
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// The 12/24-hour setting, read when a time is shown (a caller can pass it instead).
    private static bool Use24Hour => AppServices.Instance.Settings.Use24HourTime;

    /// "14:30" or "2:30 pm", by the setting.
    public static string Time(DateTime value, bool? use24Hour = null) =>
        value.ToString((use24Hour ?? Use24Hour) ? Strings.Format_Time24 : Strings.Format_Time12, Culture);

    /// "Tue, 25 Aug 2026".
    public static string Date(DateTime value) => value.ToString(Strings.Format_Date, Culture);

    /// "Tue, 25 Aug 2026  14:30".
    public static string DateAndTime(DateTime value, bool? use24Hour = null) =>
        string.Format(Culture, Strings.Format_DateAndTime, Date(value), Time(value, use24Hour));

    /// "25 Aug", for date ranges.
    public static string ShortDate(DateTime value) => value.ToString(Strings.Format_ShortDate, Culture);

    /// "25 August 2026, 14:30", for a repeat's start.
    public static string LongDateAndTime(DateTime value, bool? use24Hour = null) =>
        string.Format(Culture, Strings.Format_LongDateAndTime, value.ToString(Strings.Format_LongDate, Culture), Time(value, use24Hour));

    /// "August 2026", the calendar's heading.
    public static string MonthAndYear(DateTime value) => value.ToString(Strings.Format_MonthAndYear, Culture);
}
