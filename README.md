# Pendulum

A tray-resident Windows desktop timer and alarm utility. Pendulum stays out of the way in the system tray and surfaces alerts as a topmost popup with sound and/or spoken-phrase notifications, even while you're working in another window.

## Features

- **Reminders** — alarms set for an exact date and time, with sound, text-to-speech, or both. Supports Outlook-style recurrence (daily/weekly/monthly/yearly), bulk select/delete/export, and reminders-only or whole-app backup & restore.
- **Calendar** — a month view of every reminder, with navigable months/years and click-to-edit on any reminder shown on it.
- **Settings** — sound library management, speech-to-text (Windows or a locally-run Whisper model) for the Quick Reminder mic button, text-to-speech (Windows or a locally-run Piper voice), alert style, snooze duration, an auto-resolve timeout for unanswered alerts, 12/24-hour time format, and launch-on-startup.
- **Tray integration** — minimizes to the system tray instead of closing; the tray icon's tooltip shows the next upcoming reminder.
- **Resilient by design** — reminders that come due while the app is closed, or that go unanswered, resolve themselves sensibly (reschedule if recurring, mark spent otherwise) instead of getting stuck.

## Getting Pendulum

A self-contained `PendulumSetup-<version>.exe` (no separate .NET install required) that installs per-user (no admin/UAC prompt), adds a Start Menu entry and optional desktop shortcut, and registers a normal uninstaller.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet build Pendulum.sln -c Release
```

Produces a self-contained win-x64 build at `Pendulum.App\bin\Release\net8.0-windows\win-x64\`. This is the fast day-to-day dev loop — use `-c Debug` while iterating.

To cut a distributable release:

```
.\build-installer.ps1    # -> installer\Output\PendulumSetup-<version>.exe (requires Inno Setup 6)
```

## Project structure

- **Pendulum.App** — WPF UI (WPF-UI Fluent design), MVVM via CommunityToolkit.Mvvm, tray icon via H.NotifyIcon.
- **Pendulum.Core** — models, the timer engine, recurrence calculation, audio (NAudio) and speech (System.Speech/SAPI5) services, and JSON persistence. No UI dependencies.

## Data model

No `%AppData%`, no registry, no installer footprint (aside from the one opt-in "Launch on Windows startup" toggle, which writes a single registry `Run` key). Everything lives next to the exe:

```
Pendulum.exe
Data\
  settings.json
  triggers.json
  WhisperModels\   (Whisper ggml models, downloaded separately — see Settings)
  PiperModels\     (Piper engine + voice models, downloaded separately — see Settings)
Sounds\
  chime.wav, bell.wav, ...
```

## Third-party software

Built on several open-source libraries, and optionally on the separately-downloaded
Whisper and Piper speech engines — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
for the full list and licenses (all MIT).

---

**Surmise Software** — **Sure**mised it right. — [surmise.it](https://surmise.it)
