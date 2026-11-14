using Pendulum.Core.Engine;
using Pendulum.Core.Models;

namespace Pendulum.Core.Tests;

public sealed class WeekInMonthTests
{
    [Theory]
    [InlineData(2026, 6, 1, WeekOfMonth.First)]
    [InlineData(2026, 6, 8, WeekOfMonth.Second)]
    [InlineData(2026, 6, 17, WeekOfMonth.Third)]
    [InlineData(2026, 6, 23, WeekOfMonth.Fourth)]
    [InlineData(2026, 6, 24, WeekOfMonth.Last)]
    [InlineData(2028, 2, 22, WeekOfMonth.Fourth)]
    [InlineData(2028, 2, 23, WeekOfMonth.Last)]
    public void The_last_seven_days_take_precedence_over_the_ordinal_week(int year, int month, int day, WeekOfMonth expected) =>
        Assert.Equal(expected, RecurrenceCalculator.WeekInMonth(new DateTime(year, month, day)));
}
