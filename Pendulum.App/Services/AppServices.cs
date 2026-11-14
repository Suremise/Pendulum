using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Pendulum.App.Resources;
using Pendulum.Core.Audio;
using Pendulum.Core.Engine;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;
using Pendulum.Core.Services;
using Pendulum.Core.Speech;

namespace Pendulum.App.Services;

/// <param name="CaughtUp">Of those added or replaced, how many were already past their time: moved
/// on to their next occurrence, or marked spent, so they don't all ring at once.</param>
public readonly record struct ImportResult(int Added, int Replaced, int Skipped, int CaughtUp);

/// Single shared instance holding the app's services and live state.
/// Deliberately simple (no DI container) given the app's size.
public sealed class AppServices
{
    public static AppServices Instance { get; } = new(persistReminders: false);

    public TriggerRepository TriggerRepository { get; }
    public SettingsRepository SettingsRepository { get; } = new();
    public TimerEngine Engine { get; }

    public ReminderStore Reminders { get; } = new();
    public ReadOnlyObservableCollection<TriggerTimer> Triggers => Reminders.Items;
    public AppSettings Settings { get; private set; } = new();

    /// Data files that couldn't be read as they were at startup: each was set aside, and either
    /// the save before it was used or Pendulum started without it. The App tells the user.
    public List<DataFileNotice> DataFileNotices { get; } = new();

    private readonly DebouncedSave<(string Path, JsonElement Value)> _settingsSave;
    private readonly DebouncedSave<(string Path, JsonElement Value)> _triggersSave;
    private bool _persistReminders;

    /// Explicit opt-out for the in-memory UI tests and renderer. Never called by the app.
    internal void UseRemindersForTests() => _persistReminders = false;

    /// For tests only: settings loaded from a test's own file, without Initialize (which reads
    /// the app's Data\ and can write the Run key). With saveOnChange, each change saves as in the
    /// app, through the same batching. Never called by the app.
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

    internal AppServices(Action<string, JsonElement>? write = null, TimeProvider? timeProvider = null,
        TriggerRepository? triggerRepository = null, bool persistReminders = true)
    {
        TriggerRepository = triggerRepository ?? new();
        Engine = new(Reminders.ChangeTogether);
        _persistReminders = persistReminders;
        write ??= (path, value) => JsonStore.Save(path, value);
        void Dispatch(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
        void Report(Exception error) => CrashLog.Write("Save", error);
        _settingsSave = new(() => (AppPaths.SettingsFile, JsonSerializer.SerializeToElement(Settings)),
            snapshot => write(snapshot.Path, snapshot.Value), Dispatch, Report, timeProvider);
        _triggersSave = new(() => (TriggerRepository.FilePath, JsonSerializer.SerializeToElement(Triggers.ToList())),
            snapshot => write(snapshot.Path, snapshot.Value), Dispatch, Report, timeProvider);
        Reminders.RemindersChanged += OnRemindersChanged;
    }

    /// Loads settings and triggers, silently reconciling anything that was due while the
    /// app was closed (rescheduling recurring reminders, marking one-shots spent) instead
    /// of replaying a stale alert. Returns the names of triggers that were reconciled this way.
    public List<string> Initialize()
    {
        _persistReminders = false;
        AppPaths.EnsureDirectories();

        IsFirstLaunch = !File.Exists(AppPaths.SettingsFile);

        JustInstalledOrUpdated = !File.Exists(AppPaths.PostInstallMarkerFile);
        File.WriteAllText(AppPaths.PostInstallMarkerFile, string.Empty);

        var settingsLoad = SettingsRepository.LoadWithOutcome();
        Settings = settingsLoad.Value ?? new AppSettings();
        NoteProblem(AppPaths.SettingsFile, settingsLoad);
        Settings.PropertyChanged += (_, _) => SaveUnlessBatched();

        // Defaults are launch-on-startup enabled, but that default alone doesn't create the
        // registry value — only an explicit change through Settings does. Create it here so a
        // fresh install actually launches on startup, unless another copy's entry is already
        // there or this isn't an installed copy; the toggle then shows what really happens.
        if (IsFirstLaunch && Settings.LaunchOnWindowsStartup)
            Settings.LaunchOnWindowsStartup = StartupRegistration.RegisterOnFirstLaunch();

        var now = DateTime.Now;
        var triggersLoad = TriggerRepository.LoadWithOutcome();
        var loaded = triggersLoad.Value ?? new List<TriggerTimer>();
        NoteProblem(TriggerRepository.FilePath, triggersLoad);
        var missed = TriggerReconciler.CatchUpMissed(loaded, now).Select(t => t.Name).ToList();

        var removedSpent = 0;
        if (Settings.AutoDeleteSpentAfterDays > 0)
        {
            var cutoff = now.AddDays(-Settings.AutoDeleteSpentAfterDays);
            removedSpent = loaded.RemoveAll(t => t.HasFired && t.TriggerAt < cutoff);
        }

        Reminders.ReplaceAll(loaded, markDirty: false);
        // Missing, clean and recovered/set-aside files all complete initialization. An
        // unrecoverable read throws before here, leaving persistence disabled.
        _persistReminders = true;
        if (missed.Count > 0 || removedSpent > 0)
            _triggersSave.MarkDirty();

        return missed;
    }

    private int _settingsBatch;

    private void SaveUnlessBatched()
    {
        if (_settingsBatch == 0)
            _settingsSave.MarkDirty();
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
            _settingsSave.MarkDirty();
    }

    public string PiperExecutablePath => Path.Combine(AppPaths.PiperModelsDirectory, "piper.exe");

    public bool IsPiperExecutableAvailable => File.Exists(PiperExecutablePath);

    /// Builds a fresh, disposable text-to-speech engine matching Settings.TextToSpeechEngine,
    /// configured with the current voice/rate/volume settings. Falls back to the Windows engine
    /// if Piper is selected but not actually usable yet (missing piper.exe, or no voice model
    /// chosen/present) — callers never need to check availability themselves.
    public ITextToSpeechEngine CreateSpeechEngine() => CaptureSpeechEngineFactory()();

    /// One owned, lazy playback stack for either an alert or an editor preview.
    public AlertPlaybackController CreatePlayback(Action<AlertProblem> onProblem) => CreatePlayback(
        onProblem, () => new AudioService(), CaptureWindowsSpeechEngineFactory(),
        (exe, model, rate, volume) => new PiperSpeechService(exe, model, rate, volume));

    internal AlertPlaybackController CreatePlayback(Action<AlertProblem> onProblem,
        Func<IAlertSound> audio, Func<ITextToSpeechEngine> windows,
        Func<string, string, float, float, ITextToSpeechEngine> piper) => new(
            audio, CaptureSpeechSelection(windows, piper), onProblem);

    /// Captures settings on the UI thread; file checks and voice/device setup happen only when
    /// the worker needs speech. An alert keeps these settings throughout its life.
    public Func<ITextToSpeechEngine> CaptureSpeechEngineFactory()
    {
        var choose = CaptureSpeechSelection(CaptureWindowsSpeechEngineFactory(),
            (exe, model, rate, volume) => new PiperSpeechService(exe, model, rate, volume));
        return () => choose().Primary();
    }

    private Func<SpeechEngineSelection> CaptureSpeechSelection(Func<ITextToSpeechEngine> windows,
        Func<string, string, float, float, ITextToSpeechEngine> piper)
    {
        var selected = Settings.TextToSpeechEngine;
        var executable = PiperExecutablePath;
        var models = AppPaths.PiperModelsDirectory;
        var modelName = Settings.PiperVoiceModelFileName;
        var speakingRate = 1f - (Settings.TtsRate / 20f);
        var volume = Settings.TtsVolume / 100f;
        return () =>
        {
            if (selected == TextToSpeechEngine.Piper && File.Exists(executable)
                && DataFileName.PathInside(models, modelName) is { } modelPath && File.Exists(modelPath))
                return new SpeechEngineSelection(() => piper(executable, modelPath, speakingRate, volume), windows);
            return new SpeechEngineSelection(windows);
        };
    }

    /// The Windows voice with the current voice, rate and volume: what speaks when Piper can't.
    public ITextToSpeechEngine CreateWindowsSpeechEngine() => CaptureWindowsSpeechEngineFactory()();

    public Func<ITextToSpeechEngine> CaptureWindowsSpeechEngineFactory()
    {
        var voice = Settings.TtsVoiceName;
        var rate = Settings.TtsRate;
        var volume = Settings.TtsVolume;
        return () =>
        {
            var speech = new SpeechService();
            try
            {
                speech.SetVoice(voice);
                speech.SetRate(rate);
                speech.SetVolume(volume);
                return speech;
            }
            catch { speech.Dispose(); throw; }
        };
    }

    private void NoteProblem<T>(string file, StoreLoad<T> load) where T : class
    {
        if (load.Outcome is StoreOutcome.RecoveredFromBackup or StoreOutcome.SetAside)
            DataFileNotices.Add(new DataFileNotice(file, load.Outcome, load.SetAsidePath));
    }

    private void OnRemindersChanged(object? sender, RemindersChangedEventArgs e)
    {
        if (e.MembershipChanged || e.Persisted) Engine.SetTriggers(Triggers);
        if (e.Persisted && _persistReminders) _triggersSave.MarkDirty();
    }

    public void AddTrigger(TriggerTimer t) => Reminders.Add(t);
    public void RemoveTrigger(TriggerTimer t) => Reminders.Remove(t);
    public void RemoveTriggers(IEnumerable<TriggerTimer> items) => Reminders.RemoveRange(items);
    public void ChangeRemindersTogether(Action change) => Reminders.ChangeTogether(change);
    public bool Update(TriggerTimer reminder, Action change) => Reminders.Update(reminder, change);

    /// The full path of a sound in Sounds\, or empty for none. A name that isn't a plain file
    /// name there (a full or network path, or "..") is never used: it plays no sound.
    public string ResolveSoundPath(string? fileName) =>
        DataFileName.PathInside(AppPaths.SoundsDirectory, fileName) ?? string.Empty;

    public List<string> GetAvailableSoundFiles() =>
        DataFileName.ListIn(AppPaths.SoundsDirectory, ".wav", ".mp3"); // not user-visible: file types

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
        // Collect decisions first. Prompts can pump the UI, so they must not expose a partially
        // imported list to a pending save or overwrite independent live changes on return.
        var preview = Triggers.ToList();
        var accepted = new List<(TriggerTimer Item, TriggerTimer? Replaced)>();
        int added = 0, replaced = 0, skipped = 0;

        foreach (var t in imported)
        {
            var existing = preview.FirstOrDefault(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                t.Id = Guid.NewGuid();
                preview.Add(t);
                accepted.Add((t, null));
                added++;
            }
            else if (confirmReplace(t.Name))
            {
                preview.Remove(existing);
                t.Id = Guid.NewGuid();
                preview.Add(t);
                accepted.Add((t, existing));
                replaced++;
            }
            else
            {
                skipped++;
            }
        }

        var caughtUp = TriggerReconciler.CatchUpMissed(imported, DateTime.Now).ToHashSet();
        if (accepted.Count > 0)
            ChangeRemindersTogether(() =>
            {
                var committed = Triggers.ToList();
                foreach (var decision in accepted)
                {
                    var matched = committed.FindIndex(item => ReferenceEquals(item, decision.Replaced));
                    if (matched >= 0) committed.RemoveAt(matched);
                    committed.Add(decision.Item);
                }
                Reminders.ReplaceAll(committed, preserveExisting: true);
            });
        return new ImportResult(added, replaced, skipped, accepted.Count(d => caughtUp.Contains(d.Item)));
    }

    /// Exports settings and reminders together, for moving the whole app to another computer.
    public void ExportBackup(string path) =>
        BackupRepository.Save(path, new AppBackup { Settings = Settings, Triggers = Triggers.ToList() });

    /// Replaces all reminders with the ones in the backup and returns it so the caller can
    /// also apply its settings. Throws if the file isn't a valid Pendulum backup.
    public AppBackup RestoreBackup(string path, out int caughtUp)
    {
        AppBackup backup;
        try
        {
            backup = BackupRepository.Load(path);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidOperationException(Strings.Backup_NotValid, e);
        }

        // Before any reaches the engine: ones already past their time move on, or are spent,
        // instead of all ringing at once.
        caughtUp = TriggerReconciler.CatchUpMissed(backup.Triggers, DateTime.Now).Count;

        // Build the full replacement in memory and persist once at the end, instead of wiping
        // triggers.json to empty and then saving again after every single re-added item — a
        // crash partway through used to leave the user with an empty or partially-restored file.
        // With a single save at the end, a crash before it completes leaves the pre-restore file
        // on disk untouched.
        foreach (var t in backup.Triggers)
            t.Id = Guid.NewGuid();

        Reminders.ReplaceAll(backup.Triggers);

        return backup;
    }

    public void FlushPendingSaves()
    {
        // Try both files even if one cannot be saved. Flush reports failures to the caller.
        try { _settingsSave.Flush(); }
        finally { _triggersSave.Flush(); }
    }

    public void Shutdown()
    {
        Engine.Dispose();
        Reminders.RemindersChanged -= OnRemindersChanged;
        Reminders.Dispose();
        try { _settingsSave.Dispose(); }
        finally { _triggersSave.Dispose(); }
    }
}
