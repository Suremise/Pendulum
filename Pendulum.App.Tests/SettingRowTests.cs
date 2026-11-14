using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Pendulum.App.Controls;
using Pendulum.App.Services;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

[Collection(nameof(SettingsScreen))]
public sealed class SettingRowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_state_is_preserved_when_a_row_is_first_laid_out(bool layout) => UiThread.Invoke(() =>
    {
        var settings = new AppSettings { QuickAddHotkeyEnabled = true };
        var field = new TextBox();
        var row = new SettingRow
        {
            Style = (Style)Application.Current.FindResource("SettingRow"),
            DataContext = settings,
            Content = new StackPanel { Children = { field } }
        };
        row.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(AppSettings.QuickAddHotkeyEnabled)));
        if (layout) Layout();
        Assert.True(field.IsEnabled);
        settings.QuickAddHotkeyEnabled = false;
        Assert.False(row.IsEnabled);
        if (!layout) Layout();
        Assert.False(field.IsEnabled);

        void Layout()
        {
            row.Measure(new Size(500, 100));
            row.Arrange(new Rect(0, 0, 500, 100));
            row.UpdateLayout();
        }
    });

    [Fact]
    public void Help_and_actions_inherit_the_rows_data_context_and_align_with_its_field() => UiThread.Invoke(() =>
    {
        var field = new TextBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var action = new Button { Content = "Action" };
        var command = System.Windows.Input.ApplicationCommands.Copy;
        action.SetBinding(Button.CommandProperty, new Binding("Command"));
        var row = new SettingRow
        {
            Style = (Style)Application.Current.FindResource("SettingRow"),
            DataContext = new { Command = command },
            Header = "Label",
            Content = field,
            Footer = action
        };
        row.Measure(new Size(500, 120));
        row.Arrange(new Rect(0, 0, 500, 120));
        row.UpdateLayout();
        Assert.Same(command, action.Command);
        Assert.Equal(field.TranslatePoint(new Point(), row).X, action.TranslatePoint(new Point(), row).X);
        Assert.False(row.Focusable);
        Assert.False(row.IsTabStop);
    });

    [Fact]
    public void Shared_resources_are_loaded_once_and_include_the_link_style() => UiThread.Invoke(() =>
    {
        var app = Application.Current;
        var count = app.Resources.MergedDictionaries.Count;
        AppResources.AddTo(app);
        Assert.Equal(count, app.Resources.MergedDictionaries.Count);
        Assert.IsType<Style>(app.FindResource(typeof(System.Windows.Documents.Hyperlink)));
        Assert.IsType<Style>(app.FindResource("TagBadge"));
        Assert.IsType<Style>(app.FindResource("ReminderPillBadge"));
    });
}
