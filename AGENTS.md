# Pendulum: notes for agents

Pendulum is a tray-resident reminder and alarm app for Windows: C# and WPF on .NET, released on
GitHub and in daily use. These notes are for every agent working on this repository, whichever
CLI runs it: Codex reads this file, and Claude Code reads it through `CLAUDE.md`.

- Building, the layout, the licence rule and commits, for everyone: [CONTRIBUTING.md](CONTRIBUTING.md)
- What users see changed: [CHANGELOG.md](CHANGELOG.md). Security reports: [SECURITY.md](SECURITY.md)
- Releases (the tag, the workflow, and the manual checklist before a draft is published):
  [docs/RELEASING.md](docs/RELEASING.md). `build-installer.ps1` builds the release's files into
  `dist\`, locally and in the workflow alike.

## The layout

```
Pendulum.Core/   no UI: the models, the timer engine and recurrence, the plain-English
                 Quick Reminder parser, audio (NAudio), speech in and out (System.Speech,
                 Whisper.net, Piper), JSON persistence and backups, the update check
Pendulum.App/    the WPF app (WPF-UI, CommunityToolkit.Mvvm, H.NotifyIcon): windows and
                 panels, view models, converters, the tray, the global hotkey, alerts,
                 theme, crash log and launch-on-startup
Pendulum.Core.Tests/ the tests (xUnit, test-only), and Fixtures/: Data files as each
                 release wrote them
installer/       the Inno Setup script; build-installer.ps1 publishes and compiles it
tools/Pendulum.Screenshots/ renders the README's pictures (build-only, never shipped)
docs/screenshots/ the README's pictures, from that tool
```

- **Logic goes in Pendulum.Core**, where it can be tested without a window: anything that decides
  when a reminder fires, what a phrase means, or what is saved. `Pendulum.Core` references no WPF,
  and nothing in it shows a window. Pendulum.App wires Core to the screen and keeps the
  Windows-only glue (`Win32Interop`, `HotkeyManager`, `StartupRegistration`).
- **The data lives beside the exe** (`AppPaths`): `Data\settings.json`, `Data\triggers.json`,
  `Data\crash.log`, `Data\WhisperModels\`, `Data\PiperModels\` and `Sounds\`. A dev build writes
  its own `Data\` under `bin\`. The registry is touched only by the opt-in launch-on-startup
  (one `Run` value), and `StartupEntry` (Pendulum.Core) decides when a copy may change it: a first
  launch registers only an installed copy (its uninstaller sits beside the exe), never over
  another copy's entry, and turning the setting off removes only this copy's. So a dev build's
  first launch leaves the installed Pendulum's entry alone; still, **never toggle the setting in a
  dev build**, since turning it on points Windows at that build.
- **`excluded/`** (git-ignored, in the maintainer's main checkout only) holds the archived
  website, an old TODO and design notes, and the Whisper and Piper resources used to try speech
  locally: about 280 MB. It is **never committed**: don't add it, move files out of it into the
  repository, or unignore it. Read it only when a card points you there.

## Building and testing

You need the .NET SDK the projects target (`TargetFramework` in each `.csproj`) on Windows.

```
dotnet build Pendulum.sln -c Debug --disable-build-servers
dotnet test Pendulum.sln --disable-build-servers     # every test project in the solution
pwsh scripts/third-party-notices.ps1 -Check           # THIRD-PARTY-NOTICES.md matches what ships
dotnet format Pendulum.sln --verify-no-changes --no-restore   # the layout .editorconfig sets
pwsh scripts/screenshots.ps1                         # the README's pictures, into docs/screenshots
.\build-installer.ps1                                # the installer, with Inno Setup 6
```

- **Pass `--disable-build-servers` to every `dotnet build`, `test` and `publish`** (build-installer.ps1
  does), or run `dotnet build-server shutdown` when you finish: otherwise MSBuild nodes and the
  compiler server keep running after your shell exits. `dotnet format` takes no such flag, and its
  own restore leaves nodes running, so run it with `--no-restore` after a build.
- **CI** (`.github/workflows/ci.yml`, on Windows) runs on every push to master, every pull request
  and by hand: the Release build with warnings as errors, the tests, the notices check and the
  format check. Run the same four before review. A change to the docs and pictures alone doesn't
  run it (`paths-ignore` in ci.yml): never add to that list a file a build step, test or check reads.

- **Tests use temp folders only**, never the app's real `Data\`, the registry or the user's
  sounds, and clean up even when they fail. They **wait for conditions, never for time**, and pass
  under load beside each other. A flaky test's proof is its file 50 times under load plus one full
  run, never 50 full runs.
- **Test the logic in Pendulum.Core.** `RecurrenceCalculator`, `QuickAddParser` and
  `TriggerReconciler` take the current time as an argument, so tests pass a fixed date rather than
  reading the clock. `TimerEngine`, `UpdateCheckService` and `SettingsRepository` have internal
  constructors or overloads for tests (a clock, an `HttpClient` with a fake handler, a file path):
  the tests see them through `InternalsVisibleTo`. Snooze, auto-resolve and the catch-up of
  reminders missed while closed are in `TriggerReconciler`, not the App, so they are tested too.
- **Upgrades:** `Pendulum.Core.Tests/Fixtures/<version>/` holds a Data folder as that release wrote
  it, and the tests load each with today's code and check that every reminder and setting is kept.
  When a release changes what's saved, add its own folder (the folder's README says how).
- **UI tests (`Pendulum.App.Tests`) show no window.** They build the real view on a UI thread of
  their own, lay it out in memory (Measure and Arrange) and read it through WPF's UI Automation
  providers, the AutomationPeers a screen reader talks to. They never use Pendulum's `App` class
  (its startup takes the single-instance mutex and would wake a running copy), and use `AppPaths`'
  and `AppServices`' internal test seams for a temp folder and loaded settings.
- **The README's pictures are rendered, never captured.** `pwsh scripts/screenshots.ps1` builds
  `tools/Pendulum.Screenshots`, which lays the app's own views out in memory the same way, with
  invented reminders dated around today (dark theme, en-GB dates, 24-hour times), and draws them
  with `RenderTargetBitmap` into `docs/screenshots`. It shows no window, uses a temp folder through
  the same test seams, and never uses the `App` class. Run it again when a change alters what the
  pictures show. Never take them from a screen, where private reminders could show.
- **Nothing a test, check or screenshot script does may take the foreground or focus.** The user is
  working in other apps while it runs. A shown window can take the foreground even with
  `ShowActivated = false` and `WS_EX_NOACTIVATE`, so don't show one: lay the view out in memory and
  render it with `RenderTargetBitmap`. Never call `Show()`, `Activate()`, `Focus()` or
  `Keyboard.Focus`, `SetForegroundWindow` or set `Topmost`. Check keyboard behaviour from properties
  (focusable, tab stops, names, selection providers), not by focusing and pressing keys. No global
  keyboard or mouse input, ever.
- **Never run the installer on this PC**, and don't publish into the installed copy's folder. Build
  installers only when a card asks, and leave installing to the maintainer.
- **The maintainer runs Pendulum all day.** A dev build launched beside it doesn't start: the
  single-instance mutex hands over to the running copy and shows *its* window. So never launch the
  app while it's running, and never close, kill or touch the running copy. A check that launches
  the app refuses to run while another Pendulum is running, and closes its own process when it
  fails.

## Rules every change keeps

- **The user's data survives every upgrade.** `Data\` beside the exe (`triggers.json`,
  `settings.json`, the downloaded Whisper and Piper models) belongs to the user, and installing a
  new version over the top keeps all of it. So:
  - **its file formats stay readable by new versions:** add properties, never rename or drop one,
    unless a migration that reads the old form comes with the change. Saved settings and
    reminders from every released version load unchanged.
  - **the installer never deletes or moves `Data\`:** `installer/Pendulum.iss` excludes it from
    what it installs, and its `[InstallDelete]` removes only the `.postinstall` marker. Keep it
    that way. User-added files in `Sounds\` survive as well.
- **The licence rule:** everything shipped (NuGet packages, native libraries, sounds, icons) is
  **MIT or equally permissive** (Apache-2.0, BSD, ISC, Zlib, ...). No copyleft, and system fonts
  only. Build-only tools may use any open-source licence. A dependency outside the rule stops the
  work for the maintainer's decision, recorded in `scripts/third-party.json`. THIRD-PARTY-NOTICES.md is
  generated: a new or changed package, sound or download updates `scripts/third-party.json`, then
  `pwsh scripts/third-party-notices.ps1` rebuilds it, and `-Check` fails when it's stale. Piper voices
  are offered only when their terms let anyone use them (`SettingsViewModel.OfferedVoices`, with
  each licence and credit).
- **No network beyond two things:** the update check (`UpdateCheckService`) and the links in
  Settings that open the Whisper and Piper download pages in the user's browser. The update check
  is off unless the user turns on Settings > General, "Check for updates on startup" (settings
  files from before that default keep it on). It sends one GitHub request at startup; a
  successful answer is cached a day, and a failure is retried at the next start. Help > Check
  for updates… sends the same request when the user chooses it (`CheckNowAsync`). It notifies and
  links to the release: it never downloads or installs. No telemetry, no other requests, and
  tests make none.
- **No global keyboard or mouse input, ever**, in tests, probes or scripts: no SendKeys,
  SendInput, keybd_event or mouse_event, wscript or AutoHotkey, and no clicking at screen
  coordinates. Synthetic input goes to whatever is in the foreground, which is the user's work (on
  another project, a probe's typed path once arrived as a message the user sent).
  - **Drive only your own windows:** UI Automation patterns (ValuePattern, InvokePattern and the
    like) on elements found by your own process ID or window handle, or messages posted to your
    own window's handle.
  - **If you can't do it that way,** don't drive the real UI: prove it with tests, and say so in
    your summary of the change.
  - The app itself registers its hotkey with `RegisterHotKey` only: no keyboard or mouse hooks.
- **Every change a user would notice** adds a plain-words line under `## [Unreleased]` in
  CHANGELOG.md.
- **Every word the app shows comes from a resource**, never a literal in XAML or C#: the App's from
  `Pendulum.App/Resources/Strings.resx` (XAML: `{x:Static res:Strings.Key}`; C#: `Strings.Key`,
  with `string.Format` for placeholders), Core's from `Pendulum.Core/Resources/CoreStrings.resx`.
  The build generates both classes, so a missing key fails it. Name a key `<View>_<What>`, and give
  it a `<comment>` saying what each `{0}` is. Dates and times go through `DisplayFormat` (or
  `DateTimeDisplayConverter` in XAML), never a format of their own. A literal that isn't shown to
  users (a file name, a registry path, a resource key) marks its line `// not user-visible`.
  `LocalizationTests` fails on a new literal, an unused or missing key, or a broken placeholder.
  English only for now; Quick Reminder's phrases stay English.

## Commits and merging

- **Commit as the repository's identity**, its GitHub no-reply address, set in the repository's
  local git config. Never a personal address, and never `--author`.
- **Each piece of work lands on master as one squashed commit**, never a merge commit. Its message
  is for the public: a short summary line, then what changed and why. No card or issue numbers
  (GitHub links `#n` to the repository's issues), no people's names, no review rounds and no
  wording in the message's text about the tools or agents that made it. The round-by-round commits
  stay on your branch.
- **The `Co-Authored-By:` trailer stays on every commit** (for example
  `Co-Authored-By: Claude <noreply@anthropic.com>`). It's honest attribution in GitHub's standard
  form, not wording in the message, so the rule above doesn't cover it. Never strip it, and never
  rebuild or reword commits to remove it, before a push or at any other time.
- **Never push, tag, publish a release or add a remote without the maintainer's go-ahead at the
  time.** Pushed history is never rewritten.

Probes, logs and scratch files go in your scratchpad or a temp folder, never in the repository or
beside a checkout. Personal, machine-specific notes belong in `CLAUDE.local.md` (Claude Code reads
it; keep it out of git), not here.
