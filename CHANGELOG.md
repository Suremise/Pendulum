# Changelog

All notable changes to Pendulum are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
