# Changelog

All notable changes to Pendulum are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- A portable zip with each release: unzip it anywhere and run Pendulum.exe,
  which keeps its data in a Data folder beside it. Each release also lists
  its files' SHA-256 checksums, to check a download before running it.
- Repeating reminders can fall on a day of the week in the month, as in
  Outlook: "the last Friday", "the second Tuesday", "the first weekday" or
  "the last day" of every month or every few months, or of one month each
  year ("the fourth Thursday of November"). Choose it in the repeat window,
  under Monthly or Yearly.

### Fixed
- Quick Reminder: "today" with a time that has already gone ("report today
  9am" typed at 10:00) now says the time has passed, instead of adding a
  reminder that goes off at once. "today" with no time means the next whole
  hour, and a weekday that's today ("gym wed" on a Wednesday afternoon) means
  next week's.
- Whisper speech recognition now works on a PC that doesn't have Microsoft's
  Visual C++ runtime installed: Pendulum ships the runtime files Whisper needs.
  If they're ever missing, Quick Reminder says so before listening, instead of
  asking you to check your microphone after you've spoken.
- Many alerts at once no longer pile up off the top of the screen, where they
  couldn't be reached: they fill columns from the bottom-right corner, and any
  beyond that wait on screen behind the last one until there's room. Alerts
  now appear on the monitor you're working on, not always the main one.
- Choosing a Quick Reminder shortcut that another app (or Windows) already
  uses no longer leaves you with no shortcut at all while Settings shows the
  new one: Pendulum keeps the one you had and says why. If the saved shortcut
  is taken when Pendulum starts, it says so instead of quietly having none.
- Snooze and Quick Reminder's "in 20 min" or "in 2 hours" are now real time
  when the clocks go back or forward: a 10-minute snooze just before the clocks
  go back rang after 70 minutes, and in spring one rang early. Changing the
  PC's time zone (or a laptop changing it by itself) now takes effect at once,
  not after restarting Pendulum, and reminder times are saved the same way
  whichever way they were set.
- Dismissing an alert late, after the PC slept for days, now counts the
  times that went by meanwhile, so a reminder set to "end after" a number of
  times ends when it should. And an alert's Dismiss or Snooze no longer undoes
  a change you made to its reminder while the alert was open.
- Editing a repeating reminder after snoozing it no longer moves its time to
  the snooze time for good (a 9:00 stand-up snoozed once and then renamed
  went off at 9:10 every day). The edit window shows its usual time, and
  saving without changing the time keeps the pattern, and the snooze.
- Turning a repeating reminder back on from the list no longer rings it at
  once for a time that went by while it was off: it goes off next at its next
  time. A one-off whose time has passed stays off, and Pendulum says to give
  it a new time.
- Importing reminders or restoring a backup no longer rings every reminder in
  it whose time has already gone, all at once. Those are moved on to their
  next time, or marked spent, as when Pendulum starts after being closed, and
  the summary says how many.
- Imported reminders and restored backups can no longer make Pendulum play a
  sound or load a voice or speech model from outside its own folders (a full
  path, a network share or ".."): such a name is dropped, and that reminder
  plays no sound. Sounds in `Sounds\` and models in `Data\` work as before.
- A reminder's alert could be lost without a trace when saving the reminders
  list failed at the moment it went off (for example while another program had
  the file open), and so could the alerts of other reminders due at the same
  time. Each alert now goes off whatever happens to the save, and a reminder
  due in the first second after Pendulum starts is no longer missed.
- Saves survive a power cut or crash: your reminders and settings are written
  safely, and the save before is kept beside each as a `.bak`. If a file is ever
  damaged, Pendulum keeps it aside unchanged, uses the save before, and tells you
  at startup. A file another program has open is never replaced: Pendulum says
  so instead of starting empty.
- A repeating reminder that was still ringing when Pendulum closed (Exit, a
  crash, or Windows restarting) no longer stops for good and shows as spent:
  the next start moves it on to its next time, and lists it with the
  reminders missed while Pendulum was closed.
- Uninstalling Pendulum now removes its "Launch on Windows startup" entry
  (when it starts the copy being uninstalled), instead of leaving Windows a
  startup entry for a program that's gone.
- Repeating reminders: the first one now falls on the pattern, as in Outlook.
  A weekly reminder set to Mondays but starting on a Wednesday goes off first
  on the Monday after, not on the Wednesday; the same for a monthly or yearly
  day that isn't the start date. Reminders you already have keep their next
  time.
- Quick Reminder: "sun", "sat", "wed" and the other short day names are read
  as days only where they look like one ("gym sat 9am", "9am sat", "on sat"),
  so "buy sun cream 3pm" is a reminder for 3pm named "buy sun cream", not one
  for Sunday. Full day names work as before, and an "on" before a day is no
  longer left in the reminder's name.
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
- Restore backup and Reset settings now cover every setting, including
  "Delete spent reminders after", and save once. Neither changes Start with
  Windows any more: only its own checkbox does.
- An alert whose sound or spoken phrase fails (a damaged sound file, no audio
  device, a Piper problem) no longer goes quiet for good: the rest of the alert
  carries on and repeats, Piper falls back to the Windows voice, and the reason
  is written to `Data\crash.log`. Piper can no longer hang an alert, and Test
  voice and a reminder's Test button now say what went wrong.
- Speaking a reminder with Whisper no longer freezes Quick Reminder while the
  model loads. A Whisper model that was deleted or is damaged, a missing Windows
  speech recognizer and a microphone problem now each get their own message
  (the first before you speak, not after), and unplugging the microphone while
  it listens no longer leaves Quick Reminder stuck on "Listening".

### Changed
- The calendar's reminder chips and "+N more" are now rose, to match the app's
  colour, in light and dark, instead of green and purple. The "+N more" list
  follows the theme too, rather than staying dark in the light theme.
- With several alerts open, only the newest makes a sound; the others stay on
  screen, marked as waiting. Dismiss or snooze it and the next newest rings,
  saying its phrase once. A "Dismiss all" button appears when more than one
  alert is open.
- The installer is about 1 MB smaller, and Pendulum takes 3.5 MB less once
  installed: it no longer carries copies of the Whisper speech libraries for
  32-bit and ARM PCs, which it never used.
- The error log (`Data\crash.log`) no longer grows for ever: past 1 MB it
  starts afresh, keeping the one before as `crash.log.old`. About opens its
  text files in Windows' own Notepad, never a program of that name in another
  folder.
- New alert sounds: the chime, bell and alert are now Pendulum's own, made for it. A reminder
  set to one of them keeps its sound.
- Settings offers four Piper voices (Alba, Jenny (Dioco), LibriTTS-R and Northern English Male),
  each with its licence and credit, in place of a link to every voice: some voices' terms don't
  allow general use. The Piper and Whisper downloads show their licences too.
- THIRD-PARTY-NOTICES lists everything Pendulum ships, the .NET runtime included, with every
  licence in full.
- Times now follow the 12/24-hour setting everywhere they're shown (the reminder
  list, alerts, the calendar and messages), not just when setting a reminder,
  and dates and day names follow your Windows region.
- Pendulum is ready to be translated: every word it shows now comes from one
  place. It's still in English.
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
