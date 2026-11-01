using System.Globalization;
using System.Windows.Data;
using Pendulum.App.Services;

namespace Pendulum.App.Converters;

/// Shows a date, or a date and time, through DisplayFormat. For a time, bind the value first and
/// the 12/24-hour setting second, so the text updates when the setting changes:
///
///     <MultiBinding Converter="{StaticResource DateTimeDisplay}">
///         <Binding Path="TriggerAt"/>
///         <Binding Source="{x:Static services:AppServices.Instance}" Path="Settings.Use24HourTime"/>
///     </MultiBinding>
///
/// A date alone can be a single binding with ConverterParameter=Date.
public sealed class DateTimeDisplayConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Convert([value], targetType, parameter, culture);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not DateTime value)
            return string.Empty;
        bool? use24Hour = values.Length > 1 && values[1] is bool b ? b : null;
        return parameter as string == "Date" ? DisplayFormat.Date(value) : DisplayFormat.DateAndTime(value, use24Hour);  // not user-visible: the ConverterParameter
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
