using System.Windows.Controls;
using Pendulum.App.ViewModels;

namespace Pendulum.App.Views;

public partial class CalendarPanel : UserControl
{
    public CalendarPanel()
    {
        InitializeComponent();
        Loaded += (_, __) => (DataContext as CalendarViewModel)?.RefreshForDisplay();
    }
}
