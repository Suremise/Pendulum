using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace Pendulum.App.Services;

/// The resource setup shared by the app and the two hosts that lay out its views off screen.
internal static class AppResources
{
    private static readonly Uri SharedSource = new("pack://application:,,,/Pendulum;component/Resources/Shared.xaml"); // not user-visible: a resource location

    public static void AddTo(Application application, ApplicationTheme theme = ApplicationTheme.Dark)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        if (dictionaries.Any(dictionary => dictionary.Source == SharedSource)) return;
        dictionaries.Add(new ThemesDictionary { Theme = theme });
        dictionaries.Add(new ControlsDictionary());
        dictionaries.Add(new ResourceDictionary { Source = SharedSource });
    }
}
