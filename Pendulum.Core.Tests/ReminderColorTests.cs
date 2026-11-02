using System.Text.Json.Nodes;
using Pendulum.Core.Models;
using Pendulum.Core.Persistence;

namespace Pendulum.Core.Tests;

/// Each reminder's colour: a new optional property. Reminders saved by every release load
/// unchanged and get a colour that stays the same; a chosen colour, and one this version doesn't
/// know, survive a save; new reminders spread across the palette.
public sealed class ReminderColorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> Releases => ReleaseFixtures.All;

    private string Copy(string release)
    {
        var path = _temp.File($"{release}-triggers.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", release, "triggers.json"), path);
        return path;
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void Reminders_from_a_release_get_a_colour_that_stays_the_same(string release)
    {
        var path = Copy(release);
        var before = File.ReadAllBytes(path);

        var first = new TriggerRepository().LoadFrom(path);
        var second = new TriggerRepository().LoadFrom(path);

        Assert.Equal(ReleaseFixtures.Reminders(release), first.Count);
        Assert.All(first, r => Assert.True(ReminderColors.IsKnown(r.Color), $"{r.Name}: {r.Color}"));
        Assert.Equal(first.Select(r => r.Color), second.Select(r => r.Color));
        // The colour the file saved (1.2.0 saves one), or for none, the one worked out from its Id.
        var saved = JsonNode.Parse(before)!.AsArray()
            .Select(n => (string?)n!["Color"]).ToList();
        Assert.All(first.Select((r, i) => (r, saved[i])), x => Assert.Equal(x.Item2 ?? ReminderColors.Stable(x.r.Id), x.r.Color));
        // Loading writes nothing: the colour is saved with the next save, not on its own.
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_release_s_reminders_saved_again_keep_their_colours(string release)
    {
        var loaded = new TriggerRepository().LoadFrom(Copy(release));
        var resaved = _temp.File("resaved.json");
        new TriggerRepository().SaveTo(resaved, loaded);

        var saved = JsonNode.Parse(File.ReadAllText(resaved))!.AsArray();
        Assert.All(saved, r => Assert.True(ReminderColors.IsKnown((string?)r!["Color"])));
        Assert.Equal(loaded.Select(r => r.Color), new TriggerRepository().LoadFrom(resaved).Select(r => r.Color));
    }

    [Fact]
    public void A_chosen_colour_is_kept()
    {
        var reminder = new TriggerTimer { Name = "Dentist", Color = "plum" };
        var path = _temp.File("chosen.json");
        new TriggerRepository().SaveTo(path, [reminder]);

        var loaded = new TriggerRepository().LoadFrom(path).Single();

        Assert.Equal("plum", loaded.Color);
        Assert.Equal("plum", loaded.DisplayColor);
    }

    [Fact]
    public void A_colour_this_version_does_not_know_is_kept_and_shown_as_the_worked_out_one()
    {
        var reminder = new TriggerTimer { Name = "From a later version", Color = "magenta" };
        var path = _temp.File("later.json");
        new TriggerRepository().SaveTo(path, [reminder]);

        var loaded = new TriggerRepository().LoadFrom(path).Single();
        Assert.Equal("magenta", loaded.Color);
        Assert.Equal(ReminderColors.Stable(loaded.Id), loaded.DisplayColor);

        var resaved = _temp.File("later-resaved.json");
        new TriggerRepository().SaveTo(resaved, [loaded]);
        Assert.Equal("magenta", new TriggerRepository().LoadFrom(resaved).Single().Color);
    }

    [Fact]
    public void A_reminder_not_yet_given_a_colour_shows_the_one_from_its_id()
    {
        var reminder = new TriggerTimer { Name = "New" };

        Assert.Null(reminder.Color);
        Assert.Equal(ReminderColors.Stable(reminder.Id), reminder.DisplayColor);
    }

    [Fact]
    public void The_colour_from_an_id_is_always_the_same_and_uses_the_whole_palette()
    {
        var id = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        Assert.Equal(ReminderColors.Stable(id), ReminderColors.Stable(new Guid(id.ToString())));

        var seen = Enumerable.Range(0, 400).Select(i => ReminderColors.Stable(new Guid(i, 0, 0, new byte[8]))).ToHashSet();
        Assert.Equal(ReminderColors.All.ToHashSet(), seen);
    }

    [Fact]
    public void New_reminders_take_the_colour_fewest_have()
    {
        var reminders = new List<TriggerTimer>();
        var given = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var colour = ReminderColors.ForNew(reminders);
            given.Add(colour);
            reminders.Add(new TriggerTimer { Color = colour });
        }

        // All eight before any repeats, in the order that keeps neighbours apart, then round again.
        Assert.Equal(ReminderColors.AssignOrder.Concat(ReminderColors.AssignOrder.Take(2)), given);
    }

    [Fact]
    public void A_new_reminder_counts_reminders_without_a_saved_colour_by_the_one_they_show()
    {
        var shown = new TriggerTimer();
        var existing = new List<TriggerTimer> { shown };

        Assert.NotEqual(shown.DisplayColor, ReminderColors.ForNew(existing));
    }

    [Fact]
    public void The_palette_names_are_eight_and_distinct()
    {
        Assert.Equal(8, ReminderColors.All.Distinct().Count());
        Assert.Equal(ReminderColors.All.OrderBy(n => n), ReminderColors.AssignOrder.OrderBy(n => n));
    }
}
