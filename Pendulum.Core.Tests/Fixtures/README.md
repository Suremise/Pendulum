# Fixtures

`v1.0.0/` and `v1.1.0/` hold a `Data\` folder's `settings.json` and `triggers.json`, and a
whole-app `backup.json`, **as those releases write them**: each was saved by that release's own
`Pendulum.Core` (`JsonStore`, `BackupRepository`), from made-up settings and reminders, not anyone's
real data. The tests load them with today's code to prove an upgrade keeps every reminder and
setting. Never edit them by hand: `.gitattributes` keeps them byte for byte.

A later release's files join them the same way: build that release's `Pendulum.Core`, save the
same sample data with it, and add a folder.
