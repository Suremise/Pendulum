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

    private static readonly Regex RelativeTime =
        new(@"\bin\s+(\d+)\s*(minutes?|mins?|hours?|hrs?)\b", RegexOptions.IgnoreCase);

    // Without an am/pm suffix, leave the separating whitespace for weekday adjacency checks.
    private static readonly Regex TimeWithAt =
        new(@"\bat\s+(\d{1,2})(?::(\d{2}))?(?:\s*(am|pm))?\b", RegexOptions.IgnoreCase);

    private static readonly Regex TimeWithColon =
        new(@"\b(\d{1,2}):(\d{2})(?:\s*(am|pm))?\b", RegexOptions.IgnoreCase);

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
        var (when, dueUtc) = ResolveRelative(ref text, now, zone);
        QuickAddRefusal? refusal = null;
        if (when is null)
        {
            var day = ResolveDay(ref text, now);
            (when, refusal) = ApplyTime(ref text, now, day);
        }

        // Every time is a wall-clock time, whatever kind of DateTime "now" was.
        var wall = when is { } w ? DateTime.SpecifyKind(w, DateTimeKind.Unspecified) : (DateTime?)null;
        return new QuickAddResult(CleanName(text), wall, dueUtc, refusal);
    }

    private enum DayKind { None, Tomorrow, Today, Weekday }
    private readonly record struct ResolvedDay(DayKind Kind, DateTime Date);
    private sealed record ParsedTime(Match Match, TimeSpan Time);

    private static (DateTime? When, DateTime? DueUtc) ResolveRelative(ref string text, DateTime now, TimeZoneInfo? zone)
    {
        var matches = RelativeTime.Matches(text).ToArray();
        // Preserve minutes-before-hours priority, regardless of position. Only the first
        // phrase of each unit is tried, even if its amount is invalid or too large.
        foreach (var minutesPerUnit in new[] { 1, 60 })
        {
            var match = matches.FirstOrDefault(m =>
                (m.Groups[2].Value[0] is 'm' or 'M') == (minutesPerUnit == 1));
            if (match is not null && TryAddRelative(now, match, minutesPerUnit, zone, out var when, out var dueUtc))
            {
                text = Remove(text, match);
                return (when, dueUtc);
            }
        }
        return (null, null);
    }

    private static ResolvedDay ResolveDay(ref string text, DateTime now)
    {
        var tomorrow = TomorrowWord.Match(text);
        if (tomorrow.Success)
        {
            text = Remove(text, tomorrow);
            return new(DayKind.Tomorrow, now.Date.AddDays(1));
        }
        var today = TodayWord.Match(text);
        if (today.Success)
        {
            text = Remove(text, today);
            return new(DayKind.Today, now.Date);
        }
        var (start, length, dayIndex) = FindWeekday(text);
        if (dayIndex < 0)
            return new(DayKind.None, now.Date);
        var delta = (dayIndex - (int)now.DayOfWeek + 7) % 7;
        text = text[..start] + " " + text[(start + length)..];
        return new(DayKind.Weekday, now.Date.AddDays(delta));
    }

    private static (DateTime? When, QuickAddRefusal? Refusal) ApplyTime(ref string text, DateTime now, ResolvedDay day)
    {
        var time = ExtractTime(ref text);
        // "today" alone means the next whole hour, including midnight late in the evening.
        if (time is null && day.Kind == DayKind.Today)
            return (now.Date.AddHours(now.Hour + 1), null);

        DateTime? when = time is { } t ? day.Date + t
            : day.Kind == DayKind.None ? null : day.Date.AddHours(9);
        if (when is null || when > now)
            return (when, null);

        return day.Kind switch
        {
            DayKind.None => (when.Value.AddDays(1), null),
            DayKind.Weekday => (when.Value.AddDays(7), null),
            DayKind.Today => (null, QuickAddRefusal.TimePassedToday),
            _ => (when, null),
        };
    }

    /// Takes the first valid match in the existing pattern order, leaving invalid times alone.
    private static TimeSpan? ExtractTime(ref string text)
    {
        var found = ValidTimes(text).FirstOrDefault();
        if (found is null)
            return null;
        text = Remove(text, found.Match);
        return found.Time;
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

    /// The one place that matches and validates time-of-day expressions. Pattern order is
    /// deliberate: "at" times before colon times before bare am/pm, not text position.
    private static IEnumerable<ParsedTime> ValidTimes(string text)
    {
        foreach (var pattern in new[] { TimeWithAt, TimeWithColon, TimeWithAmPm })
        {
            foreach (Match match in pattern.Matches(text))
            {
                var time = pattern == TimeWithAmPm
                    ? BuildTime(match.Groups[1].Value, string.Empty, match.Groups[2].Value)
                    : BuildTime(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
                if (time is { } valid)
                    yield return new ParsedTime(match, valid);
            }
        }
    }

    /// Whether a cached time ends just before start (spaces or a comma between).
    private static bool TimeJustBefore(string text, int start, IReadOnlyList<ParsedTime> times)
    {
        var gap = GapJustBefore.Match(text[..start]);
        return gap.Success && times.Any(t => t.Match.Index + t.Match.Length == gap.Index);
    }

    /// Whether a cached time starts just after end (spaces or a comma between).
    private static bool TimeJustAfter(string text, int end, IReadOnlyList<ParsedTime> times)
    {
        var gap = GapJustAfter.Match(text[end..]);
        return gap.Success && times.Any(t => t.Match.Index == end + gap.Length);
    }

    /// The weekday the text names, and where it is (with an "on" before it, which goes with it).
    /// A full name ("saturday") counts anywhere. A three-letter one counts only where it reads as a
    /// day: next to a time ("sat 9am", "9am sat"), after "on" ("on sat"), or as the last word
    /// ("gym sat"). So "buy sun cream 3pm" and "sat nav update 2pm" keep their words.
    private static (int Start, int Length, int DayIndex) FindWeekday(string text)
    {
        var words = Word.Matches(text);
        ParsedTime[]? times = null;
        // Full names and "on" need no time check. Once adjacency is needed, evaluate the
        // time patterns once and share the results across every candidate and both passes.
        IReadOnlyList<ParsedTime> Times() => times ??= ValidTimes(text).ToArray();
        foreach (var fullNames in new[] { true, false })
        {
            foreach (Match candidate in words)
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
                    || TimeJustBefore(text, candidate.Index, Times())
                    || TimeJustAfter(text, candidate.Index + candidate.Length, Times())
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
