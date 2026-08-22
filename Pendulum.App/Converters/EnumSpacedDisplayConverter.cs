using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace Pendulum.App.Converters;

/// Renders enum values like "SoundAndSpeech" as "Sound And Speech" for display.
public sealed class EnumSpacedDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        var text = value.ToString() ?? string.Empty;
        var sb = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]))
                sb.Append(' ');
            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
