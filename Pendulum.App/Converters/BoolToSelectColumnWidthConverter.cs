using System.Globalization;
using System.Windows.Data;

namespace Pendulum.App.Converters;

/// Collapses the Reminders list's selection-checkbox column to 0 width when not
/// in select mode, so it doesn't reserve space (or cause a horizontal scrollbar)
/// until it's actually needed.
public sealed class BoolToSelectColumnWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? 34d : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
