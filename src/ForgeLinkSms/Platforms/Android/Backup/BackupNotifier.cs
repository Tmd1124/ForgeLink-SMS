using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Work;

namespace ForgeLinkSms.Platforms.Android.Backup;

internal static class BackupNotifier
{
    private const string ChannelId = "backup";
    public const int ProgressId = 7301;
    public const int ResultId = 7302;

    private static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            if (manager.GetNotificationChannel(ChannelId) is null)
            {
                manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Backup and restore", NotificationImportance.Low));
            }
        }
    }

    public static ForegroundInfo Progress(Context context, string title, int done, int total, Java.Util.UUID workId)
    {
        EnsureChannel(context);
        var cancel = WorkManager.GetInstance(context).CreateCancelPendingIntent(workId);
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(total > 0 ? $"{done:N0} of {total:N0} messages" : "Getting ready…")
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)
            .SetOngoing(true)
            .SetProgress(Math.Max(total, 1), done, total == 0)
            .AddAction(0, "Cancel", cancel)
            .Build()!;
        return Build.VERSION.SdkInt >= BuildVersionCodes.Q
            ? new ForegroundInfo(ProgressId, notification, (int)global::Android.Content.PM.ForegroundService.TypeDataSync)
            : new ForegroundInfo(ProgressId, notification);
    }

    public static void Result(Context context, string title, string text)
    {
        EnsureChannel(context);
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownloadDone)
            .SetAutoCancel(true)
            .Build()!;
        NotificationManagerCompat.From(context).Notify(ResultId, notification);
    }
}
