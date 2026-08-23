using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Parsing;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class QuickAddWindow : FluentWindow
{
    public QuickAddWindow()
    {
        InitializeComponent();
        Loaded += (_, __) =>
        {
            // Summoned by a global hotkey, so some other app usually owns focus — force
            // this window to the foreground so keystrokes land in the input box immediately.
            Win32Interop.ForceForegroundWindow(new WindowInteropHelper(this).Handle);
            Keyboard.Focus(InputBox);
        };
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAdd();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => TryAdd();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TryAdd()
    {
        var result = QuickAddParser.Parse(InputBox.Text, DateTime.Now);

        if (result.When is null)
        {
            ShowError("Couldn't find a time — try \"in 20 min\", \"3pm\", or \"tomorrow 9am\".");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            ShowError("Please enter a name for the reminder, not just a time.");
            return;
        }

        var trigger = new TriggerTimer
        {
            Name = result.Name,
            TriggerAt = result.When.Value,
            RecurrenceAnchor = result.When.Value,
            SoundFileName = AppServices.Instance.Settings.DefaultSoundFileName,
            Mode = AlertMode.SoundOnly
        };

        AppServices.Instance.AddTrigger(trigger);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
