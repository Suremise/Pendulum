# Changelog

All notable changes to Pendulum are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- Restore: choosing a file that isn't a backup no longer renames the file you
  chose, and choosing a settings file by mistake no longer replaces all your
  reminders with none. Pendulum says it isn't a backup and leaves it where it
  was.
- Import: choosing a file that doesn't hold reminders now says so, instead of
  reporting "Imported 0 reminder(s)", and leaves the file where it was.
- Launch on Windows startup: the first run of a second copy of Pendulum (a
  portable copy, or one unzipped to try out) no longer takes over the startup
  entry from your installed Pendulum, and turning the setting off in that copy
  no longer removes the installed one's. A copy that doesn't start with
  Windows now shows the setting off.
- Quick Reminder: a weekday that's today with a time already gone ("report
  wednesday 9am", typed on a Wednesday afternoon) now means next week's, not
  a reminder that goes off straight away.
- Quick Reminder: a number that isn't a time of day ("13pm", "25:00",
  "10:75") is no longer turned into the nearest time, and a very large one
  ("in 99999999999 min") no longer brings up an "unexpected error" message.
- Starting Pendulum after reminders were missed while it was closed no longer
  saves your reminders list part-way through loading it, which could have lost
  reminders if Pendulum was stopped at that moment.

### Changed
- Settings are easier to find: they're grouped into five sections (General,
  Alerts, Spoken alerts, Quick Reminder, Backup and data) with plainer labels and
  short explanations, and settings that only apply to another one sit under it.
  All your settings carry over unchanged.
- The app's accent colour is now rose, to match the new icon: buttons, toggles,
  the selected tab, links and the alert popup, in shades that stay easy to read
  on the light and dark themes. Switching the theme while Pendulum runs no
  longer turns buttons blue.
- Links (in About and the "View release" link in the status bar) are now easy
  to read on the dark theme, and match the app's accent on the light one.
- Runs on .NET 10, Microsoft's current long-term support release, in place of
  .NET 8, whose support ends in November 2026. The installer still includes
  everything it needs: there's nothing extra to install, and your reminders and
  settings carry over.
- The update check is now off for new installs: Pendulum goes online only if you
  turn on Settings > General > "Check for updates on startup". If you already
  had it on, it stays on. The README's new Privacy section says exactly what
  the check sends.
- New icon, in the style of the other Tools apps: the same clock face and
  swinging pendulum, now on a rose circle, with a bolder version for the tray
  and small sizes so it reads on light and dark taskbars.
- About window: now shows the version, build date, .NET and Windows versions in
  one table, with a Copy button to paste them into a bug report, plus links to
  the release notes and the source code on GitHub. The installer now names
  Darren Marshall as publisher and links to the GitHub repository.

## [1.1.0] - 2026-08-25

### Added
- Quick Reminder: optional "start listening automatically" setting, so speech
  input begins the moment the window opens instead of requiring a click on
  the mic button first.
- Settings > General: "Check for updates on startup" (on by default) — a
  single cached check against GitHub once a day; if a newer release exists,
  a small link appears in the status bar next to About. No auto-download or
  auto-install.

### Changed
- About window: License and Third-Party Notices are now inline links instead
  of separate buttons; removed the redundant Panels summary card.

## [1.0.0] - 2026-08-25

Initial public release.

### Added
- **Reminders** — alarms for an exact date and time, with sound, text-to-speech,
  or both, and an adjustable per-timer volume. Outlook-style recurrence
  (daily/weekly/monthly/yearly), bulk select/delete/export, sortable and
  filterable columns, and reminders-only or whole-app backup & restore.
- **Calendar** — a month view of every reminder, with navigable months/years
  and click-to-edit on any reminder shown on it.
- **Settings** — sound library management; speech-to-text (Windows Speech
  Recognition or a locally-run Whisper model) for the Quick Reminder mic
  button; text-to-speech (Windows or a locally-run Piper voice); alert style,
  snooze duration, and auto-resolve timeout for unanswered alerts; 12/24-hour
  time format; and launch-on-startup.
- **Tray integration** — minimizes to the system tray instead of closing; the
  tray icon's tooltip shows the next upcoming reminder.
- Resilient scheduling — reminders that come due while the app is closed, or
  that go unanswered, resolve themselves sensibly (reschedule if recurring,
  mark spent otherwise) instead of getting stuck.
- Self-contained, no-admin Windows installer.

[Unreleased]: https://github.com/Suremise/Pendulum/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/Suremise/Pendulum/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Suremise/Pendulum/releases/tag/v1.0.0
