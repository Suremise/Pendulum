using System.IO;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Pendulum.App.Services;

public readonly record struct ImportResult(int Added, int Replaced, int Skipped);

/// Single shared instance holding the app's services and live state.
/// Deliberately simple (no DI container) given the app's size.
public sealed class AppServices
{
    public static AppServices Instance { get; } = new();

    public TriggerRepository TriggerRepository { get; } = new();
    public SettingsRepository SettingsRepository { get; } = new();
    public AudioService Audio { get; } = new();
    public SpeechService Speech { get; } = new();
    public AlertPlaybackController AlertPlayback { get; }
    public TimerEngine Engine { get; } = new();

    public ObservableCollection<TriggerTimer> Triggers { get; } = new();
    public AppSettings Settings { get; private set; } = new();

    /// True for the very first run after a fresh install (no settings.json on disk yet) —
    /// used to show the main window once even though StartMinimized defaults to true, so a
    /// first-time user isn't left wondering whether the install did anything.
    public bool IsFirstLaunch { get; private set; }

    private AppServices()
    {
        AlertPlayback = new AlertPlaybackController(Audio, Speech);
    }

    /// Loads settings and triggers, silently reconciling anything that was due while the
    /// app was closed (rescheduling recurring reminders, marking one-shots spent) instead
    /// of replaying a stale alert. Returns the names of triggers that were reconciled this way.
    public List<string> Initialize()
    {
        AppPaths.EnsureDirectories();

        IsFirstLaunch = !File.Exists(AppPaths.SettingsFile);

        Settings = SettingsRepository.Load();
        Settings.PropertyChanged += OnSettingsChanged;
        ApplySpeechSettings();

        // Defaults are launch-on-startup enabled, but that default alone doesn't create the
        // registry key — only an explicit change through Settings does. Create it here so a
        // fresh install actually launches on startup, matching what the toggle already shows.
        if (IsFirstLaunch && Settings.LaunchOnWindowsStartup)
            StartupRegistration.SetEnabled(true);

        var missed = new List<string>();
        var now = DateTime.Now;

        foreach (var t in TriggerRepository.Load())
        {
            WireItem(t);
            Triggers.Add(t);

            if (t.Enabled && !t.HasFired && t.TriggerAt <= now)
            {
                if (t.Recurrence is not null)
                    t.Recurrence.OccurrencesSoFar++;
                TriggerReconciler.AdvancePastFiring(t, now);
                missed.Add(t.Name);
            }
        }

        var removedSpent = 0;
        if (Settings.AutoDeleteSpentAfterDays > 0)
        {
            var cutoff = now.AddDays(-Settings.AutoDeleteSpentAfterDays);
            var stale = Triggers.Where(t => t.HasFired && t.TriggerAt < cutoff).ToList();
            foreach (var t in stale)
                Triggers.Remove(t);
            removedSpent = stale.Count;
        }

        Engine.SetTriggers(Triggers);
        if (missed.Count > 0 || removedSpent > 0)
            TriggerRepository.Save(Triggers);

        return missed;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        SettingsRepository.Save(Settings);
        if (e.PropertyName is nameof(AppSettings.TtsVoiceName) or nameof(AppSettings.TtsRate) or nameof(AppSettings.TtsVolume))
            ApplySpeechSettings();
    }

    public void ApplySpeechSettings()
    {
        Speech.SetVoice(Settings.TtsVoiceName);
        Speech.SetRate(Settings.TtsRate);
        Speech.SetVolume(Settings.TtsVolume);
    }

    private void WireItem(TriggerTimer t) => t.PropertyChanged += (_, e) =>
    {
        // Bulk-select checkboxes are UI-only state — don't churn a disk write for every click.
        if (e.PropertyName != nameof(TriggerTimer.IsSelectedForBulk))
            RefreshTriggers();
    };

    public void AddTrigger(TriggerTimer t)
    {
        WireItem(t);
        Triggers.Add(t);
        RefreshTriggers();
    }

    public void RemoveTrigger(TriggerTimer t)
    {
        Triggers.Remove(t);
        RefreshTriggers();
    }

    public void RemoveTriggers(IEnumerable<TriggerTimer> items)
    {
        foreach (var t in items.ToList())
            Triggers.Remove(t);
        RefreshTriggers();
    }

    public void RefreshTriggers()
    {
        Engine.SetTriggers(Triggers);
        TriggerRepository.Save(Triggers);
    }

    public string ResolveSoundPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;
        return Path.Combine(AppPaths.SoundsDirectory, fileName);
    }

    public List<string> GetAvailableSoundFiles()
    {
        if (!Directory.Exists(AppPaths.SoundsDirectory))
            return new List<string>();

        return Directory.EnumerateFiles(AppPaths.SoundsDirectory)
            .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Exports the given reminders (or all of them, if none specified) to a JSON file.
    public void ExportTriggers(string path, IEnumerable<TriggerTimer>? subset = null) =>
        TriggerRepository.SaveTo(path, subset ?? Triggers);

    /// Imports triggers from a previously exported JSON file, merging into the existing
    /// list. When an imported reminder's name matches an existing one, <paramref name="confirmReplace"/>
    /// is asked whether to replace it; a "no" leaves the existing reminder untouched. Imported
    /// items always get fresh ids so they never collide with existing ones.
    public ImportResult ImportTriggersMerge(string path, Func<string, bool> confirmReplace)
    {
        var imported = TriggerRepository.LoadFrom(path);
        int added = 0, replaced = 0, skipped = 0;

        foreach (var t in imported)
        {
            var existing = Triggers.FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                t.Id = Guid.NewGuid();
                AddTrigger(t);
                added++;
            }
            else if (confirmReplace(t.Name))
            {
                RemoveTrigger(existing);
                t.Id = Guid.NewGuid();
                AddTrigger(t);
                replaced++;
            }
            else
            {
                skipped++;
            }
        }

        return new ImportResult(added, replaced, skipped);
    }

    /// Exports settings and reminders together, for moving the whole app to another computer.
    public void ExportBackup(string path) =>
        BackupRepository.Save(path, new AppBackup { Settings = Settings, Triggers = Triggers.ToList() });

    /// Replaces all reminders with the ones in the backup and returns it so the caller can
    /// also apply its settings. Throws if the file isn't a valid Pendulum backup.
    public AppBackup RestoreBackup(string path)
    {
        var backup = BackupRepository.Load(path)
            ?? throw new InvalidOperationException("That file isn't a valid Pendulum backup.");

        RemoveTriggers(Triggers);
        foreach (var t in backup.Triggers)
        {
            t.Id = Guid.NewGuid();
            AddTrigger(t);
        }

        return backup;
    }

    public void Shutdown()
    {
        Engine.Dispose();
        AlertPlayback.Dispose();
        Audio.Dispose();
        Speech.Dispose();
    }
}
