using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class CalendarService : ICalendarService
{
    public Task OpenNewEventAsync(string title, DateTime start, DateTime end, bool allDay = false)
    {
        var intent = new Intent(Intent.ActionInsert);
        intent.SetData(CalendarContract.Events.ContentUri);
        intent.PutExtra(CalendarContract.ExtraEventBeginTime, ToMillis(start, allDay));
        intent.PutExtra(CalendarContract.ExtraEventEndTime, ToMillis(end, allDay));
        intent.PutExtra(CalendarContract.ExtraEventAllDay, allDay);
        intent.PutExtra(CalendarContract.Events.InterfaceConsts.Title, title);
        intent.AddFlags(ActivityFlags.NewTask);

        var context = (Context?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? AndroidApp.Context;
        context.StartActivity(intent);

        return Task.CompletedTask;
    }

    // All-day events are stored as UTC midnight; timed ones use the local zone.
    private static long ToMillis(DateTime value, bool allDay) =>
        allDay
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : new DateTimeOffset(value).ToUnixTimeMilliseconds();
}
