using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pendulum.App.Resources;
using Pendulum.App.Services;
using Pendulum.App.Views;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;
// WPF has its own System.Windows.ThemeMode since .NET 9: the settings' one is meant here.
using ThemeMode = Pendulum.Core.Models.ThemeMode;

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
        0 => Strings.Settings_WhisperNone,
        1 => Strings.Settings_WhisperOne,
        var n => string.Format(Strings.Settings_WhisperMany, n)
    };

    public bool IsPiperExecutableAvailable => AppServices.Instance.IsPiperExecutableAvailable;
    public bool IsPiperAvailable => IsPiperExecutableAvailable && PiperModels.Count > 0;

    public string PiperStatusText => (IsPiperExecutableAvailable, PiperModels.Count) switch
    {
        (false, _) => Strings.Settings_PiperMissing,
        (true, 0) => Strings.Settings_PiperNoVoices,
        (true, 1) => Strings.Settings_PiperOneVoice,
        (true, var n) => string.Format(Strings.Settings_PiperVoices, n)
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
    private void OpenWhisperModelPage() => OpenDownloadPage("https://huggingface.co/ggerganov/whisper.cpp/tree/main");

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

    /// Piper's last release under the MIT licence (its project has since moved to a GPL one).
    [RelayCommand]
    private void OpenPiperEnginePage() => OpenDownloadPage("https://github.com/rhasspy/piper/releases/tag/2023.11.14-2");

    /// The Piper voices Pendulum offers: only those whose terms allow anyone to use them, each with
    /// its licence and the credit it asks for. A voice's own folder holds its MODEL_CARD.
    public IReadOnlyList<VoiceOffer> PiperVoiceOffers => OfferedVoices;

    internal static IReadOnlyList<VoiceOffer> OfferedVoices { get; } =
    [
        new(Strings.Voice_Alba_Name, Strings.Voice_Alba_Licence, Strings.Voice_Alba_Credit,
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_GB/alba/medium"),
        new(Strings.Voice_Jenny_Name, Strings.Voice_Jenny_Licence, Strings.Voice_Jenny_Credit,
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_GB/jenny_dioco/medium"),
        new(Strings.Voice_LibriTts_Name, Strings.Voice_LibriTts_Licence, Strings.Voice_LibriTts_Credit,
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US/libritts_r/medium"),
        new(Strings.Voice_NorthernEnglishMale_Name, Strings.Voice_NorthernEnglishMale_Licence, Strings.Voice_NorthernEnglishMale_Credit,
            "https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_GB/northern_english_male/medium"),
    ];

    [RelayCommand]
    private void OpenVoicePage(VoiceOffer? voice)
    {
        if (voice is not null)
            OpenDownloadPage(voice.Url);
    }

    private static void OpenDownloadPage(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

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
            Filter = Strings.Dialog_AudioFilter,
            Title = Strings.Dialog_AddSoundTitle
        };

        if (dialog.ShowDialog() == true)
        {
            var destination = Path.Combine(AppPaths.SoundsDirectory, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, destination, overwrite: true);
            RefreshSoundFiles();
        }
    }

    [RelayCommand]
    private async Task TestVoice()
    {
        var engine = AppServices.Instance.CreateSpeechEngine();
        try
        {
            await engine.SpeakAndWaitAsync(Strings.Settings_TestPhrase, CancellationToken.None);
        }
        catch (Exception ex)
        {
            CrashLog.Write("TestVoice", ex);  // not user-visible
            MessageBox.Show(
                Application.Current.MainWindow!,
                string.Format(Strings.Settings_TestVoiceFailed, ex.Message),
                Strings.App_Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
            Filter = Strings.Dialog_BackupFilter,
            FileName = $"pendulum-backup-{DateTime.Now:yyyy-MM-dd}.json",
            Title = Strings.Dialog_ExportBackupTitle
        };

        if (dialog.ShowDialog() != true)
            return;

        AppServices.Instance.ExportBackup(dialog.FileName);
        MessageBox.Show(
            Application.Current.MainWindow!,
            string.Format(Strings.Backup_Done, AppServices.Instance.Triggers.Count, Path.GetFileName(dialog.FileName)),
            Strings.App_Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var dialog = new OpenFileDialog
        {
            Filter = Strings.Dialog_BackupFilter,
            Title = Strings.Dialog_RestoreBackupTitle
        };

        if (dialog.ShowDialog() != true)
            return;

        var confirm = MessageBox.Show(
            Application.Current.MainWindow!,
            Strings.Backup_ConfirmRestore,
            Strings.App_Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        AppBackup backup;
        int caughtUp;
        try
        {
            backup = AppServices.Instance.RestoreBackup(dialog.FileName, out caughtUp);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current.MainWindow!,
                string.Format(Strings.Backup_CouldNotRead, ex.Message),
                Strings.App_Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (backup.Settings is not null)
            ApplySettingsFrom(backup.Settings);

        MessageBox.Show(
            Application.Current.MainWindow!,
            string.Format(Strings.Backup_Restored, backup.Triggers.Count)
                + (caughtUp > 0 ? string.Format(Strings.Backup_RestoredCaughtUp, caughtUp) : ""),
            Strings.App_Name,
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
            Strings.Settings_ConfirmReset,
            Strings.App_Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        var defaults = new AppSettings();
        defaults.TtsVoiceName = VoiceNames.Count > 0 ? VoiceNames[0] : null;
        ApplySettingsFrom(defaults);
    }

    /// Restore and Reset: every setting the user chose, saved once. Start with Windows stays as
    /// it is (only its checkbox changes the startup entry), and so does the bookkeeping.
    internal void ApplySettingsFrom(AppSettings source)
    {
        AppServices.Instance.ChangeSettingsTogether(() => Settings.CopySettingsFrom(source));
        OnPropertyChanged(nameof(TimeFormatIndex));
    }
}
