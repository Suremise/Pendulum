# Security

Pendulum runs all day in the tray, reads files people choose to import, and starts speech engines
they download themselves, so security reports matter. Thank you for taking the time.

## Reporting a vulnerability

**Please report it privately, not in a public issue.**

Use GitHub's private vulnerability reporting: on
[github.com/Suremise/Pendulum](https://github.com/Suremise/Pendulum), open the **Security** tab and
choose **Report a vulnerability**, or go straight to
[the report form](https://github.com/Suremise/Pendulum/security/advisories/new). Only the
maintainers can see it.

Please include:

- what the problem is, and what someone could do with it;
- a file (or the steps) that shows it, and what About's Copy gives (the Pendulum, .NET and
  Windows versions; on 1.1.0 and earlier, the version About shows and the Windows version);
- anything you've found about a fix.

We aim to answer within a week, and to keep you told as we work on it. We'll credit you in the
advisory and the changelog unless you'd rather we didn't. Please give us a reasonable time to fix
it before you make it public.

## Supported versions

Fixes go into the latest release only.

## What's most sensitive

Reports in these areas matter most:

- **Imports and restores** (`Pendulum.Core/Persistence`): a reminders file or a whole-app backup
  can come from anywhere. One that makes Pendulum write outside its own `Data\` folder, run
  anything, or point a reminder's sound or a speech engine at a program it then starts, is a
  vulnerability.
- **The update check** (`Pendulum.Core/Services/UpdateCheckService.cs`): one request to GitHub's
  releases API when Pendulum starts (a successful answer is cached for a day; a failed one is
  retried at the next start), and only if the user has turned on "Check for updates on
  startup" (off for new installs). It never
  downloads or installs anything, and the link it shows always goes to the release's page on
  GitHub. A reply that gets Pendulum to open anything else, or to fetch or run a file, is a
  vulnerability.
- **The speech engines** (`Pendulum.Core/Speech`): Whisper models and the Piper engine and voices
  are downloaded by the user and loaded from Pendulum's own `Data\` folder. Pendulum loading or
  starting them from anywhere else is a vulnerability.
- **Pendulum's data and the registry:** reminders and settings stay in the `Data\` folder beside
  the exe, and the registry is written only for "Launch on Windows startup" (one value under
  `Run`). A file or another process that gets Pendulum to write elsewhere counts.
- **The hotkey:** Pendulum registers its Quick Reminder hotkey with Windows and never watches the
  keyboard or mouse otherwise. Anything that reads keystrokes beyond that hotkey is a
  vulnerability.

Pendulum makes no network connections beyond the update check, which is off unless you turn it
on. The links in Settings open the
Whisper and Piper download pages in your browser; Pendulum itself downloads nothing.
