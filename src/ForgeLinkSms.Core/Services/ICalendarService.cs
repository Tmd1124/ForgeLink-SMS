namespace ForgeLinkSms.Core.Services;

public interface ICalendarService
{
    Task OpenNewEventAsync(string title, DateTime start, DateTime end, bool allDay = false);
}
