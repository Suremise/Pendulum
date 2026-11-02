using System.Globalization;
using System.Windows.Data;

namespace Pendulum.App.Converters;

/// Collapses the Reminders list's selection-checkbox column to 0 width when not
/// in select mode, so it doesn't reserve space (or cause a horizontal scrollbar)
/// until it's actually needed.
public sealed class BoolToSelectColumnWidthConverter : IValueConverter
{
    /// The column in select mode: the cell's 6 px padding each side, the box itself, and room
    /// between it and the name.
    public const double SelectingWidth = 44d;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? SelectingWidth : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
