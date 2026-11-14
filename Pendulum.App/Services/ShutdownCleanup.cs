namespace Pendulum.App.Services;

/// Exit cleanup attempts every resource, reporting failures without claiming a save succeeded.
internal static class ShutdownCleanup
{
    internal static void Run(Action<string, Exception> report, params (string Context, Action CleanUp)[] steps)
    {
        foreach (var (context, cleanUp) in steps)
        {
            try { cleanUp(); }
            catch (Exception error)
            {
                try { report(context, error); }
                catch { /* A logging failure must not skip the next resource. */ }
            }
        }
    }
}
