using System.Text.RegularExpressions;
using Pendulum.Core.Engine;

namespace Pendulum.Core.Parsing;

/// Why a phrase that names a time gives no reminder.
public enum QuickAddRefusal
{
    /// "today" with a time that has already gone ("report today 9am", typed at 10:00).
    TimePassedToday,
}

/// The parsed outcome of a quick-add phrase. <see cref="When"/> is null when no
/// recognizable time expression was found in the input, or when <see cref="Refusal"/> says why
/// the one found can't be used.
/// <see cref="DueUtc"/> is set for a time given as a span from now ("in 20 min"): the real
/// instant, for TriggerTimer.SetDue.
public sealed record QuickAddResult(string Name, DateTime? When, DateTime? DueUtc = null, QuickAddRefusal? Refusal = null);

/// Rule-based (non-LLM) parser for short reminder phrases like "Call mom 3pm" or
/// "Standup tomorrow 9am". Recognizes a small, explicit set of time expressions —
/// anything outside that set is left as part of the reminder name instead of being
/// misread as a time.
public static class QuickAddParser
{
    private static readonly string[] DayNames =
        { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    private static readonly string[] DayAbbreviations =
        { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    private static readonly Regex RelativeMinutes =
        new(@"\bin\s+(\d+)\s*(minutes?|mins?)\b", RegexOptions.IgnoreCase);

    private static readonly Regex RelativeHours =
        new(@"\bin\s+(\d+)\s*(hours?|hrs?)\b", RegexOptions.IgnoreCase);

    private static readonly Regex TimeWithAt =
        new(@"\bat\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b", RegexOptions.IgnoreCase);

    private static readonly Regex TimeWithColon =
        new(@"\b(\d{1,2}):(\d{2})\s*(am|pm)?\b", RegexOptions.IgnoreCase);

    // Not straight after a colon or a digit, so the end of a time already turned down ("25:10pm")
    // isn't read again as an hour of its own.
    private static readonly Regex TimeWithAmPm =
        new(@"(?<![:\d])\b(\d{1,2})\s*(am|pm)\b", RegexOptions.IgnoreCase);

    private static readonly Regex TomorrowWord = new(@"\btomorrow\b", RegexOptions.IgnoreCase);
    private static readonly Regex TodayWord = new(@"\btoday\b", RegexOptions.IgnoreCase);
    private static readonly Regex Word = new(@"\b[a-zA-Z]+\b");

    /// Reads <paramref name="input"/> as typed at <paramref name="now"/> (best DateTime.Now). A span
    /// from now is real time in <paramref name="zone"/> (the machine's own, unless given).
    public static QuickAddResult Parse(string input, DateTime now, TimeZoneInfo? zone = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new QuickAddResult(string.Empty, null);

        var text = input.Trim();
        DateTime? when = null;

        var relativeMatch = RelativeMinutes.Match(text);
        DateTime? dueUtc = null;
        if (relativeMatch.Success && TryAddRelative(now, relativeMatch, minutesPerUnit: 1, zone, out var inMinutes, out var inMinutesUtc))
        {
            when = inMinutes;
            dueUtc = inMinutesUtc;
            text = Remove(text, relativeMatch);
        }
        else
        {
            relativeMatch = RelativeHours.Match(text);
            if (relativeMatch.Success && TryAddRelative(now, relativeMatch, minutesPerUnit: 60, zone, out var inHours, out var inHoursUtc))
            {
                when = inHours;
                dueUtc = inHoursUtc;
                text = Remove(text, relativeMatch);
            }
        }

        QuickAddRefusal? refusal = null;
        if (when is null)
        {
            var baseDate = now.Date;
            var explicitDay = false;

            var namedWeekday = false;
            var namedToday = false;

            var tomorrowMatch = TomorrowWord.Match(text);
            if (tomorrowMatch.Success)
            {
                baseDate = now.Date.AddDays(1);
                explicitDay = true;
                text = Remove(text, tomorrowMatch);
            }
            else
            {
                var todayMatch = TodayWord.Match(text);
                if (todayMatch.Success)
                {
                    explicitDay = true;
                    namedToday = true;
                    text = Remove(text, todayMatch);
                }
                else
                {
                    var (dayStart, dayLength, dayIndex) = FindWeekday(text);
                    if (dayIndex >= 0)
                    {
                        var delta = ((dayIndex - (int)now.DayOfWeek) + 7) % 7;
                        baseDate = now.Date.AddDays(delta);
                        explicitDay = true;
                        namedWeekday = true;
                        text = text[..dayStart] + " " + text[(dayStart + dayLength)..];
                    }
                }
            }

            var time = ExtractTime(ref text);

            if (time is not null)
            {
                when = baseDate + time.Value;
                if (!explicitDay && when <= now)
                    when = when.Value.AddDays(1);
                // Today's own weekday with a time already gone means next week's, not a moment ago.
                else if (namedWeekday && when <= now)
                    when = when.Value.AddDays(7);
                // "today" with a time already gone can't be: say so rather than ring at once.
                else if (namedToday && when <= now)
                {
                    when = null;
                    refusal = QuickAddRefusal.TimePassedToday;
                }
            }
            else if (namedToday)
            {
                // "today" alone: the next whole hour (10:20 gives 11:00; 10:00 gives 11:00 too).
                when = now.Date.AddHours(now.Hour + 1);
            }
            else if (explicitDay)
            {
                // A day was named but no time — default to a reasonable morning time
                // rather than silently dropping the day the user typed. Today's own weekday, once
                // that time has gone, means next week's.
                when = baseDate.AddHours(9);
                if (namedWeekday && when <= now)
                    when = when.Value.AddDays(7);
            }
        }

        // Every time is a wall-clock time, whatever kind of DateTime "now" was.
        var wall = when is { } w ? DateTime.SpecifyKind(w, DateTimeKind.Unspecified) : (DateTime?)null;
        return new QuickAddResult(CleanName(text), wall, dueUtc, refusal);
    }

    /// The first real time in the text: "at 9", "at 9:30pm", "14:30", "3pm". A number that isn't
    /// a time of day ("13pm", "25:00", "10:75", "at 42 Main St") is left in the text instead.
    private static TimeSpan? ExtractTime(ref string text)
    {
        foreach (Match match in TimeWithAt.Matches(text))
        {
            if (BuildTime(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value) is { } time)
            {
                text = Remove(text, match);
                return time;
            }
        }

        foreach (Match match in TimeWithColon.Matches(text))
        {
            if (BuildTime(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value) is { } time)
            {
                text = Remove(text, match);
                return time;
            }
        }

        foreach (Match match in TimeWithAmPm.Matches(text))
        {
            if (BuildTime(match.Groups[1].Value, string.Empty, match.Groups[2].Value) is { } time)
            {
                text = Remove(text, match);
                return time;
            }
        }

        return null;
    }

    private static TimeSpan? BuildTime(string hourText, string minuteText, string amPm)
    {
        var hour = int.Parse(hourText);
        var minute = string.IsNullOrEmpty(minuteText) ? 0 : int.Parse(minuteText);

        if (minute > 59)
            return null;

        if (!string.IsNullOrEmpty(amPm))
        {
            if (hour is < 1 or > 12)
                return null;

            hour %= 12;
            if (amPm.Equals("pm", StringComparison.OrdinalIgnoreCase))
                hour += 12;
        }
        else if (hour > 23)
        {
            return null;
        }

        return new TimeSpan(hour, minute, 0);
    }

    /// "in 20 min", "in 2 hours": false for an amount too large to be a reminder time, so it
    /// stays part of the text rather than overflowing.
    private static bool TryAddRelative(DateTime now, Match match, int minutesPerUnit, TimeZoneInfo? zone, out DateTime when, out DateTime dueUtc)
    {
        when = default;
        dueUtc = default;
        if (!int.TryParse(match.Groups[1].Value, out var amount))
            return false;

        // A day's margin, for the time zone's offset either side of the sum.
        var minutes = (double)amount * minutesPerUnit;
        if (minutes > (DateTime.MaxValue - now).TotalMinutes - 24 * 60)
            return false;

        (when, dueUtc) = WallClock.AddRealTime(now, TimeSpan.FromMinutes(minutes), zone);
        return true;
    }

    private static readonly Regex OnJustBefore = new(@"\bon\s+$", RegexOptions.IgnoreCase);
    private static readonly Regex GapJustBefore = new(@"[\s,]+$");
    private static readonly Regex GapJustAfter = new(@"^[\s,]+");

    /// Every real time in the text, as ExtractTime would take one: the same patterns and the same
    /// range checks, so "at 42", "25:10pm" or "123pm" aren't times here either.
    private static IEnumerable<Match> ValidTimes(string text) =>
        new[] { TimeWithAt, TimeWithColon }
            .SelectMany(r => r.Matches(text))
            .Where(m => BuildTime(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) is not null)
            .Concat(TimeWithAmPm.Matches(text)
                .Where(m => BuildTime(m.Groups[1].Value, string.Empty, m.Groups[2].Value) is not null));

    /// Whether a real time ends just before <paramref name="start"/> (spaces or a comma between).
    private static bool TimeJustBefore(string text, int start)
    {
        var gap = GapJustBefore.Match(text[..start]);
        return gap.Success && ValidTimes(text).Any(m => m.Index + m.Length == gap.Index);
    }

    /// Whether a real time starts just after <paramref name="end"/> (spaces or a comma between).
    private static bool TimeJustAfter(string text, int end)
    {
        var gap = GapJustAfter.Match(text[end..]);
        return gap.Success && ValidTimes(text).Any(m => m.Index == end + gap.Length);
    }

    /// The weekday the text names, and where it is (with an "on" before it, which goes with it).
    /// A full name ("saturday") counts anywhere. A three-letter one counts only where it reads as a
    /// day: next to a time ("sat 9am", "9am sat"), after "on" ("on sat"), or as the last word
    /// ("gym sat"). So "buy sun cream 3pm" and "sat nav update 2pm" keep their words.
    private static (int Start, int Length, int DayIndex) FindWeekday(string text)
    {
        foreach (var fullNames in new[] { true, false })
        {
            foreach (Match candidate in Word.Matches(text))
            {
                var names = fullNames ? DayNames : DayAbbreviations;
                var dayIndex = Array.IndexOf(names, candidate.Value.ToLowerInvariant());
                if (dayIndex < 0)
                    continue;

                var before = text[..candidate.Index];
                var after = text[(candidate.Index + candidate.Length)..];
                var on = OnJustBefore.Match(before);

                var readsAsDay = fullNames
                    || on.Success
                    || TimeJustBefore(text, candidate.Index)
                    || TimeJustAfter(text, candidate.Index + candidate.Length)
                    || (after.Trim(' ', ',', '.').Length == 0 && before.Trim().Length > 0);
                if (!readsAsDay)
                    continue;

                var start = on.Success ? on.Index : candidate.Index;
                return (start, candidate.Index + candidate.Length - start, dayIndex);
            }
        }

        return (0, 0, -1);
    }

    private static string Remove(string text, Match match) =>
        text[..match.Index] + " " + text[(match.Index + match.Length)..];

    private static string CleanName(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '-', '.');
}
