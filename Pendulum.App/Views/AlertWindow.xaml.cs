using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Pendulum.App.Services;

namespace Pendulum.App.Views;

public partial class AlertWindow : Window
{
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
        Loaded += (_, __) => PositionBottomRight();

        _topmostReinforcer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostReinforcer.Tick += (_, __) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                Win32Interop.ForceTopmost(hwnd);
        };
        _topmostReinforcer.Start();

        Closed += (_, __) => _topmostReinforcer.Stop();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - ActualHeight - 24;
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
