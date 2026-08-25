using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;

namespace Pendulum.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public AppSettings Settings => AppServices.Instance.Settings;

    [ObservableProperty] private List<string> voiceNames = new();
    [ObservableProperty] private List<string> soundFiles = new();
    [ObservableProperty] private List<string> whisperModels = new();
    [ObservableProperty] private List<string> piperModels = new();

    public IEnumerable<ThemeMode> ThemeModes => Enum.GetValues<ThemeMode>();
    public IEnumerable<SpeechToTextEngine> SpeechToTextEngines => Enum.GetValues<SpeechToTextEngine>();
    public IEnumerable<TextToSpeechEngine> TextToSpeechEngines => Enum.GetValues<TextToSpeechEngine>();

    public bool IsWhisperAvailable => WhisperModels.Count > 0;

    public string WhisperStatusText => WhisperModels.Count switch
    {
        0 => "No Whisper models found yet — download one below and drop it into the Whisper Models folder, then click Refresh.",
        1 => "1 model found.",
        var n => $"{n} models found."
    };

    public bool IsPiperExecutableAvailable => AppServices.Instance.IsPiperExecutableAvailable;
    public bool IsPiperAvailable => IsPiperExecutableAvailable && PiperModels.Count > 0;

    public string PiperStatusText => (IsPiperExecutableAvailable, PiperModels.Count) switch
    {
        (false, _) => "piper.exe not found yet — download it below and extract it into the Piper Models folder, then click Refresh.",
        (true, 0) => "piper.exe found, but no voices yet — download one below and drop it into the Piper Models folder, then click Refresh.",
        (true, 1) => "Ready — 1 voice found.",
        (true, var n) => $"Ready — {n} voices found."
    };

    public int TimeFormatIndex
    {
        get => Settings.Use24HourTime ? 1 : 0;
        set => Settings.Use24HourTime = value == 1;
    }

    public SettingsViewModel()
    {
        VoiceNames = AppServices.Instance.Speech.GetVoiceNames().ToList();

        if (string.IsNullOrEmpty(Settings.TtsVoiceName) && VoiceNames.Count > 0)
            Settings.TtsVoiceName = VoiceNames[0];

        RefreshSoundFiles();
        RefreshWhisperModels();
        RefreshPiperModels();

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.LaunchOnWindowsStartup))
                StartupRegistration.SetEnabled(Settings.LaunchOnWindowsStartup);
            if (e.PropertyName == nameof(AppSettings.Theme))
                AppThemeManager.Apply(Settings.Theme);
        };
    }

    partial void OnWhisperModelsChanged(List<string> value)
    {
        OnPropertyChanged(nameof(IsWhisperAvailable));
        OnPropertyChanged(nameof(WhisperStatusText));
    }

    partial void OnPiperModelsChanged(List<string> value)
    {
        OnPropertyChanged(nameof(IsPiperAvailable));
        OnPropertyChanged(nameof(PiperStatusText));
    }

    private void RefreshSoundFiles() => SoundFiles = AppServices.Instance.GetAvailableSoundFiles();

    [RelayCommand]
    private void RefreshWhisperModels()
    {
        AppPaths.EnsureDirectories();
        WhisperModels = Directory.EnumerateFiles(AppPaths.WhisperModelsDirectory, "*.bin")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Settings.WhisperModelFileName is null || !WhisperModels.Contains(Settings.WhisperModelFileName))
            Settings.WhisperModelFileName = WhisperModels.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenWhisperModelsFolder()
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo(AppPaths.WhisperModelsDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenWhisperModelPage()
    {
        Process.Start(new ProcessStartInfo("https://huggingface.co/ggerganov/whisper.cpp/tree/main") { UseShellExecute = true });
    }

    [RelayCommand]
    private void RefreshPiperModels()
    {
        AppPaths.EnsureDirectories();
        PiperModels = Directory.EnumerateFiles(AppPaths.PiperModelsDirectory, "*.onnx")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(IsPiperExecutableAvailable));

        if (Settings.PiperVoiceModelFileName is null || !PiperModels.Contains(Settings.PiperVoiceModelFileName))
            Settings.PiperVoiceModelFileName = PiperModels.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenPiperModelsFolder()
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo(AppPaths.PiperModelsDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenPiperEnginePage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/rhasspy/piper/releases/latest") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenPiperVoicePage()
    {
        Process.Start(new ProcessStartInfo("https://huggingface.co/rhasspy/piper-voices/tree/main") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenSoundsFolder()
    {
        Process.Start(new ProcessStartInfo(AppPaths.SoundsDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void AddSound()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3",
            Title = "Add sound"
        };

        if (dialog.ShowDialog() == true)
        {
            var destination = Path.Combine(AppPaths.SoundsDirectory, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, destination, overwrite: true);
            RefreshSoundFiles();
        }
    }

    [RelayCommand]
    private void TestVoice()
    {
        var engine = AppServices.Instance.CreateSpeechEngine();
        _ = SpeakThenDisposeAsync(engine);
    }

    private static async Task SpeakThenDisposeAsync(ITextToSpeechEngine engine)
    {
        try
        {
            await engine.SpeakAndWaitAsync("This is how Pendulum will sound.", CancellationToken.None);
        }
        finally
        {
            engine.Dispose();
        }
    }

    [RelayCommand]
    private void ExportBackup()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Pendulum backup (*.json)|*.json",
            FileName = $"pendulum-backup-{DateTime.Now:yyyy-MM-dd}.json",
            Title = "Export backup"
        };

        if (dialog.ShowDialog() != true)
            return;

        AppServices.Instance.ExportBackup(dialog.FileName);
        MessageBox.Show(
            Application.Current.MainWindow!,
            $"Backed up settings and {AppServices.Instance.Triggers.Count} reminder(s) to {Path.GetFileName(dialog.FileName)}.",
            "Pendulum",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Pendulum backup (*.json)|*.json",
            Title = "Restore backup"
        };

        if (dialog.ShowDialog() != true)
            return;

        var confirm = MessageBox.Show(
            Application.Current.MainWindow!,
            "Restore this backup?\n\nThis replaces all your current settings and reminders. This cannot be undone.",
            "Pendulum",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        AppBackup backup;
        try
        {
            backup = AppServices.Instance.RestoreBackup(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current.MainWindow!,
                $"Couldn't read that file as a Pendulum backup.\n\n{ex.Message}",
                "Pendulum",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (backup.Settings is not null)
            ApplySettingsFrom(backup.Settings);

        MessageBox.Show(
            Application.Current.MainWindow!,
            $"Restored settings and {backup.Triggers.Count} reminder(s).",
            "Pendulum",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ChangeHotkey()
    {
        var dialog = new HotkeyCaptureWindow(Settings.QuickAddHotkeyGesture) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
            Settings.QuickAddHotkeyGesture = dialog.Result;
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var result = MessageBox.Show(
            Application.Current.MainWindow!,
            "Reset all settings to their defaults?",
            "Pendulum",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var defaults = new AppSettings();
        defaults.TtsVoiceName = VoiceNames.Count > 0 ? VoiceNames[0] : null;
        ApplySettingsFrom(defaults);
    }

    private void ApplySettingsFrom(AppSettings source)
    {
        Settings.DefaultSoundFileName = source.DefaultSoundFileName;
        Settings.TtsVoiceName = source.TtsVoiceName;
        Settings.TtsRate = source.TtsRate;
        Settings.TtsVolume = source.TtsVolume;
        Settings.LaunchOnWindowsStartup = source.LaunchOnWindowsStartup;
        Settings.StartMinimized = source.StartMinimized;
        Settings.SnoozeMinutes = source.SnoozeMinutes;
        Settings.RepeatAlertUntilDismissed = source.RepeatAlertUntilDismissed;
        Settings.AutoResolveMinutes = source.AutoResolveMinutes;
        Settings.Theme = source.Theme;
        Settings.Use24HourTime = source.Use24HourTime;
        Settings.QuickAddHotkeyEnabled = source.QuickAddHotkeyEnabled;
        Settings.QuickAddHotkeyGesture = source.QuickAddHotkeyGesture;
        Settings.QuickAddAutoListen = source.QuickAddAutoListen;
        Settings.SpeechToTextEngine = source.SpeechToTextEngine;
        Settings.WhisperModelFileName = source.WhisperModelFileName;
        Settings.TextToSpeechEngine = source.TextToSpeechEngine;
        Settings.PiperVoiceModelFileName = source.PiperVoiceModelFileName;

        OnPropertyChanged(nameof(TimeFormatIndex));
    }
}
