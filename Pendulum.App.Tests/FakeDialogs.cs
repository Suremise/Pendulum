using Pendulum.App.Services;
using Pendulum.Core.Models;

namespace Pendulum.App.Tests;

internal sealed class FakeDialogs : IDialogs
{
    public Queue<bool> Answers { get; } = new();
    public List<(string Message, bool Warning)> Confirmations { get; } = new();
    public List<string> Information { get; } = new();
    public List<(string Message, bool Error)> Warnings { get; } = new();
    public List<(TriggerTimer Reminder, bool IsNew)> Edits { get; } = new();
    public List<(string Title, string Filter)> OpenRequests { get; } = new();
    public List<(string Title, string Filter, string FileName)> SaveRequests { get; } = new();
    public string? OpenPath { get; set; }
    public string? SavePath { get; set; }
    public TriggerTimer? EditResult { get; set; }
    public Action? OnEdit { get; set; }
    public string? HotkeyResult { get; set; }
    public List<string> HotkeyRequests { get; } = new();

    public bool Confirm(string message, bool warning = false)
    {
        Confirmations.Add((message, warning));
        return Answers.Count > 0 && Answers.Dequeue();
    }

    public void Inform(string message) => Information.Add(message);
    public void Warn(string message, bool error = false) => Warnings.Add((message, error));

    public string? PickOpenFile(string title, string filter)
    {
        OpenRequests.Add((title, filter));
        return OpenPath;
    }

    public string? PickSaveFile(string title, string filter, string fileName)
    {
        SaveRequests.Add((title, filter, fileName));
        return SavePath;
    }

    public TriggerTimer? EditReminder(TriggerTimer reminder, bool isNew = false)
    {
        Edits.Add((reminder, isNew));
        OnEdit?.Invoke();
        return EditResult;
    }

    public string? EditHotkey(string gesture)
    {
        HotkeyRequests.Add(gesture);
        return HotkeyResult;
    }
}
