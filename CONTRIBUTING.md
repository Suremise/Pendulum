# Contributing to Pendulum

Thanks for helping. Bug reports, fixes and ideas are all welcome. Please report security problems
privately, as [SECURITY.md](SECURITY.md) says, not in a public issue.

For anything bigger than a small fix, open an issue first so we can agree on the shape before you
spend time on it. Pendulum should stay out of the way until a reminder is due: a change should keep
it quick, quiet and dependable.

## Building and checking

You need Windows 10 or 11 and the .NET SDK the projects target (the `TargetFramework` in each
`.csproj`). [Inno Setup 6](https://jrsoftware.org/isinfo.php) builds the installer.

```
dotnet build Pendulum.sln -c Debug   # day to day
dotnet build Pendulum.sln -c Release
dotnet test Pendulum.sln             # every test project in the solution
.\build-installer.ps1                # installer\Output\PendulumSetup-<version>.exe
```

The build is self-contained for win-x64: `Pendulum.App\bin\<Configuration>\<framework>\win-x64\`
holds `Pendulum.exe`, and a run from there keeps its data in a `Data\` folder beside it, apart from
an installed copy's. Only one Pendulum runs at a time: a second launch shows the copy already
running, so close an installed Pendulum (from its tray icon) before running a build.

## How the code is laid out

```
Pendulum.Core/
  Models/        reminders, recurrence rules, alert modes and settings
  Engine/        the timer engine, recurrence, and what happens to a reminder once it fires
  Parsing/       Quick Reminder's plain-English parser ("call mom 3pm", "in 20 min")
  Audio/         sound playback (NAudio)
  Speech/        speech in (Windows, Whisper) and out (Windows, Piper)
  Persistence/   where the data lives, and saving, exports and backups as JSON
  Services/      the update check
Pendulum.App/
  Views/, ViewModels/, Converters/   the windows and panels (WPF-UI, CommunityToolkit.Mvvm)
  Services/      app wiring, alerts, the tray, the hotkey, theme, crash log, launch on startup
  Resources/     the icon and the bundled sounds
installer/       the Inno Setup script
```

**Pendulum.Core has no UI**: it references no WPF and shows no windows, so its logic can be tested
on its own. Put anything that decides what happens (when a reminder fires, what a phrase means,
what is saved) in Core, and keep Pendulum.App to showing it.

## Dependencies: the licence rule

Everything Pendulum ships must be **MIT or equally permissive**: MIT, Apache-2.0, BSD-2-Clause,
BSD-3-Clause, ISC, Zlib, 0BSD, BSL-1.0, CC0-1.0 or Unicode-3.0, or a dual licence that includes
one of them. **No copyleft and no extra conditions**: no GPL, LGPL, MPL, EPL, OFL and the like, and
**system fonts only**. That covers NuGet packages and everything they bring with them (native
libraries included), sounds and icons.

- Before adding a package, check its licence and its dependencies'. Prefer a well-used, maintained
  one, and say in the pull request what it's for and why it's worth it.
- Add it to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and update the entry when a
  package's version or licence changes.
- Build-only tools (the SDK, Inno Setup, CI actions, scripts) don't ship, so the rule doesn't
  apply to them.

## Tests

- **Tests use temporary folders only.** A test never reads or writes your real Pendulum data, the
  registry or your sounds, and cleans up after itself even when it fails.
- **Tests wait for conditions, never for time,** and pass under load, beside every other test.
  Pass the time into the code under test rather than reading the clock.
- **No network:** Pendulum makes no network calls beyond its update check, and its tests make none.
- **No global keyboard or mouse input,** ever: a test drives only its own windows, through UI
  Automation on its own process or messages to its own window.
- A fix comes with a test that fails without it, where the code can be tested.

## Commits and pull requests

- **A commit message's first line says what changed, in plain English** ("Snooze keeps a weekly
  reminder's day"), under about 72 characters. Add a body when the why isn't obvious. Keep issue
  and pull request numbers, people's names and review rounds out of commit messages: each pull
  request lands on master as one squashed commit whose message stays readable on its own, so link
  the issue it fixes in the pull request's description instead.
- Keep a pull request to one change, and update the README for anything it describes.
- **Add an entry under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md)** for anything a user
  would notice, in plain words.
- A change to what's on screen comes with before and after screenshots.
- By contributing, you agree that your contribution is under Pendulum's [MIT licence](LICENSE).
