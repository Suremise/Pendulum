# Pendulum

A tray-resident Windows desktop timer and alarm utility. Pendulum stays out of the way in the system tray and surfaces alerts as a topmost popup with sound and/or spoken-phrase notifications, even while you're working in another window.

## Features

- **Reminders** — alarms set for an exact date and time, with sound, text-to-speech, or both. Supports Outlook-style recurrence (daily/weekly/monthly/yearly), bulk select/delete/export, and reminders-only or whole-app backup & restore.
- **Stopwatch** — standard count-up stopwatch with start/pause/reset.
- **Countdown** — configurable days/hours/minutes/seconds countdown, with the same alert options as Reminders.
- **Settings** — sound library management, TTS voice/rate/volume, alert style, snooze duration, an auto-resolve timeout for unanswered alerts, 12/24-hour time format, and launch-on-startup.
- **Tray integration** — minimizes to the system tray instead of closing; the tray icon's tooltip shows the next upcoming reminder.
- **Resilient by design** — reminders that come due while the app is closed, or that go unanswered, resolve themselves sensibly (reschedule if recurring, mark spent otherwise) instead of getting stuck.

## Getting Pendulum

Two ways to run it, both self-contained (no separate .NET install required):

- **Portable** — no install, no admin rights, zero footprint outside its own folder. Grab the `Pendulum-portable-win-x64` build, unzip anywhere, run `Pendulum.exe`. All state lives in a `Data\` folder created next to the exe.
- **Installer** — a proper `PendulumSetup-<version>.exe` that installs per-user (no admin/UAC prompt), adds a Start Menu entry and optional desktop shortcut, and registers a normal uninstaller.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet build Pendulum.sln -c Release
```

Produces a self-contained win-x64 build at `Pendulum.App\bin\Release\net8.0-windows\win-x64\`. This is the fast day-to-day dev loop — use `-c Debug` while iterating.

To cut a distributable release:

```
.\build-portable.ps1     # -> dist\Pendulum-portable-win-x64\
.\build-installer.ps1    # -> installer\Output\PendulumSetup-<version>.exe (requires Inno Setup 6)
```

## Project structure

- **Pendulum.App** — WPF UI (WPF-UI Fluent design), MVVM via CommunityToolkit.Mvvm, tray icon via H.NotifyIcon.
- **Pendulum.Core** — models, the timer engine, recurrence calculation, audio (NAudio) and speech (System.Speech/SAPI5) services, and JSON persistence. No UI dependencies.

## Portable data model

No `%AppData%`, no registry, no installer footprint (aside from the one opt-in "Launch on Windows startup" toggle, which writes a single registry `Run` key). Everything lives next to the exe:

```
Pendulum.exe
Data\
  settings.json
  triggers.json
Sounds\
  chime.wav, bell.wav, ...
```

---

Built by [](mailto:) with Claude (Sonnet 5).

**Surmise Software** — **Sure**mised it right. — [surmise.it](https://surmise.it)
