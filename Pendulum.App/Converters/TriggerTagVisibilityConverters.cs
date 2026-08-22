using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pendulum.App.Converters;

/// Multi-binds a TriggerTimer's Recurrence and HasFired so the Reminders list's TAGS
/// column can show exactly one tag per row: Spent (handled separately) takes priority,
/// then Repeats for an active recurring reminder, then this converter's own "One-time"
/// fallback for anything with neither.
public sealed class ActiveRecurringVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasRecurrence = values.Length > 0 && values[0] is not null;
        bool hasFired = values.Length > 1 && values[1] is true;
        return hasRecurrence && !hasFired ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OneTimeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasRecurrence = values.Length > 0 && values[0] is not null;
        bool hasFired = values.Length > 1 && values[1] is true;
        return !hasRecurrence && !hasFired ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
