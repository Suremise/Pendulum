using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using Pendulum.App.Resources;

namespace Pendulum.App.Converters;

public sealed class SortArrowConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string property && property == parameter as string
            && values[1] is ListSortDirection direction
            ? direction == ListSortDirection.Ascending ? Strings.Reminders_SortAscending : Strings.Reminders_SortDescending
            : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
