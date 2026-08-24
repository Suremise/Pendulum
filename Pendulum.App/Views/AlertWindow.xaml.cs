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

    // Re-positions every open alert in a column above the bottom-right corner, most recently
    // opened at the bottom — run whenever one opens or closes so the rest slide down/up to fill
    // the gap rather than leaving new alerts to draw directly on top of existing ones.
    private static void RestackOpenWindows()
    {
        var workArea = SystemParameters.WorkArea;
        var bottom = workArea.Bottom - 24;

        for (int i = OpenWindows.Count - 1; i >= 0; i--)
        {
            var w = OpenWindows[i];
            w.Left = workArea.Right - w.Width - 24;
            w.Top = bottom - w.ActualHeight;
            bottom -= w.ActualHeight + 12;
        }
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
