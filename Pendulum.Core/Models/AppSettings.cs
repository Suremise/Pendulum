using CommunityToolkit.Mvvm.ComponentModel;

namespace Pendulum.Core.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private string defaultSoundFileName = "chime.wav";
    [ObservableProperty] private string? ttsVoiceName;
    [ObservableProperty] private int ttsRate;
    [ObservableProperty] private int ttsVolume = 100;
    [ObservableProperty] private bool launchOnWindowsStartup;
    [ObservableProperty] private int snoozeMinutes = 5;
    [ObservableProperty] private bool repeatAlertUntilDismissed = true;

    /// When RepeatAlertUntilDismissed is false, how long an unanswered alert waits before
    /// resolving itself (recurring reminders reschedule, one-shots move to Spent) — the same
    /// outcome as if the user had clicked Dismiss. Has no effect when RepeatAlertUntilDismissed is true.
    [ObservableProperty] private int autoResolveMinutes = 30;
    [ObservableProperty] private ThemeMode theme = ThemeMode.System;
    [ObservableProperty] private bool use24HourTime = true;

    /// Internal bookkeeping (not a user-facing setting): whether the "still running in
    /// the tray" toast has been shown yet. Fires once, the first time the window is
    /// ever minimized to the tray rather than closed.
    [ObservableProperty] private bool hasShownTrayMinimizeHint;
}
