using System.Windows;
using System.Windows.Controls;
using Pendulum.App.ViewModels;

namespace Pendulum.App.Views;

public partial class CalendarPanel : UserControl
{
    // A day's cell, as DayCellTemplate draws it: its edge (0.6 each side) and padding (3 each
    // side), the day's number (18, and 2 below it), and a line for each chip or "+N more" (16,
    // and 2 below it). Kept here with the template; the tests lay the calendar out at many sizes
    // and check that nothing is cut off.
    internal const double CellChrome = 0.6 * 2 + 3 * 2 + 18 + 2;
    internal const double ChipLine = 16 + 2;

    public CalendarPanel()
    {
        InitializeComponent();
        Loaded += (_, __) => (DataContext as CalendarViewModel)?.RefreshForDisplay();
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
