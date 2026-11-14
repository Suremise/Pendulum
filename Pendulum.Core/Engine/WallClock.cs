namespace Pendulum.Core.Engine;

/// Reminder times are wall-clock times (what the clock on the wall says), kept with no time zone
/// so a daily 09:00 stays 09:00 whatever the season. A relative time ("in 20 minutes", a snooze)
/// is different: it's a real span from now, so it's worked out in UTC and only then turned into
/// the wall-clock time it lands on. That way it's right across the clocks going back or forward.
public static class WallClock
{
    /// The next local date boundary as a real instant, for scheduling a calendar rollover.
    public static DateTimeOffset NextDateBoundary(DateTimeOffset now, TimeZoneInfo zone)
    {
        var midnight = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1);
        if (zone.IsInvalidTime(midnight))
        {
            // A gap can start before midnight and end partway through the new date. Find
            // its exact end, rather than assuming a one-hour gap or rounding to a minute.
            var lastInvalid = midnight;
            var firstValid = midnight.AddHours(1);
            while (zone.IsInvalidTime(firstValid))
            {
                lastInvalid = firstValid;
                firstValid = firstValid.AddHours(1);
            }
            while (firstValid.Ticks - lastInvalid.Ticks > 1)
            {
                var middle = lastInvalid.AddTicks((firstValid.Ticks - lastInvalid.Ticks) / 2);
                if (zone.IsInvalidTime(middle)) lastInvalid = middle;
                else firstValid = middle;
            }
            midnight = firstValid;
        }

        if (zone.IsAmbiguousTime(midnight))
        {
            // The larger offset gives the earlier instant. A rollback can cross the date:
            // if that first midnight already passed, use the next occurrence still ahead.
            return zone.GetAmbiguousTimeOffsets(midnight)
                .Select(offset => new DateTimeOffset(midnight, offset).ToUniversalTime())
                .Where(instant => instant > now)
                .Min();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone));
    }

    /// The real instant <paramref name="span"/> after <paramref name="now"/>, as a UTC time and as
    /// the wall-clock time it reads in <paramref name="zone"/> (the machine's own, unless given).
    /// <paramref name="now"/> is best DateTime.Now or a UTC time: a Local DateTime from the clock
    /// knows which of a repeated hour it's in, where a bare wall-clock time can't.
    public static (DateTime Wall, DateTime Utc) AddRealTime(DateTime now, TimeSpan span, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var utc = ToUtc(now, zone).Add(span);
        return (DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone), DateTimeKind.Unspecified), utc);
    }

    /// <paramref name="value"/> as UTC: a UTC time as it is, the clock's Local time by its own
    /// reckoning, and a wall-clock time as <paramref name="zone"/> reads it. A wall time the clocks
    /// skipped (the hour they go forward) counts as just after the gap.
    public static DateTime ToUtc(DateTime value, TimeZoneInfo zone)
    {
        switch (value.Kind)
        {
            case DateTimeKind.Utc:
                return value;
            case DateTimeKind.Local when ReferenceEquals(zone, TimeZoneInfo.Local) || zone.Id == TimeZoneInfo.Local.Id:
                return value.ToUniversalTime();
        }

        var wall = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall))
        {
            // In the gap: step forward until the clock reads again.
            var step = wall;
            for (var i = 0; i < 4 * 60 && zone.IsInvalidTime(step); i++)
                step = step.AddMinutes(1);
            wall = step;
        }
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }
}
