namespace ForgeLinkSms.Core.Models;

public enum ScheduleRepeat
{
    None,
    Daily,
    Weekdays,
    Weekly,
    Monthly,
    Yearly
}

public sealed record ScheduledSend(DateTimeOffset SendAtUtc, ScheduleRepeat Repeat);

public sealed record ScheduledEdit(int Id, string Body, DateTimeOffset SendAtUtc, ScheduleRepeat Repeat);
