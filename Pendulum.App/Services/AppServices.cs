using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Pendulum.App.Resources;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Speech;

namespace Pendulum.App.Services;

/// <param name="CaughtUp">Of those added or replaced, how many were already past their time: moved
/// on to their next occurrence, or marked spent, so they don't all ring at once.</param>
public readonly record struct ImportResult(int Added, int Replaced, int Skipped, int CaughtUp);

/// Single shared instance holding the app's services and live state.
/// Deliberately simple (no DI container) given the app's size.
public sealed class AppServices
{
    public static AppServices Instance { get; } = new();

    public TriggerRepository TriggerRepository { get; } = new();
    public SettingsRepository SettingsRepository { get; } = new();
    public AudioService Audio { get; } = new();
    public SpeechService Speech { get; } = new();
    public TimerEngine Engine { get; } = new();

    public ObservableCollection<TriggerTimer> Triggers { get; } = new();
    public AppSettings Settings { get; private set; } = new();

    /// Data files that couldn't be read as they were at startup: each was set aside, and either
    /// the save before it was used or Pendulum started without it. The App tells the user.
    public List<DataFileNotice> DataFileNotices { get; } = new();

    /// For tests only: settings loaded from a test's own file, without Initialize (which reads
    /// the app's Data\ and can write the Run key). With saveOnChange, each change saves as in the
    /// app, through the same batching, but the Windows voice isn't touched: SAPI can stall a
    /// test host. Never called by the app.
    internal void UseSettingsForTests(AppSettings settings, bool saveOnChange = false)
    {
        Settings = settings;
        if (saveOnChange)
            settings.PropertyChanged += (_, _) => SaveUnlessBatched();
    }

    /// True for the very first run after a fresh install (no settings.json on disk yet) —
    /// used to show the main window once even though StartMinimized defaults to true, so a
    /// first-time user isn't left wondering whether the install did anything.
    public bool IsFirstLaunch { get; private set; }

    /// True for the first launch after any install/reinstall/update — including over an
    /// existing install where Data\ (and IsFirstLaunch) already exists. The installer wipes
    /// AppPaths.PostInstallMarkerFile on every run, so its absence here means "setup just
    /// finished." Also used to force the window open once even if StartMinimized is on, so
    /// updating to a new version visibly confirms the install worked.
    public bool JustInstalledOrUpdated { get; private set; }

    private AppServices()
    {
    }

    /// Loads settings and triggers, silently reconciling anything that was due while the
    /// app was closed (rescheduling recurring reminders, marking one-shots spent) instead
    /// of replaying a stale alert. Returns the names of triggers that were reconciled this way.
    public List<string> Initialize()
    {
        AppPaths.EnsureDirectories();

        IsFirstLaunch = !File.Exists(AppPaths.SettingsFile);

        JustInstalledOrUpdated = !File.Exists(AppPaths.PostInstallMarkerFile);
        File.WriteAllText(AppPaths.PostInstallMarkerFile, string.Empty);

        var settingsLoad = SettingsRepository.LoadWithOutcome();
        Settings = settingsLoad.Value ?? new AppSettings();
        NoteProblem(AppPaths.SettingsFile, settingsLoad);
        Settings.PropertyChanged += OnSettingsChanged;
        ApplySpeechSettings();

        // Defaults are launch-on-startup enabled, but that default alone doesn't create the
        // registry value — only an explicit change through Settings does. Create it here so a
        // fresh install actually launches on startup, unless another copy's entry is already
        // there or this isn't an installed copy; the toggle then shows what really happens.
        if (IsFirstLaunch && Settings.LaunchOnWindowsStartup)
            Settings.LaunchOnWindowsStartup = StartupRegistration.RegisterOnFirstLaunch();

        var now = DateTime.Now;
        var triggersLoad = TriggerRepository.LoadWithOutcome();
        var loaded = triggersLoad.Value ?? new List<TriggerTimer>();
        NoteProblem(AppPaths.TriggersFile, triggersLoad);
        var missed = TriggerReconciler.CatchUpMissed(loaded, now);

        foreach (var t in loaded)
        {
            WireItem(t);
            Triggers.Add(t);
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

    private int _settingsBatch;

    private void SaveUnlessBatched()
    {
        if (_settingsBatch == 0)
            SettingsRepository.Save(Settings);
    }

    /// Makes many settings changes with one save at the end, so a restore or reset is never
    /// saved half done. Each change is still announced, so the screen and the app follow it.
    public void ChangeSettingsTogether(Action change)
    {
        _settingsBatch++;
        try
        {
            change();
        }
        finally
        {
            _settingsBatch--;
        }

        if (_settingsBatch == 0)
            SettingsRepository.Save(Settings);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveUnlessBatched();
        if (e.PropertyName is nameof(AppSettings.TtsVoiceName) or nameof(AppSettings.TtsRate) or nameof(AppSettings.TtsVolume))
            ApplySpeechSettings();
    }

    public void ApplySpeechSettings()
    {
        Speech.SetVoice(Settings.TtsVoiceName);
        Speech.SetRate(Settings.TtsRate);
        Speech.SetVolume(Settings.TtsVolume);
    }

    public string PiperExecutablePath => Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe");

    public bool IsPiperExecutableAvailable => File.Exists(PiperExecutablePath);

    /// Builds a fresh, disposable text-to-speech engine matching Settings.TextToSpeechEngine,
    /// configured with the current voice/rate/volume settings. Falls back to the Windows engine
    /// if Piper is selected but not actually usable yet (missing piper.exe, or no voice model
    /// chosen/present) — callers never need to check availability themselves.
    public ITextToSpeechEngine CreateSpeechEngine()
    {
        if (Settings.TextToSpeechEngine == TextToSpeechEngine.Piper
            && IsPiperExecutableAvailable
            && DataFileName.PathInside(AppPaths.PiperModelsDirectory, Settings.PiperVoiceModelFileName) is { } modelPath)
        {
            if (File.Exists(modelPath))
            {
                // SAPI's Rate is -10 (slowest) to +10 (fastest); Piper's length_scale is the
                // opposite shape (a multiplier where lower is faster, 1.0 is normal) — this maps
                // the same slider onto both rather than needing a second, Piper-only control.
                var speakingRate = 1f - (Settings.TtsRate / 20f);
                return new PiperSpeechService(PiperExecutablePath, modelPath, speakingRate, Settings.TtsVolume / 100f);
            }
        }

        return CreateWindowsSpeechEngine();
    }

    /// The Windows voice with the current voice, rate and volume: what speaks when Piper can't.
    public ITextToSpeechEngine CreateWindowsSpeechEngine()
    {
        var speech = new SpeechService();
        speech.SetVoice(Settings.TtsVoiceName);
        speech.SetRate(Settings.TtsRate);
        speech.SetVolume(Settings.TtsVolume);
        return speech;
    }

    private void NoteProblem<T>(string file, StoreLoad<T> load) where T : class
    {
        if (load.Outcome is StoreOutcome.RecoveredFromBackup or StoreOutcome.SetAside)
            DataFileNotices.Add(new DataFileNotice(file, load.Outcome, load.SetAsidePath));
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

    /// The full path of a sound in Sounds\, or empty for none. A name that isn't a plain file
    /// name there (a full or network path, or "..") is never used: it plays no sound.
    public string ResolveSoundPath(string? fileName) =>
        DataFileName.PathInside(AppPaths.SoundsDirectory, fileName) ?? string.Empty;

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
        // Before any reaches the engine: ones already past their time move on, or are spent.
        var caughtUp = TriggerReconciler.CatchUpMissedTriggers(imported, DateTime.Now).ToHashSet();
        int added = 0, replaced = 0, skipped = 0, caughtUpTaken = 0;

        foreach (var t in imported)
        {
            var existing = Triggers.FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                t.Id = Guid.NewGuid();
                AddTrigger(t);
                added++;
                if (caughtUp.Contains(t)) caughtUpTaken++;
            }
            else if (confirmReplace(t.Name))
            {
                RemoveTrigger(existing);
                t.Id = Guid.NewGuid();
                AddTrigger(t);
                replaced++;
                if (caughtUp.Contains(t)) caughtUpTaken++;
            }
            else
            {
                skipped++;
            }
        }

        return new ImportResult(added, replaced, skipped, caughtUpTaken);
    }

    /// Exports settings and reminders together, for moving the whole app to another computer.
    public void ExportBackup(string path) =>
        BackupRepository.Save(path, new AppBackup { Settings = Settings, Triggers = Triggers.ToList() });

    /// Replaces all reminders with the ones in the backup and returns it so the caller can
    /// also apply its settings. Throws if the file isn't a valid Pendulum backup.
    public AppBackup RestoreBackup(string path, out int caughtUp)
    {
        var backup = BackupRepository.Load(path)
            ?? throw new InvalidOperationException(Strings.Backup_NotValid);

        // Before any reaches the engine: ones already past their time move on, or are spent,
        // instead of all ringing at once.
        caughtUp = TriggerReconciler.CatchUpMissedTriggers(backup.Triggers, DateTime.Now).Count;

        // Build the full replacement in memory and persist once at the end, instead of wiping
        // triggers.json to empty and then saving again after every single re-added item — a
        // crash partway through used to leave the user with an empty or partially-restored file.
        // With a single save at the end, a crash before it completes leaves the pre-restore file
        // on disk untouched.
        foreach (var t in backup.Triggers)
            t.Id = Guid.NewGuid();

        Triggers.Clear();
        foreach (var t in backup.Triggers)
        {
            WireItem(t);
            Triggers.Add(t);
        }
        RefreshTriggers();

        return backup;
    }

    public void Shutdown()
    {
        Engine.Dispose();
        Audio.Dispose();
        Speech.Dispose();
    }
}
