using CommunityToolkit.Mvvm.ComponentModel;

namespace Pendulum.Core.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum SpeechToTextEngine
{
    WindowsSpeechRecognition,
    Whisper
}

public enum TextToSpeechEngine
{
    WindowsSpeechSynthesis,
    Piper
}

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private string defaultSoundFileName = "chime.wav";
    [ObservableProperty] private string? ttsVoiceName;
    [ObservableProperty] private int ttsRate;
    [ObservableProperty] private int ttsVolume = 100;
    [ObservableProperty] private bool launchOnWindowsStartup = true;
    [ObservableProperty] private bool startMinimized = true;
    [ObservableProperty] private int snoozeMinutes = 5;
    [ObservableProperty] private bool repeatAlertUntilDismissed = true;

    /// When RepeatAlertUntilDismissed is false, how long an unanswered alert waits before
    /// resolving itself (recurring reminders reschedule, one-shots move to Spent) — the same
    /// outcome as if the user had clicked Dismiss. Has no effect when RepeatAlertUntilDismissed is true.
    [ObservableProperty] private int autoResolveMinutes = 30;
    [ObservableProperty] private ThemeMode theme = ThemeMode.System;
    [ObservableProperty] private bool use24HourTime;
    [ObservableProperty] private bool quickAddHotkeyEnabled = true;
    [ObservableProperty] private string quickAddHotkeyGesture = "Ctrl+Shift+R";

    [ObservableProperty] private SpeechToTextEngine speechToTextEngine = SpeechToTextEngine.WindowsSpeechRecognition;

    /// File name (not full path) of the .bin ggml model in AppPaths.WhisperModelsDirectory
    /// to use when SpeechToTextEngine is Whisper. Null if none has been picked yet.
    [ObservableProperty] private string? whisperModelFileName;

    [ObservableProperty] private TextToSpeechEngine textToSpeechEngine = TextToSpeechEngine.WindowsSpeechSynthesis;

    /// File name (not full path) of the .onnx voice model in AppPaths.PiperModelsDirectory
    /// to use when TextToSpeechEngine is Piper. Null if none has been picked yet.
    [ObservableProperty] private string? piperVoiceModelFileName;

    /// 0 disables cleanup. When greater than 0, Spent reminders whose trigger time is more
    /// than this many days in the past are permanently deleted on startup.
    [ObservableProperty] private int autoDeleteSpentAfterDays;

    /// Internal bookkeeping (not a user-facing setting): whether the "still running in
    /// the tray" toast has been shown yet. Fires once, the first time the window is
    /// ever minimized to the tray rather than closed.
    [ObservableProperty] private bool hasShownTrayMinimizeHint;

    /// Internal bookkeeping: the Reminders list's filter state, restored on the next launch
    /// so the user doesn't have to reapply filters every time they open the app.
    [ObservableProperty] private bool reminderFilterRowVisible;
    [ObservableProperty] private string reminderFilterName = string.Empty;
    [ObservableProperty] private bool reminderFilterTypeFixed;
    [ObservableProperty] private bool reminderFilterTypeScheduled;
    [ObservableProperty] private bool reminderFilterStatusUpcoming;
    [ObservableProperty] private bool reminderFilterStatusSpent;
    [ObservableProperty] private bool reminderFilterStatusDisabled;
    [ObservableProperty] private bool reminderFilterModeSoundOnly;
    [ObservableProperty] private bool reminderFilterModeSoundAndSpeech;
    [ObservableProperty] private bool reminderFilterModeSpeechOnly;
    [ObservableProperty] private DateTime? reminderFilterFrom;
    [ObservableProperty] private DateTime? reminderFilterTo;
}
