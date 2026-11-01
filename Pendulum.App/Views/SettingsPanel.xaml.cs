using System.Windows;
using System.Windows.Controls;

namespace Pendulum.App.Views;

/// Settings, as a list of sections on the left and the chosen one on the right. Only which
/// section is shown lives here; every setting binds to SettingsViewModel as before.
public partial class SettingsPanel : UserControl
{
    private readonly FrameworkElement[] _sections;

    public SettingsPanel()
    {
        InitializeComponent();
        _sections = [GeneralSection, AlertsSection, VoiceSection, QuickSection, DataSection];
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged can fire while InitializeComponent is still building the list.
        if (_sections is null)
            return;

        var index = Math.Max(SectionList.SelectedIndex, 0);
        for (var i = 0; i < _sections.Length; i++)
            _sections[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        SectionScroller.ScrollToTop();
    }
}
