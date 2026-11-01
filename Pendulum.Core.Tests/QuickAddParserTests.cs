using System.Globalization;
using Pendulum.Core.Parsing;

namespace Pendulum.Core.Tests;

public class QuickAddParserTests
{
    /// Wednesday 3 June 2026, 10:00: every phrase is read as if typed then.
    private static readonly DateTime Now = new(2026, 6, 3, 10, 0, 0);

    private static DateTime At(string text) => DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Theory]
    // A time of day: later today, or tomorrow once it has gone.
    [InlineData("call mom 3pm", "call mom", "2026-06-03 15:00")]
    [InlineData("Call Mom 3PM", "Call Mom", "2026-06-03 15:00")]
    [InlineData("call mom 9am", "call mom", "2026-06-04 09:00")]
    [InlineData("call mom 10am", "call mom", "2026-06-04 10:00")]
    [InlineData("lunch 12pm", "lunch", "2026-06-03 12:00")]
    [InlineData("midnight snack 12am", "midnight snack", "2026-06-04 00:00")]
    [InlineData("call mom 3 pm", "call mom", "2026-06-03 15:00")]
    [InlineData("call mom at 3:45pm", "call mom", "2026-06-03 15:45")]
    [InlineData("call mom at 9:15 am", "call mom", "2026-06-04 09:15")]
    // 24-hour times, and "at" with a bare hour, are read as 24-hour.
    [InlineData("meeting 14:30", "meeting", "2026-06-03 14:30")]
    [InlineData("meeting at 14:30", "meeting", "2026-06-03 14:30")]
    [InlineData("meeting at 14", "meeting", "2026-06-03 14:00")]
    [InlineData("meeting at 9", "meeting", "2026-06-04 09:00")]
    [InlineData("pay rent 5:30", "pay rent", "2026-06-04 05:30")]
    [InlineData("night shift 23:59", "night shift", "2026-06-03 23:59")]
    [InlineData("early 00:15", "early", "2026-06-04 00:15")]
    [InlineData("exactly now 10:00", "exactly now", "2026-06-04 10:00")]
    // From now.
    [InlineData("stretch in 20 min", "stretch", "2026-06-03 10:20")]
    [InlineData("in 20 min stretch", "stretch", "2026-06-03 10:20")]
    [InlineData("tea in 5 minutes", "tea", "2026-06-03 10:05")]
    [InlineData("tea in 1 minute", "tea", "2026-06-03 10:01")]
    [InlineData("tea in 45 mins", "tea", "2026-06-03 10:45")]
    [InlineData("check oven in 2 hours", "check oven", "2026-06-03 12:00")]
    [InlineData("check oven in 1 hr", "check oven", "2026-06-03 11:00")]
    [InlineData("check oven in 3hrs", "check oven", "2026-06-03 13:00")]
    [InlineData("check oven in 14 hours", "check oven", "2026-06-04 00:00")]
    [InlineData("in 90 minutes call back", "call back", "2026-06-03 11:30")]
    // Tomorrow and today.
    [InlineData("standup tomorrow 9am", "standup", "2026-06-04 09:00")]
    [InlineData("tomorrow at 14:00 dentist", "dentist", "2026-06-04 14:00")]
    [InlineData("dentist tomorrow", "dentist", "2026-06-04 09:00")]
    [InlineData("report today 4pm", "report", "2026-06-03 16:00")]
    [InlineData("report today", "report", "2026-06-03 09:00")]
    // Weekdays: the next one, today's own only while its time is still ahead.
    [InlineData("call bank friday 2pm", "call bank", "2026-06-05 14:00")]
    [InlineData("call bank Fri 2pm", "call bank", "2026-06-05 14:00")]
    [InlineData("gym fri", "gym", "2026-06-05 09:00")]
    [InlineData("review monday 10am", "review", "2026-06-08 10:00")]
    [InlineData("review mon at 10:30", "review", "2026-06-08 10:30")]
    [InlineData("bins tuesday 7pm", "bins", "2026-06-09 19:00")]
    [InlineData("sunday 11am brunch", "brunch", "2026-06-07 11:00")]
    [InlineData("saturday 8:00 run", "run", "2026-06-06 08:00")]
    [InlineData("thursday 9am dentist", "dentist", "2026-06-04 09:00")]
    [InlineData("report wednesday 3pm", "report", "2026-06-03 15:00")]
    [InlineData("report wednesday 9am", "report", "2026-06-10 09:00")]
    [InlineData("report wed 10am", "report", "2026-06-10 10:00")]
    // A number that isn't a time stays in the name, and a real time later on is still found.
    [InlineData("meet at 42 Main St 3pm", "meet at 42 Main St", "2026-06-03 15:00")]
    [InlineData("call 0800 555 1234 at 4pm", "call 0800 555 1234", "2026-06-03 16:00")]
    [InlineData("meeting 25:10pm 4pm", "meeting 25:10pm", "2026-06-03 16:00")]
    [InlineData("meeting at 13:10pm 4pm", "meeting at 13:10pm", "2026-06-03 16:00")]
    [InlineData("meeting 10:75 at 4pm", "meeting 10:75", "2026-06-03 16:00")]
    [InlineData("  trim me   3pm  ", "trim me", "2026-06-03 15:00")]
    [InlineData("call mom, 3pm.", "call mom", "2026-06-03 15:00")]
    public void Reads_the_time_and_keeps_the_rest_as_the_name(string input, string name, string when)
    {
        var result = QuickAddParser.Parse(input, Now);

        Assert.Equal(name, result.Name);
        Assert.Equal(At(when), result.When);
    }

    [Theory]
    [InlineData("buy milk")]
    [InlineData("call 555 1234")]
    [InlineData("in a few minutes")]
    [InlineData("at noon")]
    [InlineData("meeting 13pm")]
    [InlineData("meeting 0am")]
    [InlineData("meeting 25:00")]
    [InlineData("meeting 24:00")]
    [InlineData("meeting 10:75")]
    [InlineData("meeting 25:10pm")]
    [InlineData("meeting at 13:10pm")]
    [InlineData("meeting 10:75pm")]
    [InlineData("meeting at 31")]
    [InlineData("wait in 99999999999 minutes")]
    [InlineData("wait in 2147483647 hours")]
    [InlineData("pin 1234pm")]
    public void Finds_no_time_where_there_is_none(string input)
    {
        var result = QuickAddParser.Parse(input, Now);

        Assert.Null(result.When);
        Assert.Equal(input.Trim(), result.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_gives_nothing(string input)
    {
        var result = QuickAddParser.Parse(input, Now);

        Assert.Equal(string.Empty, result.Name);
        Assert.Null(result.When);
    }

    [Theory]
    [InlineData("3pm")]
    [InlineData("in 20 min")]
    [InlineData("tomorrow 9am")]
    public void A_time_alone_has_an_empty_name(string input)
    {
        var result = QuickAddParser.Parse(input, Now);

        Assert.Equal(string.Empty, result.Name);
        Assert.NotNull(result.When);
    }

    [Fact]
    public void A_time_already_gone_rolls_over_midnight_into_the_next_month_and_year()
    {
        var newYearsEve = new DateTime(2026, 12, 31, 22, 0, 0);

        var result = QuickAddParser.Parse("fireworks 9pm", newYearsEve);

        Assert.Equal(new DateTime(2027, 1, 1, 21, 0, 0), result.When);
    }

    [Fact]
    public void Tomorrow_on_the_last_day_of_february_in_a_leap_year_is_the_29th()
    {
        var result = QuickAddParser.Parse("dentist tomorrow 9am", new DateTime(2028, 2, 28, 12, 0, 0));

        Assert.Equal(new DateTime(2028, 2, 29, 9, 0, 0), result.When);
    }

    [Fact]
    public void Minutes_from_now_keep_the_seconds_of_now()
    {
        var now = new DateTime(2026, 6, 3, 10, 0, 42);

        var result = QuickAddParser.Parse("tea in 5 min", now);

        Assert.Equal(now.AddMinutes(5), result.When);
    }
}
