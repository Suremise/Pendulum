using System.Text.RegularExpressions;

namespace Pendulum.Core.Parsing;

/// The parsed outcome of a quick-add phrase. <see cref="When"/> is null when no
/// recognizable time expression was found in the input.
public sealed record QuickAddResult(string Name, DateTime? When);

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

    private static readonly Regex TimeWithAmPm =
        new(@"\b(\d{1,2})\s*(am|pm)\b", RegexOptions.IgnoreCase);

    private static readonly Regex TomorrowWord = new(@"\btomorrow\b", RegexOptions.IgnoreCase);
    private static readonly Regex TodayWord = new(@"\btoday\b", RegexOptions.IgnoreCase);
    private static readonly Regex Word = new(@"\b[a-zA-Z]+\b");

    public static QuickAddResult Parse(string input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new QuickAddResult(string.Empty, null);

        var text = input.Trim();
        DateTime? when = null;

        var relativeMatch = RelativeMinutes.Match(text);
        if (relativeMatch.Success)
        {
            when = now.AddMinutes(int.Parse(relativeMatch.Groups[1].Value));
            text = Remove(text, relativeMatch);
        }
        else
        {
            relativeMatch = RelativeHours.Match(text);
            if (relativeMatch.Success)
            {
                when = now.AddHours(int.Parse(relativeMatch.Groups[1].Value));
                text = Remove(text, relativeMatch);
            }
        }

        if (when is null)
        {
            var baseDate = now.Date;
            var explicitDay = false;

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
                    text = Remove(text, todayMatch);
                }
                else
                {
                    var (dayWord, dayIndex) = FindWeekday(text);
                    if (dayIndex >= 0)
                    {
                        var delta = ((dayIndex - (int)now.DayOfWeek) + 7) % 7;
                        baseDate = now.Date.AddDays(delta);
                        explicitDay = true;
                        text = RemoveWord(text, dayWord);
                    }
                }
            }

            var time = ExtractTime(ref text);

            if (time is not null)
            {
                when = baseDate + time.Value;
                if (!explicitDay && when <= now)
                    when = when.Value.AddDays(1);
            }
            else if (explicitDay)
            {
                // A day was named but no time — default to a reasonable morning time
                // rather than silently dropping the day the user typed.
                when = baseDate.AddHours(9);
            }
        }

        return new QuickAddResult(CleanName(text), when);
    }

    private static TimeSpan? ExtractTime(ref string text)
    {
        var match = TimeWithAt.Match(text);
        if (!match.Success)
            match = TimeWithColon.Match(text);

        if (match.Success)
        {
            var time = BuildTime(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
            text = Remove(text, match);
            return time;
        }

        match = TimeWithAmPm.Match(text);
        if (match.Success)
        {
            var time = BuildTime(match.Groups[1].Value, string.Empty, match.Groups[2].Value);
            text = Remove(text, match);
            return time;
        }

        return null;
    }

    private static TimeSpan BuildTime(string hourText, string minuteText, string amPm)
    {
        var hour = int.Parse(hourText);
        var minute = string.IsNullOrEmpty(minuteText) ? 0 : int.Parse(minuteText);

        if (!string.IsNullOrEmpty(amPm))
        {
            hour %= 12;
            if (amPm.Equals("pm", StringComparison.OrdinalIgnoreCase))
                hour += 12;
        }

        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return new TimeSpan(hour, minute, 0);
    }

    private static (string Word, int DayIndex) FindWeekday(string text)
    {
        foreach (Match candidate in Word.Matches(text))
        {
            var word = candidate.Value.ToLowerInvariant();
            for (var i = 0; i < DayNames.Length; i++)
            {
                if (word == DayNames[i] || word == DayAbbreviations[i])
                    return (candidate.Value, i);
            }
        }

        return (string.Empty, -1);
    }

    private static string Remove(string text, Match match) =>
        text[..match.Index] + " " + text[(match.Index + match.Length)..];

    private static string RemoveWord(string text, string word) =>
        Regex.Replace(text, $@"\b{Regex.Escape(word)}\b", " ", RegexOptions.IgnoreCase);

    private static string CleanName(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '-', '.');
}
