using Pendulum.App.Resources;

namespace Pendulum.App.Services;

/// A reminder colour's name as the app shows it (ReminderColors saves them by name, in English,
/// as keys).
internal static class ReminderColorNames
{
    public static string Display(string name) => name switch
    {
        "terracotta" => Strings.Edit_ColourTerracotta, // not user-visible: saved names
        "ochre" => Strings.Edit_ColourOchre, // not user-visible
        "olive" => Strings.Edit_ColourOlive, // not user-visible
        "sage" => Strings.Edit_ColourSage, // not user-visible
        "teal" => Strings.Edit_ColourTeal, // not user-visible
        "blue" => Strings.Edit_ColourBlue, // not user-visible
        "violet" => Strings.Edit_ColourViolet, // not user-visible
        "plum" => Strings.Edit_ColourPlum, // not user-visible
        _ => name,
    };
}
