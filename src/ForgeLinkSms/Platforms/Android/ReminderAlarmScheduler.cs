using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidAlarmManager = global::Android.App.AlarmManager;
using AndroidAlarmType = global::Android.App.AlarmType;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContext = global::Android.Content.Context;

namespace ForgeLinkSms.Platforms.Android;

// Same inexact-but-Doze-safe alarm as snoozes and scheduled texts: no "Alarms & reminders" permission needed.
public class ReminderAlarmScheduler : IReminderAlarmScheduler
{
    public const string ReminderIdExtra = "reminder_id";

    public void Arm(int reminderId, DateTimeOffset remindAtUtc)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.SetAndAllowWhileIdle(AndroidAlarmType.RtcWakeup, remindAtUtc.ToUnixTimeMilliseconds(), PendingIntentFor(context, reminderId));
    }

    public void Disarm(int reminderId)
    {
        var context = AndroidApp.Context;
        var alarmManager = (AndroidAlarmManager)context.GetSystemService(AndroidContext.AlarmService)!;
        alarmManager.Cancel(PendingIntentFor(context, reminderId));
    }

    private static AndroidPendingIntent PendingIntentFor(AndroidContext context, int reminderId)
    {
        var intent = new AndroidIntent(context, typeof(ReminderAlarmReceiver)).SetPackage(context.PackageName);
        intent.PutExtra(ReminderIdExtra, reminderId);
        return AndroidPendingIntent.GetBroadcast(context, reminderId, intent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;
    }
}
