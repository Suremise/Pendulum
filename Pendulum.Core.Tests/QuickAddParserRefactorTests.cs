using System.Globalization;
using Pendulum.Core.Parsing;

namespace Pendulum.Core.Tests;

/// Pin precedence and fallback behavior before consolidating the parser's matching paths.
public sealed class QuickAddParserRefactorTests
{
    private static readonly DateTime Now = new(2026, 6, 3, 10, 0, 0);

    [Theory]
    [InlineData("tea in 2 hours in 5 min", "tea in 2 hours", "2026-06-03 10:05")]
    [InlineData("tea in 99999999999 min in 2 hours", "tea in 99999999999 min", "2026-06-03 12:00")]
    [InlineData("tea in 2 hr in 99999999999 minutes", "tea in 99999999999 minutes", "2026-06-03 12:00")]
    [InlineData("meeting 3pm at 11", "meeting 3pm", "2026-06-03 11:00")]
    [InlineData("meeting 3pm 14:30", "meeting 3pm", "2026-06-03 14:30")]
    [InlineData("meeting at 42 3pm 14:30", "meeting at 42 3pm", "2026-06-03 14:30")]
    [InlineData("report today tomorrow 9am", "report today", "2026-06-04 09:00")]
    [InlineData("report fri monday 9am", "report fri", "2026-06-08 09:00")]
    [InlineData("meet fri at 14:30 notes", "meet notes", "2026-06-05 14:30")]
    [InlineData("meet at 14:30 fri notes", "meet notes", "2026-06-05 14:30")]
    [InlineData("meet at 2:30pm fri notes", "meet notes", "2026-06-05 14:30")]
    [InlineData("meet 14:30, fri", "meet", "2026-06-05 14:30")]
    [InlineData("meet fri, at 14:30", "meet", "2026-06-05 14:30")]
    [InlineData("report sun 25:10pm note 4pm", "report sun 25:10pm note", "2026-06-03 16:00")]
    [InlineData("report today 25:00", "report 25:00", "2026-06-03 11:00")]
    [InlineData("gym wednesday at 25", "gym at 25", "2026-06-10 09:00")]
    [InlineData("gym wed at 25", "gym wed at 25", null)]
    public void Existing_precedence_and_day_fallbacks_are_preserved(string input, string name, string? expected)
    {
        var result = QuickAddParser.Parse(input, Now, TimeZoneInfo.Utc);
        Assert.Equal(name, result.Name);
        Assert.Equal(expected is null ? (DateTime?)null : DateTime.ParseExact(expected, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), result.When);
        Assert.Null(result.Refusal);
    }

    [Theory]
    [InlineData("tea in 99999999999 min in 5 min")]
    [InlineData("tea in 99999999999 hr in 2 hours")]
    public void A_later_phrase_of_the_same_unit_does_not_replace_an_invalid_first_one(string input)
    {
        var result = QuickAddParser.Parse(input, Now, TimeZoneInfo.Utc);
        Assert.Equal(input, result.Name);
        Assert.Null(result.When);
        Assert.Null(result.DueUtc);
        Assert.Null(result.Refusal);
    }

    [Theory]
    [InlineData("tea in 0 mins", 0)]
    [InlineData("tea IN 2 HRS", 120)]
    [InlineData("tea in 15minutes", 15)]
    public void Both_relative_units_keep_seconds_and_the_real_UTC_instant(string input, int minutes)
    {
        var now = Now.AddSeconds(42);
        var result = QuickAddParser.Parse(input, now, TimeZoneInfo.Utc);
        Assert.Equal("tea", result.Name);
        Assert.Equal(now.AddMinutes(minutes), result.When);
        Assert.Equal(DateTimeKind.Unspecified, result.When!.Value.Kind);
        Assert.Equal(DateTime.SpecifyKind(now.AddMinutes(minutes), DateTimeKind.Utc), result.DueUtc);
    }

    [Fact]
    public void An_invalid_time_stays_in_the_name_when_today_refuses_a_later_valid_time()
    {
        var result = QuickAddParser.Parse("report today at 13:10pm 9am", Now);
        Assert.Equal("report at 13:10pm", result.Name);
        Assert.Null(result.When);
        Assert.Equal(QuickAddRefusal.TimePassedToday, result.Refusal);
    }
}
