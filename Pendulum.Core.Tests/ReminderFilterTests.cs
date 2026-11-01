using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

/// The Reminders list's filters: kept in the settings' ReminderFilter* values (so settings.json
/// keeps its keys), one Changed for each change that can alter the list, and the same matching as
/// before. In memory only.
public sealed class ReminderFilterTests
{
    private static readonly DateTime Day = new(2026, 3, 10, 9, 0, 0);

    private static TriggerTimer Reminder(string name = "Dentist", bool scheduled = false, bool enabled = true,
        bool fired = false, AlertMode mode = AlertMode.SoundOnly, DateTime? at = null) => new()
        {
            Name = name,
            Recurrence = scheduled ? new RecurrenceRule() : null,
            Enabled = enabled,
            HasFired = fired,
            Mode = mode,
            TriggerAt = at ?? Day,
        };

    [Fact]
    public void Each_filter_is_its_setting()
    {
        var settings = new AppSettings();
        _ = new ReminderFilter(settings)
        {
            RowVisible = true,
            Name = "dent",
            TypeFixed = true,
            TypeScheduled = true,
            StatusUpcoming = true,
            StatusSpent = true,
            StatusDisabled = true,
            ModeSoundOnly = true,
            ModeSoundAndSpeech = true,
            ModeSpeechOnly = true,
            From = Day,
            To = Day.AddDays(1),
        };

        Assert.True(settings.ReminderFilterRowVisible);
        Assert.Equal("dent", settings.ReminderFilterName);
        Assert.True(settings.ReminderFilterTypeFixed);
        Assert.True(settings.ReminderFilterTypeScheduled);
        Assert.True(settings.ReminderFilterStatusUpcoming);
        Assert.True(settings.ReminderFilterStatusSpent);
        Assert.True(settings.ReminderFilterStatusDisabled);
        Assert.True(settings.ReminderFilterModeSoundOnly);
        Assert.True(settings.ReminderFilterModeSoundAndSpeech);
        Assert.True(settings.ReminderFilterModeSpeechOnly);
        Assert.Equal(Day, settings.ReminderFilterFrom);
        Assert.Equal(Day.AddDays(1), settings.ReminderFilterTo);

        // And the other way: a filter opens as the settings were saved.
        var reopened = new ReminderFilter(settings);
        Assert.True(reopened.RowVisible);
        Assert.Equal("dent", reopened.Name);
        Assert.True(reopened.ModeSpeechOnly);
        Assert.Equal(Day.AddDays(1), reopened.To);
    }

    [Fact]
    public void Every_saved_filter_has_a_property_of_the_same_name()
    {
        // ReminderFilter names each change by the setting's name less "ReminderFilter": a setting
        // added without its property would announce a change no binding hears.
        const string prefix = "ReminderFilter";
        var settingNames = typeof(AppSettings).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .Select(n => n[prefix.Length..])
            .ToList();

        Assert.Equal(12, settingNames.Count);
        Assert.All(settingNames, n => Assert.NotNull(typeof(ReminderFilter).GetProperty(n)));
    }

    [Fact]
    public void A_change_raises_Changed_once_and_names_its_property()
    {
        var filter = new ReminderFilter(new AppSettings());
        var changed = 0;
        var names = new List<string?>();
        filter.Changed += (_, _) => changed++;
        filter.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        filter.StatusSpent = true;

        Assert.Equal(1, changed);
        Assert.Contains(nameof(ReminderFilter.StatusSpent), names);
        Assert.Contains(nameof(ReminderFilter.IsActive), names);

        filter.StatusSpent = true; // no change: nothing raised
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Showing_the_filter_row_doesnt_change_the_list()
    {
        var filter = new ReminderFilter(new AppSettings());
        var changed = 0;
        var names = new List<string?>();
        filter.Changed += (_, _) => changed++;
        filter.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        filter.RowVisible = true;

        Assert.Equal(0, changed);
        Assert.Equal([nameof(ReminderFilter.RowVisible)], names);
    }

    [Fact]
    public void A_change_made_in_the_settings_is_heard()
    {
        var settings = new AppSettings();
        var filter = new ReminderFilter(settings);
        var changed = 0;
        filter.Changed += (_, _) => changed++;

        settings.ReminderFilterName = "gym";

        Assert.Equal(1, changed);
        Assert.Equal("gym", filter.Name);
        Assert.True(filter.IsActive);
    }

    [Fact]
    public void Clearing_the_dates_clears_both()
    {
        var filter = new ReminderFilter(new AppSettings()) { From = Day, To = Day };

        filter.ClearDates();

        Assert.Null(filter.From);
        Assert.Null(filter.To);
        Assert.False(filter.IsActive);
    }

    [Fact]
    public void With_no_filter_everything_shows()
    {
        var filter = new ReminderFilter(new AppSettings());

        Assert.False(filter.IsActive);
        Assert.True(filter.Matches(Reminder()));
        Assert.True(filter.Matches(Reminder(scheduled: true, enabled: false, fired: true, mode: AlertMode.SpeechOnly)));
    }

    [Fact]
    public void Name_matches_any_part_ignoring_case()
    {
        var filter = new ReminderFilter(new AppSettings()) { Name = "DENT" };

        Assert.True(filter.Matches(Reminder("Call the dentist")));
        Assert.False(filter.Matches(Reminder("Gym")));
    }

    [Fact]
    public void A_blank_name_filters_nothing()
    {
        var filter = new ReminderFilter(new AppSettings()) { Name = "   " };

        Assert.False(filter.IsActive);
        Assert.True(filter.Matches(Reminder("Gym")));
    }

    [Fact]
    public void Type_lets_through_any_ticked_type()
    {
        var filter = new ReminderFilter(new AppSettings()) { TypeScheduled = true };
        Assert.True(filter.Matches(Reminder(scheduled: true)));
        Assert.False(filter.Matches(Reminder(scheduled: false)));

        filter.TypeFixed = true;
        Assert.True(filter.Matches(Reminder(scheduled: false)));
    }

    [Fact]
    public void Status_tells_upcoming_spent_and_off_apart()
    {
        TriggerTimer[] reminders = [Reminder(), Reminder(fired: true), Reminder(enabled: false), Reminder(enabled: false, fired: true)];

        var filter = new ReminderFilter(new AppSettings()) { StatusUpcoming = true };
        Assert.Equal([true, false, false, false], reminders.Select(filter.Matches));

        filter = new ReminderFilter(new AppSettings()) { StatusSpent = true };
        Assert.Equal([false, true, false, true], reminders.Select(filter.Matches));

        filter = new ReminderFilter(new AppSettings()) { StatusDisabled = true };
        Assert.Equal([false, false, true, false], reminders.Select(filter.Matches));
    }

    [Fact]
    public void Mode_lets_through_any_ticked_mode()
    {
        var filter = new ReminderFilter(new AppSettings()) { ModeSoundOnly = true, ModeSpeechOnly = true };

        Assert.True(filter.Matches(Reminder(mode: AlertMode.SoundOnly)));
        Assert.True(filter.Matches(Reminder(mode: AlertMode.SpeechOnly)));
        Assert.False(filter.Matches(Reminder(mode: AlertMode.SoundAndSpeech)));
    }

    [Fact]
    public void Dates_include_their_whole_first_and_last_days()
    {
        var filter = new ReminderFilter(new AppSettings()) { From = Day.Date.AddHours(23), To = Day.Date.AddDays(2) };

        Assert.True(filter.Matches(Reminder(at: Day.Date.AddMinutes(1))));
        Assert.True(filter.Matches(Reminder(at: Day.Date.AddDays(2).AddHours(23))));
        Assert.False(filter.Matches(Reminder(at: Day.Date.AddMinutes(-1))));
        Assert.False(filter.Matches(Reminder(at: Day.Date.AddDays(3))));
    }

    [Fact]
    public void Every_group_must_let_a_reminder_through()
    {
        var filter = new ReminderFilter(new AppSettings()) { Name = "dent", TypeScheduled = true, ModeSoundOnly = true };

        Assert.True(filter.Matches(Reminder("Dentist", scheduled: true)));
        Assert.False(filter.Matches(Reminder("Dentist", scheduled: false)));
        Assert.False(filter.Matches(Reminder("Dentist", scheduled: true, mode: AlertMode.SpeechOnly)));
        Assert.False(filter.Matches(Reminder("Gym", scheduled: true)));
    }
}
