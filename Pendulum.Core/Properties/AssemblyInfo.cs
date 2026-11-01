using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Pendulum.Core.Tests")]
[assembly: InternalsVisibleTo("Pendulum.App.Tests")]
// The build-only tool that renders the README's screenshots (tools/Pendulum.Screenshots): it uses
// the same test seams, a temp folder and settings of its own. It never ships.
[assembly: InternalsVisibleTo("Pendulum.Screenshots")]
