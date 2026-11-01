using System.Globalization;
using System.Text;
using System.Windows.Data;
using Pendulum.App.Resources;

namespace Pendulum.App.Converters;

/// Shows an enum value by its name in Resources\Strings.resx (Enum_<Type>_<Value>, as
/// Enum_AlertMode_SoundAndSpeech), or, for a value with no name there, spaced out ("Sound And Speech").
public sealed class EnumSpacedDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Empty;

        var name = Strings.ResourceManager.GetString($"Enum_{value.GetType().Name}_{value}", CultureInfo.CurrentUICulture);  // not user-visible: a key
        return name ?? Spaced(value.ToString() ?? string.Empty);
    }

    private static string Spaced(string text)
    {
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
