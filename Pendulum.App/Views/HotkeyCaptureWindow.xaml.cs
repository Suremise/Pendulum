using System.Windows;
using System.Windows.Input;
using Pendulum.App.Services;
using Wpf.Ui.Controls;

namespace Pendulum.App.Views;

public partial class HotkeyCaptureWindow : FluentWindow
{
    private ModifierKeys _modifiers;
    private Key _key = Key.None;

    public string? Result { get; private set; }

    public HotkeyCaptureWindow(string currentGesture)
    {
        InitializeComponent();
        CapturedText.Text = currentGesture;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
            return;

        e.Handled = true;

        if (key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            CapturedText.Text = "Must include Ctrl, Alt, Shift, or Win — try again";
            SaveButtonControl.IsEnabled = false;
            return;
        }

        _modifiers = modifiers;
        _key = key;
        CapturedText.Text = HotkeyManager.FormatGesture(_modifiers, _key);
        SaveButtonControl.IsEnabled = true;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Result = HotkeyManager.FormatGesture(_modifiers, _key);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
