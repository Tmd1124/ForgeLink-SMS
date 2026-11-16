using System.Globalization;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public enum MessageLinkKind
{
    PlainText,
    Url,
    PhoneNumber,
    Address,
    Appointment
}

// Data for an Appointment segment is a DateTime when the text names a time,
// or a DateOnly when it names only a date (an all-day event).
public sealed record MessageTextSegment(string Text, MessageLinkKind Kind, object? Data = null);

public static partial class MessageLinkParser
{
    private const string StreetSuffixes =
        "St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Dr|Drive|Ln|Lane|Way|Ct|Court|Pl|Place|Cir|Circle|Ter|Terrace|Pkwy|Parkway";

    private const string Weekdays =
        "Mon(?:day)?|Tue(?:s(?:day)?)?|Wed(?:nesday)?|Thu(?:rs(?:day)?)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?";

    private const string Months =
        "January|February|March|April|May|June|July|August|September|October|November|December|" +
        "Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sept|Sep|Oct|Nov|Dec";

    private const string Time =
        @"(?:\d{1,2}(?::\d{2})?\s*(?:am|pm|a\.m\.|p\.m\.)|\d{1,2}:\d{2}|noon|midnight|(?<=\bat\s)\d{1,2})(?!\d)";

    private const string RelativeDay = @"(?:(?:next\s+|this\s+|on\s+)?(?:" + Weekdays + @")|today|tonight|tomorrow|tmrw)\b";

    private const string CalendarDate =
        $@"(?:\b\d{{4}}-\d{{1,2}}-\d{{1,2}}\b|\b\d{{1,2}}/\d{{1,2}}(?:/\d{{2,4}})?\b" +
        $@"|\b(?:{Months})\.?\s+\d{{1,2}}(?:st|nd|rd|th)?\b(?:,?\s+\d{{4}}\b)?" +
        $@"|\b\d{{1,2}}(?:st|nd|rd|th)?\s+(?:of\s+)?(?:{Months})\b\.?(?:,?\s+\d{{4}}\b)?)";

    private const string AnyDate = $@"(?:{CalendarDate}|{RelativeDay})";

    private const string Joiner = @"(?:\s*(?:,|@|-|at|on|around|by)?\s*)";

    // A bare weekday or "today" alone is too common in ordinary prose to link,
    // so those only count when paired with a time.
    private const string DateTimeAlternation =
        $@"\b{AnyDate}{Joiner}{Time}|\b{Time}{Joiner}{AnyDate}|\b{Time}|{CalendarDate}";

    private const string UrlTail = @"[^\s]*?(?=[.,;:!?)\]]*(?:\s|$))";

    // Alternation order doubles as detector priority: a match earlier in this
    // list wins over one later in it when they'd otherwise overlap.
    [GeneratedRegex(
        $@"(?<url>\bhttps?://{UrlTail}|\bwww\.{UrlTail}" +
        $@"|(?<![@\w.-])[a-z0-9][a-z0-9-]*(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|co|us|gov|edu|app|dev|ai|me|ly|info|biz)\b(?:/{UrlTail})?)" +
        $@"|(?<phone>(?:\+?1[-.\s]?)?\(?\d{{3}}\)?[-.\s]\d{{3}}[-.\s]\d{{4}}\b)" +
        $@"|(?<address>\b\d{{1,6}}\s+(?:[A-Z][a-zA-Z]*\s){{1,4}}(?:{StreetSuffixes})\b\.?(?:,\s*[A-Za-z][A-Za-z\s]*)?(?:,\s*[A-Z]{{2}})?(?:\s+\d{{5}}(?:-\d{{4}})?)?)" +
        $@"|(?<appt>{DateTimeAlternation})",
        RegexOptions.IgnoreCase)]
    private static partial Regex EntityRegex();

    public static IReadOnlyList<MessageTextSegment> Parse(string text, DateTime? now = null)
    {
        var reference = now ?? DateTime.Now;
        var segments = new List<MessageTextSegment>();
        var lastIndex = 0;

        foreach (Match match in EntityRegex().Matches(text))
        {
            if (match.Index > lastIndex)
            {
                segments.Add(new MessageTextSegment(text[lastIndex..match.Index], MessageLinkKind.PlainText));
            }

            if (match.Groups["url"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.Url, BuildUrl(match.Value)));
            }
            else if (match.Groups["phone"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.PhoneNumber, PhoneNumberFormatter.ToComparableDigits(match.Value)));
            }
            else if (match.Groups["address"].Success)
            {
                segments.Add(new MessageTextSegment(match.Value, MessageLinkKind.Address, match.Value));
            }
            else if (match.Groups["appt"].Success)
            {
                var parsed = TryParseAppointment(match.Value, reference);
                segments.Add(parsed is null
                    ? new MessageTextSegment(match.Value, MessageLinkKind.PlainText)
                    : new MessageTextSegment(match.Value, MessageLinkKind.Appointment, parsed));
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            segments.Add(new MessageTextSegment(text[lastIndex..], MessageLinkKind.PlainText));
        }

        if (segments.Count == 0)
        {
            segments.Add(new MessageTextSegment(text, MessageLinkKind.PlainText));
        }

        return segments;
    }

    private static string BuildUrl(string raw) => raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : "https://" + raw;

    [GeneratedRegex(@"(\d{1,2})(?::(\d{2}))?\s*([ap])\.?m\.?", RegexOptions.IgnoreCase)]
    private static partial Regex TimeWithMeridiemRegex();

    [GeneratedRegex(@"\b(\d{1,2}):(\d{2})\b")]
    private static partial Regex Time24Regex();

    [GeneratedRegex(@"\bat\s+(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeAfterAtRegex();

    [GeneratedRegex(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex($@"\b({Months})\.?\s+(\d{{1,2}})(?:st|nd|rd|th)?\b(?:,?\s+(\d{{4}})\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayRegex();

    [GeneratedRegex($@"\b(\d{{1,2}})(?:st|nd|rd|th)?\s+(?:of\s+)?({Months})\b\.?(?:,?\s+(\d{{4}})\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthRegex();

    [GeneratedRegex($@"\b({Weekdays})\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayRegex();

    private static readonly string[] MonthAbbreviations =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static object? TryParseAppointment(string text, DateTime reference)
    {
        var time = ParseTime(text);
        var date = ResolveDate(text, reference);

        if (time is null && date is null)
        {
            return null;
        }

        if (time is null)
        {
            return DateOnly.FromDateTime(date!.Value);
        }

        if (date is null)
        {
            var today = reference.Date + time.Value;
            return today > reference ? today : today.AddDays(1);
        }

        return date.Value.Date + time.Value;
    }

    private static TimeSpan? ParseTime(string text)
    {
        if (text.Contains("noon", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(12);
        }

        if (text.Contains("midnight", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.Zero;
        }

        var m = TimeWithMeridiemRegex().Match(text);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (hour is < 1 or > 12 || minute > 59)
            {
                return null;
            }

            var pm = char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p';
            return new TimeSpan(hour % 12 + (pm ? 12 : 0), minute, 0);
        }

        m = Time24Regex().Match(text);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return hour <= 23 && minute <= 59 ? new TimeSpan(hour, minute, 0) : null;
        }

        m = TimeAfterAtRegex().Match(text);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (hour is < 1 or > 12)
            {
                return null;
            }

            // No explicit am/pm: "tonight" and hours 1-6 mean evening ("call at 7" == 7pm).
            var evening = text.Contains("tonight", StringComparison.OrdinalIgnoreCase) || hour <= 6;
            return TimeSpan.FromHours(evening ? hour + 12 : hour);
        }

        return null;
    }

    private static DateTime? ResolveDate(string text, DateTime reference)
    {
        if (text.Contains("tomorrow", StringComparison.OrdinalIgnoreCase) || text.Contains("tmrw", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Date.AddDays(1);
        }

        if (text.Contains("tonight", StringComparison.OrdinalIgnoreCase) || text.Contains("today", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Date;
        }

        var iso = IsoDateRegex().Match(text);
        if (iso.Success)
        {
            return SafeDate(
                int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture),
                false, reference);
        }

        var numeric = NumericDateRegex().Match(text);
        if (numeric.Success)
        {
            var hasYear = numeric.Groups[3].Success;
            return SafeDate(
                hasYear ? NormalizeYear(numeric.Groups[3].Value) : reference.Year,
                int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture),
                !hasYear, reference);
        }

        var monthDay = MonthDayRegex().Match(text);
        var dayMonth = DayMonthRegex().Match(text);
        if (dayMonth.Success && (!monthDay.Success || dayMonth.Index < monthDay.Index))
        {
            return NamedMonthDate(dayMonth.Groups[2].Value, dayMonth.Groups[1].Value, dayMonth.Groups[3], reference);
        }

        if (monthDay.Success)
        {
            return NamedMonthDate(monthDay.Groups[1].Value, monthDay.Groups[2].Value, monthDay.Groups[3], reference);
        }

        var weekday = WeekdayRegex().Match(text);
        if (weekday.Success)
        {
            return NextWeekday(reference.Date, weekday.Value);
        }

        return null;
    }

    private static DateTime? NamedMonthDate(string monthText, string dayText, Group yearGroup, DateTime reference)
    {
        var month = Array.FindIndex(MonthAbbreviations, a => monthText.StartsWith(a, StringComparison.OrdinalIgnoreCase)) + 1;
        var day = int.Parse(dayText, CultureInfo.InvariantCulture);
        var hasYear = yearGroup.Success;
        var year = hasYear ? int.Parse(yearGroup.Value, CultureInfo.InvariantCulture) : reference.Year;
        return SafeDate(year, month, day, !hasYear, reference);
    }

    // A yearless date that is clearly in the past (more than a month back) means next year's.
    private static DateTime? SafeDate(int year, int month, int day, bool rollForward, DateTime reference)
    {
        if (month is < 1 or > 12 || year is < 1 or > 9998 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        var date = new DateTime(year, month, day);
        if (rollForward && reference.Date - date > TimeSpan.FromDays(30) && day <= DateTime.DaysInMonth(year + 1, month))
        {
            return new DateTime(year + 1, month, day);
        }

        return date;
    }

    private static int NormalizeYear(string raw)
    {
        var year = int.Parse(raw, CultureInfo.InvariantCulture);
        return year < 100 ? 2000 + year : year;
    }

    private static DateTime NextWeekday(DateTime fromDate, string weekdayText)
    {
        var target = weekdayText[..3].ToLowerInvariant() switch
        {
            "mon" => DayOfWeek.Monday,
            "tue" => DayOfWeek.Tuesday,
            "wed" => DayOfWeek.Wednesday,
            "thu" => DayOfWeek.Thursday,
            "fri" => DayOfWeek.Friday,
            "sat" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };

        var daysAhead = ((int)target - (int)fromDate.DayOfWeek + 7) % 7;
        daysAhead = daysAhead == 0 ? 7 : daysAhead;
        return fromDate.AddDays(daysAhead);
    }
}
