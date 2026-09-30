using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class NotificationService : INotificationService
{
    private const string ChannelId = "incoming_sms";
    // Android fixes a channel's sound when it is created, so silent notifications need their own channel.
    private const string SilentChannelId = "incoming_sms_silent";
    private static int _notificationId;

    // One notification per conversation, so a new text replaces the last one and the Reply /
    // Mark as read buttons can clear it by id.
    public static int NotificationIdFor(long threadId) =>
        threadId != 0 ? (int)(threadId % int.MaxValue) : System.Threading.Interlocked.Increment(ref _notificationId) + int.MaxValue / 2;

    public void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address, bool withSound = true)
    {
        var context = AndroidApp.Context;
        EnsureChannels(context);

        var notificationId = NotificationIdFor(threadId);
        var route = $"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}";

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        launchIntent.PutExtra("initial_route", route);
        // Each notification needs its own PendingIntent request code — reusing one code across
        // notifications makes Android collapse them into a single PendingIntent, so tapping an
        // older notification would open whichever thread's route was set most recently.
        var contentIntent = PendingIntent.GetActivity(context, notificationId, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, withSound ? ChannelId : SilentChannelId)
            .SetSilent(!withSound)
            .SetContentTitle(fromDisplayName)
            .SetContentText(body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))
            .SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail)
            .SetAutoCancel(true)
            .SetContentIntent(contentIntent);

        if (threadId != 0)
        {
            // RemoteInput fills the typed text into the intent, which only works on a mutable PendingIntent.
            var replyInput = new AndroidX.Core.App.RemoteInput.Builder(NotificationActionReceiver.ReplyTextKey).SetLabel("Reply").Build();
            var replyIntent = NotificationActionReceiver.CreateIntent(context, NotificationActionReceiver.ReplyAction, threadId, address, notificationId);
            var replyPending = PendingIntent.GetBroadcast(context, notificationId * 2, replyIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
            var replyAction = new NotificationCompat.Action.Builder(0, "Reply", replyPending)
                .AddRemoteInput(replyInput)
                .SetAllowGeneratedReplies(true)
                .Build();

            var readIntent = NotificationActionReceiver.CreateIntent(context, NotificationActionReceiver.MarkReadAction, threadId, address, notificationId);
            var readPending = PendingIntent.GetBroadcast(context, notificationId * 2 + 1, readIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            builder.AddAction(replyAction).AddAction(0, "Mark as read", readPending);
        }

        var notification = builder.Build();

        NotificationManagerCompat.From(context).Notify(notificationId, notification);
    }

    private static void EnsureChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is null)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Incoming Messages", NotificationImportance.High));
        }
        if (manager.GetNotificationChannel(SilentChannelId) is null)
        {
            var silent = new NotificationChannel(SilentChannelId, "Incoming Messages (silent)", NotificationImportance.Low);
            silent.SetSound(null, null);
            silent.EnableVibration(false);
            manager.CreateNotificationChannel(silent);
        }
    }
}
