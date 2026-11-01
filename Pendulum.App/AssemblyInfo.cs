using System.Runtime.CompilerServices;
using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

[assembly: InternalsVisibleTo("Pendulum.App.Tests")]
// The build-only tool that renders the README's screenshots (tools/Pendulum.Screenshots): it uses
// the same test seams, a temp folder and settings of its own. It never ships.
[assembly: InternalsVisibleTo("Pendulum.Screenshots")]
