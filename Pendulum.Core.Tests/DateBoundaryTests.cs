using Pendulum.Core.Engine;

namespace Pendulum.Core.Tests;

/// Explicit custom zones and UTC instants: never change the machine's clock or timezone.
public sealed class DateBoundaryTests
{
    private static DateTimeOffset Utc(int month, int day, int hour = 0, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TimeZoneInfo Zone(int startHour, int startMinute, int endHour, int endMinute = 0,
        int milliseconds = 0, int deltaMinutes = 60)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, startHour, startMinute, 0, milliseconds), 3, 29);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, endHour, endMinute, 0), 10, 25);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31), TimeSpan.FromMinutes(deltaMinutes), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Calendar test zone", TimeSpan.Zero, "Calendar test zone", "Standard", "Daylight", [rule]);
    }

    private static DateTimeOffset AssertBoundary(DateTimeOffset now, TimeZoneInfo zone, DateTimeOffset expected)
    {
        var result = WallClock.NextDateBoundary(now, zone);
        Assert.Equal(expected, result);
        Assert.True(result > now);
        Assert.Equal(TimeSpan.Zero, result.Offset);
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        Assert.Equal(date, TimeZoneInfo.ConvertTime(result.AddTicks(-1), zone).Date);
        Assert.True(TimeZoneInfo.ConvertTime(result, zone).Date > date);
        return result;
    }

    [Fact]
    public void Utc_rolls_over_at_midnight_and_exact_midnight_schedules_the_following_day()
    {
        var boundary = AssertBoundary(Utc(11, 14, 23, 59), TimeZoneInfo.Utc, Utc(11, 15));
        AssertBoundary(boundary, TimeZoneInfo.Utc, Utc(11, 16));
    }

    [Theory]
    [InlineData(330)]
    [InlineData(-210)]
    public void Fixed_offsets_use_the_zones_date_not_the_input_offset(int minutes)
    {
        var offset = TimeSpan.FromMinutes(minutes);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Fixed calendar zone", offset, "Fixed", "Fixed");
        var now = Utc(11, 14, 12).ToOffset(TimeSpan.FromHours(-7));
        var expected = new DateTimeOffset(2026, 11, 15, 0, 0, 0, offset).ToUniversalTime();
        AssertBoundary(now, zone, expected);
    }

    [Fact]
    public void Ordinary_spring_and_autumn_changes_make_23_and_25_hour_days()
    {
        var zone = Zone(2, 0, 2);
        var spring = Utc(3, 29);
        Assert.Equal(TimeSpan.FromHours(23), AssertBoundary(spring, zone, Utc(3, 29, 23)) - spring);
        var autumn = Utc(10, 24, 23);
        Assert.Equal(TimeSpan.FromHours(25), AssertBoundary(autumn, zone, Utc(10, 26)) - autumn);
    }

    [Fact]
    public void A_skipped_midnight_uses_the_first_valid_time_of_the_new_date()
    {
        var zone = Zone(0, 0, 2);
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 29)));
        var boundary = AssertBoundary(Utc(3, 28, 20), zone, Utc(3, 29));
        Assert.Equal(1, TimeZoneInfo.ConvertTime(boundary, zone).Hour);
        AssertBoundary(boundary, zone, Utc(3, 29, 23));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void A_gap_spanning_midnight_uses_its_exact_end_instead_of_rounding_to_an_hour_or_minute(int milliseconds)
    {
        var zone = Zone(23, 30, 2, milliseconds: milliseconds);
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 30)));
        var expected = Utc(3, 29, 23, 30).AddMilliseconds(milliseconds);
        AssertBoundary(Utc(3, 29, 20), zone, expected);
    }

    [Fact]
    public void An_ambiguous_midnight_uses_its_first_real_occurrence()
    {
        var zone = Zone(2, 0, 1);
        Assert.True(zone.IsAmbiguousTime(new DateTime(2026, 10, 25)));
        var first = AssertBoundary(Utc(10, 24, 22), zone, Utc(10, 24, 23));
        AssertBoundary(first, zone, Utc(10, 26));
    }

    [Fact]
    public void A_rollback_across_the_date_uses_the_next_future_midnight_not_one_that_already_passed()
    {
        var zone = Zone(2, 0, 0, 30);
        Assert.True(zone.IsAmbiguousTime(new DateTime(2026, 10, 25)));
        AssertBoundary(Utc(10, 24, 22), zone, Utc(10, 24, 23));
        // At 23:30 UTC the clock returns from 00:30 to 23:30 of the previous date.
        // Its first midnight is now past, so the second midnight is the next date boundary.
        AssertBoundary(Utc(10, 24, 23, 45), zone, Utc(10, 25));
    }

    [Fact]
    public void A_half_hour_midnight_gap_is_not_assumed_to_be_one_hour()
    {
        var zone = Zone(23, 45, 2, deltaMinutes: 30);
        AssertBoundary(Utc(3, 29, 20), zone, Utc(3, 29, 23, 45));
    }

    [Fact]
    public void Negative_daylight_adjustments_handle_both_repeated_and_skipped_midnights()
    {
        var zone = Zone(0, 30, 23, 45, deltaMinutes: -30);
        Assert.True(zone.IsAmbiguousTime(new DateTime(2026, 3, 29)));
        AssertBoundary(Utc(3, 28, 23), zone, Utc(3, 29));
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 10, 26)));
        AssertBoundary(Utc(10, 25, 20), zone, Utc(10, 26, 0, 15));
    }
}
