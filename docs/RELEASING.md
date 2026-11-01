# Releasing Pendulum

A release starts from a version tag. The tag runs `.github/workflows/release.yml`, which checks,
tests and builds everything with the same `build-installer.ps1` a local build uses, then **drafts**
a GitHub release. Nothing is published until the maintainer has run the checklist below and
publishes the draft.

## What ships

| File | What it is |
|---|---|
| `PendulumSetup-<version>.exe` | The installer (Inno Setup 6): per-user, under `%LocalAppData%\Programs\Pendulum`, no admin prompt. Installing over an earlier version keeps `Data\` (settings, reminders, downloaded models). |
| `Pendulum-<version>-portable.zip` | The same build, to unzip anywhere and run. It keeps its data in a `Data\` folder beside `Pendulum.exe`, and never takes over the installed copy's "Launch on Windows startup" entry. |
| `SHA256SUMS.txt` | Both files' SHA-256 checksums, as `sha256sum -c` reads them. |

## The tools

- **The .NET SDK** that `global.json` pins (10.0.403, rolling forward to its latest patch).
- **Inno Setup 6** (build-only: it builds the installer, and ships nothing of its own beyond
  the setup program's code, under its own licence, recorded in THIRD-PARTY-NOTICES.md). The
  GitHub runners have it; `build-installer.ps1` finds it in its usual folders.
- **PowerShell 7** for the scripts, and the **GitHub CLI** (`gh`) in the workflow to draft the release.

## Steps

1. **Move `## [Unreleased]` in CHANGELOG.md** to `## [<version>] - <date>`, add an empty
   `## [Unreleased]` above it, and update the links at the bottom. The release notes come from
   this section: the workflow stops if it's missing.
2. **Set the version** in `Pendulum.App/Pendulum.App.csproj` (`<Version>`): it's the one version
   source. The installer, About and the update check all read it.
3. **Build locally** and run the checks CI runs (AGENTS.md, Building and testing), then:
   ```
   .\build-installer.ps1
   ```
   It writes `dist\PendulumSetup-<version>.exe`, `dist\Pendulum-<version>-portable.zip` and
   `dist\SHA256SUMS.txt`, and fails if the zip is missing files or holds a `Data\` folder or
   debug symbols.
4. **Run the manual checklist** below on that build, on a PC where installing is fine.
5. **Merge to master, then tag**: `git tag v<version>` on the release commit and push the tag.
   This needs the maintainer's go-ahead at the time, like every push.
6. **The workflow** checks that the tag matches the csproj's version, builds with warnings as
   errors, runs the tests and the notices check, builds the three files and drafts the release
   with the CHANGELOG's section, how to check the download, and the SmartScreen note.
7. **Check the draft** (its notes, its three files, and the checksums against your local
   `SHA256SUMS.txt`: CI's build has different hashes from a local one, so users check against the
   draft's file) and **publish it**. Once it's published, the update check in earlier versions
   finds it, and its link opens this release's page with the downloads.

**Signing:** Pendulum isn't code-signed yet. `build-installer.ps1` and the workflow mark where
signing goes, so a certificate can be added later without changing the steps.

## The manual checklist

Copy this into the release's tracking issue or notes and tick each box. Use the **installer from
step 3** (or the draft's), on a PC where it's fine to install. Before you start, **back up
`%LocalAppData%\Programs\Pendulum\Data\`** if this PC already has Pendulum.

### Upgrade over v1.1.0, keeping everything

- [ ] With v1.1.0 installed and in use (reminders: one-off, weekly on chosen days, monthly, yearly,
      one that ends after a count; settings changed from their defaults), note the reminder count
      and a few settings.
- [ ] Leave Pendulum running and start the new Setup: it asks to close Pendulum first. Close it
      from the tray and carry on.
- [ ] After the upgrade, Pendulum starts and shows its window once.
- [ ] **Every reminder is there**, with the same names, times, repeats and sounds.
- [ ] **Every setting is as it was**: theme, sounds, snooze, alerts, speech engines and voices,
      Quick Reminder hotkey, update check, launch on startup.
- [ ] `Data\` still holds `settings.json`, `triggers.json` and the downloaded Whisper and Piper models.
- [ ] About shows the new version and its Copy gives the version, build, .NET and Windows.

### A fresh install

- [ ] On a PC (or Windows user) without Pendulum: SmartScreen shows "Windows protected your PC"
      for the unsigned Setup. **Check the file against SHA256SUMS.txt first**
      (`Get-FileHash .\PendulumSetup-<version>.exe`), then More info, Run anyway.
- [ ] Setup installs with no admin prompt, into `%LocalAppData%\Programs\Pendulum`.
- [ ] Pendulum starts and shows its window; Settings shows "Launch on Windows startup" on.
- [ ] The Start menu has Pendulum and Uninstall Pendulum.

### Using it

- [ ] **The tray:** the icon shows (in the tray or its overflow), its tooltip names the next
      reminder, and its menu opens Pendulum and exits it.
- [ ] Closing the window leaves Pendulum in the tray.
- [ ] **Alerts:** a reminder in a minute, with sound: the alert pops up on top of other windows
      and plays the sound. Snooze brings it back after the snooze time; Dismiss ends it.
- [ ] An alert with **speech**, with the Windows voice and with Piper: the phrase is spoken.
- [ ] A repeating reminder moves on to its next time after Dismiss, on its pattern (try "the last
      Friday" monthly).
- [ ] **Quick Reminder by typing:** the hotkey opens it; "tea in 2 min" and "call mom 3pm" add
      reminders at the right times.
- [ ] **Quick Reminder by voice**, with Windows speech recognition and with Whisper: the mic
      button hears a phrase and fills it in.
- [ ] Backup, then Restore, gives back the same reminders and settings; restoring a file that
      isn't a backup says so and leaves the file where it was.
- [ ] The update check (if on) finds nothing newer than this version.

### Startup and uninstall

- [ ] **Startup launch:** sign out and in (or restart): Pendulum starts in the tray.
- [ ] Turning "Launch on Windows startup" off, then signing in again: it doesn't start.
- [ ] **The portable zip:** unzipped to a folder and run (with the installed Pendulum closed),
      it starts with its own `Data\` beside it, and the installed copy's startup entry is
      unchanged.
- [ ] **Uninstall** from Settings > Apps or the Start menu: the app's files and Start menu
      entries go, and the startup entry goes too (`HKCU\...\Run\Pendulum`, when it starts this
      copy), so Pendulum no longer starts with Windows. `Data\` is left in place: it's the user's.
- [ ] With the portable copy's startup turned on before uninstalling, the uninstall leaves its
      entry alone.

When every box is ticked, publish the draft.
