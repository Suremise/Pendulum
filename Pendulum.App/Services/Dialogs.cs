using System.Windows;
using Microsoft.Win32;
using Pendulum.App.Resources;
using Pendulum.App.Views;
using Pendulum.Core.Models;

namespace Pendulum.App.Services;

/// User decisions and modal UI, separated from the commands that act on them.
public interface IDialogs
{
    bool Confirm(string message, bool warning = false);
    void Inform(string message);
    void Warn(string message, bool error = false);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string fileName);
    TriggerTimer? EditReminder(TriggerTimer reminder, bool isNew = false);
    string? EditHotkey(string gesture);
}

public sealed class WpfDialogs : IDialogs
{
    public static IDialogs Instance { get; } = new WpfDialogs();

    public bool Confirm(string message, bool warning = false) =>
        MessageBox.Show(Application.Current.MainWindow!, message, Strings.App_Name,
            MessageBoxButton.YesNo, warning ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, Strings.App_Name,
            MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, bool error = false) =>
        MessageBox.Show(Application.Current.MainWindow!, message, Strings.App_Name,
            MessageBoxButton.OK, error ? MessageBoxImage.Error : MessageBoxImage.Warning);

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// The existing editor applies an edit itself; a new reminder is returned for the caller
    /// to add only after Save. Both the calendar and the list enter the editor here.
    public TriggerTimer? EditReminder(TriggerTimer reminder, bool isNew = false)
    {
        var dialog = new TriggerEditWindow(reminder, isNew) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? EditHotkey(string gesture)
    {
        var dialog = new HotkeyCaptureWindow(gesture) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
