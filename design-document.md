# Pendulum — Design Document

Status: **Architecture agreed, implementation not started.**
Stack decision: **C# / .NET 8 (WPF)** — chosen over Python (PySide6) and Electron for native Windows integration, offline TTS, small/fast single-file exe, and no PyInstaller-style AV false positives.

---

## 1. What Pendulum is

A tray-resident Windows desktop app with three independent timing tools:

1. **Fixed Triggers** — alarms/reminders set for an exact date & time (defaults to "now", adjustable via a calendar + time picker). Each can play a sound, speak a phrase via TTS, or both.
2. **Stopwatch** — standard count-up stopwatch (start/pause/reset, optional laps).
3. **Countdown** — count-down timer configured as days/hours/minutes/seconds, independent from Fixed Triggers.

The app lives in the system tray when closed (close = minimize to tray); only **right-click → Exit** actually terminates it.

---

## 2. Why .NET / WPF

| Requirement | How .NET/WPF satisfies it |
|---|---|
| Tray icon, minimize-to-tray | `H.NotifyIcon.Wpf` (actively maintained tray-icon control for modern WPF) |
| Calendar/time picker | WPF `Calendar`/`DatePicker` built in; time-of-day picker via Xceed WPF Toolkit (free) or a small custom control |
| Offline TTS | `System.Speech.Synthesis` — wraps Windows SAPI5, no internet/cloud dependency, ships with Windows |
| Sound playback (wav/mp3) | `NAudio` — lightweight, handles WAV natively and MP3 via `MediaFoundationReader` |
| Always-on-top alert window | Native WPF `Topmost` + Win32 `SetWindowPos(HWND_TOPMOST)` — no hooks/injection |
| Professional look | `WPF-UI` (Fluent Design System library) — Windows 11-style controls, light/dark theme support out of the box |
| Single .exe output | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` |
| No AV false positives | Native compiled/JIT binary, not a PyInstaller-style bundled interpreter (which antivirus heuristics often flag) |

Python (PySide6) was the close second — natural fit given the existing Python workspace — but loses on: no built-in time picker, heavier/slower-starting PyInstaller exe, occasional AV flagging, and TTS via `pyttsx3` being a thinner wrapper around the same SAPI5 engine .NET accesses directly. Electron was ruled out primarily on exe size (~150-200MB) and RAM footprint for what is essentially a lightweight utility.

---

## 3. Project structure

```
Pendulum/
├── Pendulum.App/              WPF UI project (startup, views, viewmodels, tray)
│   ├── App.xaml / App.xaml.cs         app entry, tray lifecycle, single-instance guard
│   ├── Views/
│   │   ├── MainWindow.xaml             shell: nav between panels
│   │   ├── TriggersPanel.xaml          fixed-trigger list + editor
│   │   ├── StopwatchPanel.xaml
│   │   ├── CountdownPanel.xaml
│   │   ├── SettingsPanel.xaml
│   │   └── AlertWindow.xaml            the topmost popup shown when a timer fires
│   ├── ViewModels/                     MVVM (CommunityToolkit.Mvvm)
│   └── Resources/                      icon.ico, default sounds, styles
│
├── Pendulum.Core/              class library, no UI dependencies
│   ├── Models/                 TriggerTimer, CountdownTimer, AppSettings
│   ├── Engine/                 TimerEngine (polls due timers), StopwatchEngine
│   ├── Audio/                  AudioService (NAudio wrapper)
│   ├── Speech/                 SpeechService (System.Speech wrapper)
│   ├── Notifications/          AlertPresenter (creates/positions AlertWindow), ToastFallback
│   └── Persistence/             JsonStore (reads/writes %AppData%\Pendulum\*.json)
│
└── Pendulum.Tests/             unit tests for Engine/Persistence
```

MVVM via **CommunityToolkit.Mvvm** (`ObservableObject`, `RelayCommand`) keeps viewmodels testable and decoupled from XAML.

---

## 4. Data model

```csharp
class TriggerTimer
{
    Guid Id;
    string Name;
    DateTime TriggerAt;          // defaults to DateTime.Now at creation, editable via calendar+time picker
    bool Enabled;
    AlertMode Mode;               // SoundOnly | SoundAndSpeech
    string? SoundFilePath;        // null = silent / phrase-only
    string? Phrase;               // spoken via TTS when Mode includes speech
    RecurrenceRule? Recurrence;   // null = one-shot; optional future: Daily/Weekly/Weekdays
}

class CountdownTimer
{
    Guid Id;
    string Label;
    TimeSpan Duration;            // days/hours/minutes/seconds as entered
    AlertMode Mode;
    string? SoundFilePath;
    string? Phrase;
}

enum AlertMode { SoundOnly, SoundAndSpeech }

class AppSettings
{
    string SoundsFolderPath;      // default: %AppData%\Pendulum\Sounds
    string DefaultSoundFile;
    string TtsVoiceName;
    int TtsRateAndVolume...;
    bool LaunchOnWindowsStartup;
    bool UseTopmostAlertWindow;   // vs. native toast only
    int SnoozeMinutes;
    ThemeMode Theme;              // Light | Dark | System
}
```

**Portable storage**: no `%AppData%`, no registry, no installer. All state lives in folders next to the exe, resolved at runtime via `AppContext.BaseDirectory`:

```
Pendulum\
├── Pendulum.exe
├── Data\
│   ├── triggers.json
│   ├── countdowns.json
│   └── settings.json
└── Sounds\
    ├── chime.wav
    ├── bell.wav
    └── ...
```

Copy the `Pendulum` folder anywhere (including a USB stick), run the exe, and everything — config and sounds — travels with it. `Data\` and `Sounds\` are created on first run if missing (seeded with a couple of default tones shipped in the release). No database needed at this scale — plain JSON is also easy to hand-edit or back up by just copying the folder.

---

## 5. Sounds

- **Folder**: the `Sounds\` subfolder next to the exe (path overridable in Settings if you ever want to point elsewhere), pre-seeded with a few default alert tones shipped as part of the portable release.
- **Formats supported**: **WAV** (uncompressed, zero-latency, always works) and **MP3** (common, small, user-friendly for custom tones). NAudio handles both without extra native dependencies. Settings panel has an "Open Sounds Folder" button and a "Add Sound…" file picker that copies the chosen file into that folder.
- Timers reference a sound by filename; Settings panel lists everything currently in the folder in a dropdown, auto-refreshed when the folder changes.

## 6. Text-to-Speech

- `System.Speech.Synthesis.SpeechSynthesizer` — fully offline, uses installed Windows SAPI5 voices.
- Settings panel: voice dropdown (populated from `synth.GetInstalledVoices()`), rate slider, volume slider, "Test Voice" button.
- Alert flow for `SoundAndSpeech`: play the sound (looped or single-shot per setting) → speak the phrase → repeat until dismissed/snoozed, matching typical alarm behavior.

## 7. Calendar / time selection

- Trigger editor defaults `TriggerAt` to `DateTime.Now` when a new timer is created.
- Date chosen via a popup `Calendar` control (click a field → flyout calendar, click a date → closes).
- Time chosen via a companion time-of-day control (hour/minute/AM-PM steppers, or Xceed `TimePicker`) next to the date field.
- Countdown panel uses four numeric inputs (days/hours/minutes/seconds) instead of a date, since it's relative, not absolute.

## 8. Stopwatch & Countdown panels

- **Stopwatch**: `Stopwatch` class (System.Diagnostics) driving a `DispatcherTimer` UI tick (~100ms) for display refresh. Start/Pause/Resume/Reset, optional Lap list.
- **Countdown**: user enters D/H/M/S, Start begins a `DateTime` target (`Now + duration`) so it survives UI tick drift; Pause captures remaining `TimeSpan`; on reaching zero, fires the same alert pipeline as a Fixed Trigger (sound/speech).
- Both panels are independent of the Fixed Triggers list — no persistence needed across restarts unless you later want "resume running countdown after relaunch" (easy to add: persist target DateTime).

## 9. Tray behavior

- `H.NotifyIcon.Wpf`'s `TaskbarIcon` bound in `App.xaml`.
- Closing `MainWindow` is intercepted (`Closing` event, `e.Cancel = true`) → `Hide()` instead, unless the close originated from the tray context menu's **Exit** command (a flag distinguishes the two paths).
- Tray context menu: **Open Pendulum**, separator, **Exit**.
- Tray icon tooltip shows next upcoming trigger, optionally.
- Single-instance enforcement via a named `Mutex` so relaunching the exe just focuses the existing tray app.

## 10. Alert / notification delivery — and the fullscreen question

**Can a notification be seen while a game or fullscreen app is running? Yes, in the majority of real-world cases, without any hook or overlay.**

Mechanism: `AlertWindow` is a plain WPF window (`WindowStyle=None`, `ShowInTaskbar=false`, `Topmost=true`), additionally reinforced via the Win32 API (`SetWindowPos` with `HWND_TOPMOST`) and re-asserted on a short timer in case another app steals the topmost flag. This is architecturally identical to any normal always-on-top utility (volume OSD, screenshot tools, etc.) — it is a separate top-level window, not code running inside another process.

Why this works for most games today: since Windows 10 (v1703+), **Fullscreen Optimizations** silently converts most DirectX 9-11 "exclusive fullscreen" games into a borderless-equivalent mode by default, specifically so the OS compositor (DWM) can draw things like notifications, Xbox Game Bar, and Alt-Tab over them. So the majority of modern games are *already* effectively borderless-fullscreen under the hood, even when the game itself claims "Fullscreen" — and a topmost window displays over them fine.

Where it can fail: a smaller set of older or anti-cheat-hardened titles force **true legacy exclusive fullscreen** (bypassing Fullscreen Optimizations, sometimes specifically to block overlays for anti-cheat integrity). In that narrow case, DWM doesn't composite anything over the game, including Windows' own notifications — a plain topmost window won't show either.

**Reaching that last case reliably requires overlay technique (DLL injection into the game's render/present pipeline, e.g. how Discord/Steam overlays work) — and that is exactly the pattern EAC/BattlEye/Vanguard flag or block.** It is not worth doing for an alarm/reminder app, both because of the anti-cheat risk and because it's a poor fit (injecting into arbitrary third-party processes is invasive for a personal productivity tool).

**Chosen approach:**
1. Primary: topmost `AlertWindow`, covering windowed, borderless, and the large majority of "fullscreen" games (via Fullscreen Optimizations).
2. Secondary/backup: also fire a native Windows toast notification (`Microsoft.Toolkit.Uwp.Notifications` / `CommunityToolkit.WinUI.Notifications`) in parallel — Windows' own notification pipeline gets similar compositor treatment and acts as a redundant channel, plus it's what shows in Action Center/notification history if the user was away.
3. Explicitly **not** implementing: any DLL injection, render-hook, or graphics API overlay. Not needed for the stated use case, and it's the one approach that risks anti-cheat false-positives.
4. The alert also plays sound/TTS regardless of window visibility, so even in the rare exclusive-fullscreen miss case, the audio cue still lands.

## 11. Icon / visual theme

Concept: a minimalist flat-Fluent pendulum-clock mark — a stylized swinging pendulum bob inside/below a simple clock arc, using a Windows-11-appropriate accent color (deep indigo/teal) with a light background variant for taskbar/tray contrast. Delivered as multi-resolution `.ico` (16/24/32/48/256px) embedded via the WPF project's `<ApplicationIcon>` and also used for the tray `TaskbarIcon`. Exact artwork to be produced during implementation (vector SVG source → rasterized PNGs → `.ico`), matching the app's light/dark theme so the tray glyph stays legible in both.

## 12. Settings panel — fields

- Sounds folder path (+ Open Folder / Add Sound)
- Default sound for new timers
- TTS voice, rate, volume (+ Test Voice)
- Snooze duration
- Launch on Windows startup (opt-in only; writes a Registry `Run` key pointing at the exe's *current* path — since the app is portable, moving the folder after enabling this means re-toggling it once at the new location)
- Alert style: Topmost window / Toast only / Both
- Theme: Light / Dark / Follow system

## 13. Packaging — portable, no installer, zero footprint on the machine

No MSI, no Setup.exe, no install step, no temp-folder extraction cache. Self-contained **multi-file** publish — every dependency, including the .NET runtime itself, sits as plain DLLs in the folder next to `Pendulum.exe`:

```
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false
```

Deliberately *not* using `PublishSingleFile` here: single-file mode still unpacks its bundled native libraries into a per-version cache under `%TEMP%\.net\...` on first run. Going multi-file instead means nothing is written anywhere outside the app's own folder — not `%TEMP%`, not `%AppData%`, not the registry — until you explicitly opt into "Launch on Windows startup" in Settings (the one deliberate exception, user-initiated and one line in the registry, cleanly reversible from the same toggle).

Release layout:

```
Pendulum\                  ← this whole folder is "the app"; zip it, copy it, run it anywhere
├── Pendulum.exe
├── Pendulum.dll                    (managed app code)
├── *.dll                           (all managed + native dependencies: WPF/.NET runtime,
│                                     H.NotifyIcon, NAudio, System.Speech, WPF-UI, etc.)
├── Pendulum.deps.json / .runtimeconfig.json
└── Sounds\                ← default tones, pre-seeded
    ├── chime.wav
    └── bell.wav
```

(`Data\` is not shipped — it's created on first launch, inside this same folder.)

Folder will be larger than a single-file build (~100-150MB vs ~60-100MB, since nothing is compressed into one exe) and looks busier with all the DLLs visible — the tradeoff for genuinely leaving nothing behind on the host machine. For distribution/testing, the release artifact is `Pendulum-portable-win-x64.zip` containing the folder above — unzip, run `Pendulum.exe`, no admin rights needed. Uninstalling is just deleting the folder.

---

## 14. Open items for implementation phase

- Final icon artwork.
- Recurrence rules for Fixed Triggers (one-shot only for v1, daily/weekly as a fast-follow).
- Whether Countdown should persist across app restarts.
- Exact time-picker control (Xceed toolkit vs. small custom control).
