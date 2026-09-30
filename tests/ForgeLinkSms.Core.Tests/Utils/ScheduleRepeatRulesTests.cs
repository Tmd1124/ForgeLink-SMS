using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ScheduleRepeatRulesTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi = 0) =>
        new(new DateTime(y, mo, d, h, mi, 0), Eastern.GetUtcOffset(new DateTime(y, mo, d, h, mi, 0)));

    private static DateTime LocalOf(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Eastern).DateTime;

    [Fact]
    public void A_text_that_does_not_repeat_has_no_next_time()
    {
        var at = Local(2026, 10, 5, 8);
        Assert.Null(ScheduleRepeatRules.Next(at, ScheduleRepeat.None, at, Eastern));
    }

    [Fact]
    public void The_first_time_is_used_while_it_is_still_in_the_future()
    {
        var at = Local(2026, 10, 5, 8);
        Assert.Equal(at, ScheduleRepeatRules.Next(at, ScheduleRepeat.Daily, at.AddHours(-3), Eastern));
    }

    [Theory]
    [InlineData(ScheduleRepeat.Daily, 2026, 10, 6)]
    [InlineData(ScheduleRepeat.Weekly, 2026, 10, 12)]
    [InlineData(ScheduleRepeat.Monthly, 2026, 11, 5)]
    [InlineData(ScheduleRepeat.Yearly, 2027, 10, 5)]
    public void Each_repeat_moves_to_the_following_occurrence(ScheduleRepeat repeat, int y, int m, int d)
    {
        var at = Local(2026, 10, 5, 8);
        Assert.Equal(new DateTime(y, m, d, 8, 0, 0), LocalOf(ScheduleRepeatRules.Next(at, repeat, at, Eastern)!.Value));
    }

    [Fact]
    public void Every_weekday_skips_the_weekend()
    {
        var friday = Local(2026, 10, 9, 8);
        Assert.Equal(new DateTime(2026, 10, 12, 8, 0, 0), LocalOf(ScheduleRepeatRules.Next(friday, ScheduleRepeat.Weekdays, friday, Eastern)!.Value));
    }

    [Fact]
    public void Keeps_the_same_local_time_across_a_daylight_saving_change()
    {
        var saturday = Local(2026, 10, 31, 8);
        var next = ScheduleRepeatRules.Next(saturday, ScheduleRepeat.Daily, saturday.AddDays(1).AddHours(-2), Eastern)!.Value;

        Assert.Equal(new DateTime(2026, 11, 1, 8, 0, 0), LocalOf(next));
        Assert.Equal(TimeSpan.FromHours(-5), Eastern.GetUtcOffset(next));
    }

    [Fact]
    public void A_time_that_does_not_exist_on_the_spring_forward_day_moves_an_hour_later()
    {
        var at = Local(2027, 3, 13, 2, 30);
        Assert.Equal(new DateTime(2027, 3, 14, 3, 30, 0), LocalOf(ScheduleRepeatRules.Next(at, ScheduleRepeat.Daily, at, Eastern)!.Value));
    }

    [Fact]
    public void Monthly_on_the_31st_uses_the_last_day_of_short_months_and_returns_to_the_31st()
    {
        var at = Local(2027, 1, 31, 9);
        var feb = ScheduleRepeatRules.Next(at, ScheduleRepeat.Monthly, at, Eastern)!.Value;
        var mar = ScheduleRepeatRules.Next(at, ScheduleRepeat.Monthly, feb, Eastern)!.Value;

        Assert.Equal(new DateTime(2027, 2, 28, 9, 0, 0), LocalOf(feb));
        Assert.Equal(new DateTime(2027, 3, 31, 9, 0, 0), LocalOf(mar));
    }

    [Fact]
    public void Yearly_on_february_29th_uses_the_28th_in_other_years()
    {
        var at = Local(2028, 2, 29, 9);
        Assert.Equal(new DateTime(2029, 2, 28, 9, 0, 0), LocalOf(ScheduleRepeatRules.Next(at, ScheduleRepeat.Yearly, at, Eastern)!.Value));
    }

    [Fact]
    public void Missed_times_are_skipped_so_the_next_time_is_in_the_future()
    {
        var at = Local(2026, 10, 5, 8);
        var phoneBackOn = Local(2026, 10, 9, 12);
        Assert.Equal(new DateTime(2026, 10, 10, 8, 0, 0), LocalOf(ScheduleRepeatRules.Next(at, ScheduleRepeat.Daily, phoneBackOn, Eastern)!.Value));
    }

    [Fact]
    public void Labels_read_naturally()
    {
        Assert.Equal("Doesn't repeat", ScheduleRepeatRules.Label(ScheduleRepeat.None));
        Assert.Equal("Every weekday (Mon–Fri)", ScheduleRepeatRules.Label(ScheduleRepeat.Weekdays));
        Assert.Equal(6, ScheduleRepeatRules.All.Count);
    }
}
