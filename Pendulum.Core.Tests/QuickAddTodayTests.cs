using Pendulum.Core.Parsing;

namespace Pendulum.Core.Tests;

/// Today, or today's own weekday, when the time has already gone: a weekday rolls to next week,
/// "today" with a past time is refused, and "today" alone is the next whole hour.
public class QuickAddTodayTests
{
    /// Wednesday 3 June 2026.
    private static DateTime At(int hour, int minute = 0) => new(2026, 6, 3, hour, minute, 0);

    // ---- "today" with a time ----

    [Theory]
    [InlineData("report today 9am")]
    [InlineData("report today at 10")]
    [InlineData("today 08:30 report")]
    [InlineData("report today 10:00")]
    public void Today_with_a_time_already_gone_is_refused(string input)
    {
        var result = QuickAddParser.Parse(input, At(10));

        Assert.Null(result.When);
        Assert.Equal(QuickAddRefusal.TimePassedToday, result.Refusal);
        Assert.Equal("report", result.Name);
    }

    [Fact]
    public void Today_with_a_time_still_ahead_is_today()
    {
        var result = QuickAddParser.Parse("report today 4pm", At(10));

        Assert.Equal(At(16), result.When);
        Assert.Null(result.Refusal);
    }

    // ---- "today" alone ----

    [Theory]
    [InlineData(10, 0, 11)]
    [InlineData(10, 20, 11)]
    [InlineData(10, 59, 11)]
    [InlineData(0, 0, 1)]
    public void Today_alone_is_the_next_whole_hour(int hour, int minute, int expectedHour)
    {
        var result = QuickAddParser.Parse("report today", At(hour, minute));

        Assert.Equal(At(expectedHour), result.When);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Today_alone_late_in_the_evening_is_midnight()
    {
        var result = QuickAddParser.Parse("report today", At(23, 30));

        Assert.Equal(new DateTime(2026, 6, 4, 0, 0, 0), result.When);
    }

    // ---- Today's own weekday ----

    [Fact]
    public void Todays_weekday_alone_after_its_morning_time_is_next_week()
    {
        var result = QuickAddParser.Parse("gym wed", At(10));

        Assert.Equal(new DateTime(2026, 6, 10, 9, 0, 0), result.When);
    }

    [Fact]
    public void Todays_weekday_alone_before_its_morning_time_is_today()
    {
        var result = QuickAddParser.Parse("gym wednesday", At(8));

        Assert.Equal(At(9), result.When);
    }

    [Fact]
    public void Todays_weekday_with_a_time_gone_is_next_week_as_before()
    {
        Assert.Equal(new DateTime(2026, 6, 10, 9, 0, 0), QuickAddParser.Parse("report wednesday 9am", At(10)).When);
    }

    [Fact]
    public void Another_weekday_alone_is_unchanged()
    {
        Assert.Equal(new DateTime(2026, 6, 5, 9, 0, 0), QuickAddParser.Parse("gym fri", At(10)).When);
    }

    // ---- What else doesn't change ----

    [Fact]
    public void Tomorrow_alone_is_still_nine_in_the_morning()
    {
        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), QuickAddParser.Parse("dentist tomorrow", At(10)).When);
    }

    [Fact]
    public void A_time_alone_already_gone_still_rolls_to_tomorrow()
    {
        var result = QuickAddParser.Parse("call mom 9am", At(10));

        Assert.Equal(new DateTime(2026, 6, 4, 9, 0, 0), result.When);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void A_phrase_with_no_time_is_no_time_not_a_refusal()
    {
        var result = QuickAddParser.Parse("buy milk", At(10));

        Assert.Null(result.When);
        Assert.Null(result.Refusal);
    }
}
