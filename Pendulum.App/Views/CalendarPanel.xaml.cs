using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Pendulum.App.ViewModels;
using Pendulum.Core.Engine;

namespace Pendulum.App.Views;

public partial class CalendarPanel : UserControl
{
    // A day's cell, as DayCellTemplate draws it: its edge (0.6 each side) and padding (3 each
    // side), the day's number (18, and 2 below it), and a line for each chip or "+N more" (16,
    // and 2 below it). Kept here with the template; the tests lay the calendar out at many sizes
    // and check that nothing is cut off.
    internal const double CellChrome = 0.6 * 2 + 3 * 2 + 18 + 2;
    internal const double ChipLine = 16 + 2;

    private readonly DispatcherTimer _midnightTimer;
    private Window? _owner;
    private bool _active;

    public CalendarPanel()
    {
        _midnightTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _midnightTimer.Tick += (_, _) => RefreshClock();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _owner = Window.GetWindow(this);
            if (_owner is not null) _owner.StateChanged += OnOwnerStateChanged;
            UpdateActivity();
        };
        Unloaded += (_, _) =>
        {
            SetActive(false);
            if (_owner is not null) _owner.StateChanged -= OnOwnerStateChanged;
            _owner = null;
        };
        IsVisibleChanged += (_, _) => UpdateActivity();
        DataContextChanged += (_, e) =>
        {
            (e.OldValue as CalendarViewModel)?.SetVisible(false);
            (e.NewValue as CalendarViewModel)?.SetVisible(_active);
        };
    }

    private void OnOwnerStateChanged(object? sender, EventArgs e) => UpdateActivity();

    private void UpdateActivity() => SetActive(IsLoaded && IsVisible && _owner?.WindowState != WindowState.Minimized);

    private void SetActive(bool active)
    {
        (DataContext as CalendarViewModel)?.SetVisible(active);
        if (_active == active) return;
        _active = active;
        if (active)
        {
            SystemEvents.TimeChanged += OnTimeChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            RefreshClock();
        }
        else
        {
            _midnightTimer.Stop();
            SystemEvents.TimeChanged -= OnTimeChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshClock, DispatcherPriority.Background);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(RefreshClock, DispatcherPriority.Background);
    }

    private void RefreshClock()
    {
        if (!_active) return;
        (DataContext as CalendarViewModel)?.RefreshDate();
        _midnightTimer.Stop();
        var now = DateTimeOffset.UtcNow;
        _midnightTimer.Interval = WallClock.NextDateBoundary(now, TimeZoneInfo.Local) - now;
        _midnightTimer.Start();
    }

    /// The day grid is six rows of cells: as many chip lines as fit in one (none in a small
    /// window, when "+N more" goes beside the day's number).
    internal static int LinesThatFit(double gridHeight) =>
        Math.Max(0, (int)Math.Floor((gridHeight / 6 - CellChrome) / ChipLine));

    private void DayGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged && DataContext is CalendarViewModel vm)
            vm.ChipLines = LinesThatFit(e.NewSize.Height);
    }
}
