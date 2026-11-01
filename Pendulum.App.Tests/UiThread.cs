using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace Pendulum.App.Tests;

/// One WPF UI thread for the whole test run, with WPF-UI's theme and controls loaded, as the app
/// has them. Tests build and read views on it with Invoke. It never shows a window.
///
/// It is a plain Application, never Pendulum's own App class, whose startup takes the
/// single-instance lock and would wake a running copy of Pendulum.
public static class UiThread
{
    private static readonly Lazy<Dispatcher> Instance = new(Start);

    public static T Invoke<T>(Func<T> work) => Instance.Value.Invoke(work);

    public static void Invoke(Action work) => Instance.Value.Invoke(work);

    public static void Invoke(Action work, DispatcherPriority priority) => Instance.Value.Invoke(work, priority);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF UI for tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
