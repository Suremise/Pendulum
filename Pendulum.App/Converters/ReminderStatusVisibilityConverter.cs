using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pendulum.App.Converters;

/// Picks between the "Upcoming" and "Disabled" status badges, which share the same underlying
/// condition (not yet fired) but split on Enabled. Spent is handled separately via a plain
/// HasFired binding since it always takes priority once a reminder has actually fired.
/// Bind [Enabled, HasFired] and pass ConverterParameter="Disabled" for the disabled badge,
/// or omit it (or pass anything else) for the upcoming badge.
public sealed class ReminderStatusVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not bool enabled || values[1] is not bool hasFired)
            return Visibility.Collapsed;

        if (hasFired)
            return Visibility.Collapsed;

        var wantDisabled = string.Equals(parameter as string, "Disabled", StringComparison.Ordinal);
        return (wantDisabled == !enabled) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
