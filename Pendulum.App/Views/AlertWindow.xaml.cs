using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Pendulum.App.Services;

namespace Pendulum.App.Views;

public partial class AlertWindow : Window
{
    // Every currently-open AlertWindow, so simultaneous alerts stack in a column above the
    // bottom-right corner instead of all rendering at the same spot and hiding each other.
    private static readonly List<AlertWindow> OpenWindows = new();

    // The work area alerts go in: the monitor the user was working on when the newest opened.
    private static Rect _workArea = SystemParameters.WorkArea;

    private readonly DispatcherTimer _topmostReinforcer;

    public event Action? Dismissed;
    public event Action? Snoozed;

    public AlertWindow(string title, string subtitle, bool canSnooze)
    {
        InitializeComponent();

        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        SnoozeButton.Visibility = canSnooze ? Visibility.Visible : Visibility.Collapsed;

        SourceInitialized += (_, __) => Win32Interop.MakeNonActivating(new WindowInteropHelper(this).Handle);
        Loaded += (_, __) =>
        {
            OpenWindows.Add(this);
            _workArea = WorkAreaFor(this);
            RestackOpenWindows();
        };

        _topmostReinforcer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostReinforcer.Tick += (_, __) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                Win32Interop.ForceTopmost(hwnd);
        };
        _topmostReinforcer.Start();

        Closed += (_, __) =>
        {
            _topmostReinforcer.Stop();
            OpenWindows.Remove(this);
            RestackOpenWindows();
        };
    }

    // Re-positions every open alert (AlertLayout: columns from the bottom-right corner, newest at
    // the bottom, all on screen however many) — run whenever one opens or closes so the rest move
    // to fill the gap rather than leaving new alerts to draw directly on top of existing ones.
    private static void RestackOpenWindows()
    {
        var sizes = OpenWindows.Select(w => new Size(w.Width, w.ActualHeight)).ToList();
        var positions = AlertLayout.Place(_workArea, sizes);
        for (var i = 0; i < OpenWindows.Count; i++)
        {
            OpenWindows[i].Left = positions[i].X;
            OpenWindows[i].Top = positions[i].Y;
        }
    }

    // The work area of the monitor with the user's window, in this window's units; the primary
    // monitor's when Windows can't say.
    private static Rect WorkAreaFor(Window window)
    {
        var pixels = Win32Interop.ForegroundMonitorWorkArea();
        var fromDevice = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice;
        if (pixels is not { } p || fromDevice is not { } m)
            return SystemParameters.WorkArea;

        return new Rect(m.Transform(new Point(p.Left, p.Top)), m.Transform(new Point(p.Right, p.Bottom)));
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke();
        Close();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        Snoozed?.Invoke();
        Close();
    }
}
