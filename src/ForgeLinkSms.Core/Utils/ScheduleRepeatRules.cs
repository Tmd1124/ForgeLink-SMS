using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class ScheduleRepeatRules
{
    public static IReadOnlyList<ScheduleRepeat> All { get; } = Enum.GetValues<ScheduleRepeat>();

    public static string Label(ScheduleRepeat repeat) => repeat switch
    {
        ScheduleRepeat.Daily => "Every day",
        ScheduleRepeat.Weekdays => "Every weekday (Mon–Fri)",
        ScheduleRepeat.Weekly => "Every week",
        ScheduleRepeat.Monthly => "Every month",
        ScheduleRepeat.Yearly => "Every year",
        _ => "Doesn't repeat"
    };

    // Occurrences are counted from the first time in local wall-clock terms, so 8:00 AM stays 8:00 AM
    // across daylight saving, and a 31st-of-the-month text returns to the 31st after a short month.
    // Returns the first occurrence strictly after afterUtc; missed ones are skipped rather than sent late.
    public static DateTimeOffset? Next(DateTimeOffset firstUtc, ScheduleRepeat repeat, DateTimeOffset afterUtc, TimeZoneInfo zone)
    {
        if (repeat == ScheduleRepeat.None)
        {
            return null;
        }
        if (firstUtc > afterUtc)
        {
            return firstUtc;
        }

        var first = TimeZoneInfo.ConvertTime(firstUtc, zone).DateTime;
        var day = first;
        for (var n = 1; ; n++)
        {
            DateTime local;
            if (repeat == ScheduleRepeat.Weekdays)
            {
                do
                {
                    day = day.AddDays(1);
                }
                while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
                local = day;
            }
            else
            {
                local = repeat switch
                {
                    ScheduleRepeat.Daily => first.AddDays(n),
                    ScheduleRepeat.Weekly => first.AddDays(7 * n),
                    ScheduleRepeat.Monthly => first.AddMonths(n),
                    _ => first.AddYears(n)
                };
            }

            var candidate = ToUtc(local, zone);
            if (candidate > afterUtc)
            {
                return candidate;
            }
        }
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
