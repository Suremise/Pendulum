using System.Reflection;
using System.Text.Json.Serialization;
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

/// Marks a saved value that isn't a setting the user chose (a cache, a hint shown once, the
/// Reminders list's filters): Restore and Reset leave it as it is.
[AttributeUsage(AttributeTargets.Property)]
public sealed class BookkeepingAttribute : Attribute;

public partial class AppSettings : ObservableObject, IJsonOnDeserializing
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
    /// Off for a new install: Pendulum goes online only when the user turns this on. Settings
    /// read from a file (settings.json or a backup) that predates this default and doesn't name
    /// the key keep the old default, on: see OnDeserializing.
    [ObservableProperty] private bool checkForUpdatesOnStartup;

    /// Cache for UpdateCheckService, so it only actually hits GitHub once a day rather than
    /// on every launch. Null until the first check ever runs.
    [ObservableProperty][property: Bookkeeping] private DateTime? lastUpdateCheckUtc;
    [ObservableProperty][property: Bookkeeping] private string? lastKnownLatestVersion;

    [ObservableProperty] private bool use24HourTime;
    [ObservableProperty] private bool quickAddHotkeyEnabled = true;
    [ObservableProperty] private string quickAddHotkeyGesture = "Ctrl+Shift+R";
    [ObservableProperty] private bool quickAddAutoListen;

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
    [ObservableProperty][property: Bookkeeping] private bool hasShownTrayMinimizeHint;

    /// Internal bookkeeping: the Reminders list's filter state, restored on the next launch
    /// so the user doesn't have to reapply filters every time they open the app.
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterRowVisible;
    [ObservableProperty][property: Bookkeeping] private string reminderFilterName = string.Empty;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterTypeFixed;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterTypeScheduled;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterStatusUpcoming;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterStatusSpent;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterStatusDisabled;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterModeSoundOnly;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterModeSoundAndSpeech;
    [ObservableProperty][property: Bookkeeping] private bool reminderFilterModeSpeechOnly;
    [ObservableProperty][property: Bookkeeping] private DateTime? reminderFilterFrom;
    [ObservableProperty][property: Bookkeeping] private DateTime? reminderFilterTo;

    /// Runs before a file's values are applied. Up to 1.1.0 the update check was on by default,
    /// so a file without the key had it on: keep it so, rather than turning a user's check off
    /// when they upgrade. A file that names the key sets it as it says.
    void IJsonOnDeserializing.OnDeserializing() => CheckForUpdatesOnStartup = true;

    /// The settings Restore and Reset copy: every saved setting the user chose, found by
    /// reflection so a new one is never left out. Not the bookkeeping, and not
    /// LaunchOnWindowsStartup: only its own checkbox changes the Windows startup entry.
    public static IReadOnlyList<PropertyInfo> CopiedSettings { get; } = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .Where(p => p.GetCustomAttribute<BookkeepingAttribute>() is null)
        .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
        .Where(p => p.Name != nameof(LaunchOnWindowsStartup))
        .ToList();

    /// Sets every one of CopiedSettings to the value <paramref name="source"/> has.
    public void CopySettingsFrom(AppSettings source)
    {
        foreach (var p in CopiedSettings)
            p.SetValue(this, p.GetValue(source));
    }
}
