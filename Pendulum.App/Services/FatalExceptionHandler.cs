using System.Windows.Threading;

namespace Pendulum.App.Services;

/// Final best-effort work before an unhandled exception terminates the process.
internal static class FatalExceptionHandler
{
    internal static void Handle(Exception? error, Dispatcher owner, Action flush, Action<string, Exception> report)
    {
        if (error is not null) TryReport("AppDomain.UnhandledException", error); // not user-visible: log context

        // Snapshot live models only on their owner. Never dispatch or wait for it: a fatal
        // background failure can arrive while the UI is blocked or already shutting down.
        if (!owner.CheckAccess()) return;
        try { flush(); }
        catch (Exception saveError) { TryReport("FatalException.FlushPendingSaves", saveError); } // not user-visible: log context

        void TryReport(string context, Exception exception)
        {
            try { report(context, exception); }
            catch { /* Logging must not prevent the remaining crash cleanup. */ }
        }
    }
}
