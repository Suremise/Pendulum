using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Pendulum.App.Services;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public AppSettings Settings => AppServices.Instance.Settings;

    [ObservableProperty] private List<string> voiceNames = new();
    [ObservableProperty] private List<string> soundFiles = new();

    public IEnumerable<AlertStyle> AlertStyles => Enum.GetValues<AlertStyle>();

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

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.LaunchOnWindowsStartup))
                StartupRegistration.SetEnabled(Settings.LaunchOnWindowsStartup);
        };
    }

    private void RefreshSoundFiles() => SoundFiles = AppServices.Instance.GetAvailableSoundFiles();

    [RelayCommand]
    private void OpenSoundsFolder()
    {
        Process.Start(new ProcessStartInfo(PortablePaths.SoundsDirectory) { UseShellExecute = true });
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
            var destination = Path.Combine(PortablePaths.SoundsDirectory, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, destination, overwrite: true);
            RefreshSoundFiles();
        }
    }

    [RelayCommand]
    private void TestVoice()
    {
        _ = AppServices.Instance.Speech.SpeakAndWaitAsync("This is how Pendulum will sound.", CancellationToken.None);
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
        Settings.SnoozeMinutes = source.SnoozeMinutes;
        Settings.RepeatAlertUntilDismissed = source.RepeatAlertUntilDismissed;
        Settings.AutoResolveMinutes = source.AutoResolveMinutes;
        Settings.Theme = source.Theme;
        Settings.AlertStyle = source.AlertStyle;
        Settings.Use24HourTime = source.Use24HourTime;

        OnPropertyChanged(nameof(TimeFormatIndex));
    }
}
