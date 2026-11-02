# Fixtures

`v1.0.0/`, `v1.1.0/` and `v1.2.0/` hold a `Data\` folder's `settings.json` and `triggers.json`, and a
whole-app `backup.json`, **as those releases write them**: each was saved by that release's own
`Pendulum.Core` (`JsonStore`, `BackupRepository`), from made-up settings and reminders, not anyone's
real data. The tests load them with today's code to prove an upgrade keeps every reminder and
setting. Never edit them by hand: `.gitattributes` keeps them byte for byte.

A later release's files join them the same way: build that release's `Pendulum.Core`, save the
same sample data with it, and add a folder (and its name to `ReleaseFixtures`). `v1.2.0/` is
v1.1.0's sample read and saved by 1.2.0, plus what 1.2.0 added to the format: a reminder set as a
span ("Call the dentist", its `DueUtc`), a snoozed repeating one ("Stand-up", its
`SnoozedOccurrence`) and a seventh, "Book club", monthly on the last Friday, with a colour chosen
for it ("plum"). The others have the colour 1.2.0 gives a reminder saved without one, worked out
from its Id as it loads and written with the next save (these made-up Ids all give "teal").
