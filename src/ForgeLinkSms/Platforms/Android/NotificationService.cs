using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using ForgeLinkSms.Platforms.Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Bitmap = Android.Graphics.Bitmap;
using AndroidColor = Android.Graphics.Color;
using Person = AndroidX.Core.App.Person;
using SmsMessage = ForgeLinkSms.Core.Models.SmsMessage;
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

    public void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address, bool withSound = true) =>
        Post(AndroidApp.Context, threadId, address, fromDisplayName, body, withSound, new NotificationCompat.BigTextStyle().BigText(body), shortcutId: null);

    // Runs inside the SMS broadcast's time budget, so it reads only the chat's unread incoming
    // messages, and MMS without their photo data.
    private static List<SmsMessage> UnreadIncoming(Context context, long threadId)
    {
        var messages = new List<SmsMessage>();
        using (var cursor = context.ContentResolver!.Query(global::Android.Net.Uri.Parse("content://sms")!,
                   new[] { "_id", "address", "body", "date" }, "thread_id = ? AND read = 0 AND type = 1",
                   new[] { threadId.ToString() }, $"date DESC LIMIT {ConversationNotification.MaxLines}"))
        {
            while (cursor?.MoveToNext() == true)
            {
                messages.Add(new SmsMessage
                {
                    Id = cursor.GetLong(0),
                    ThreadId = threadId,
                    Address = cursor.GetString(1) ?? string.Empty,
                    Body = cursor.GetString(2) ?? string.Empty,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(3)),
                    IsOutgoing = false,
                    Status = SmsMessageStatus.Delivered
                });
            }
        }

        foreach (var mms in MmsReader.QueryAll(context, threadId).Where(m => !m.IsOutgoing && !m.IsRead).Take(ConversationNotification.MaxLines))
        {
            var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: false);
            messages.Add(new SmsMessage
            {
                Id = mms.Id,
                ThreadId = threadId,
                Address = MmsReader.GetAddress(context, mms.Id, false),
                Body = body,
                Timestamp = mms.Date,
                IsOutgoing = false,
                Status = SmsMessageStatus.Delivered,
                Attachments = attachments,
                IsMms = true
            });
        }
        return messages;
    }

    private static void Post(Context context, long threadId, string address, string title, string body, bool withSound,
        NotificationCompat.Style style, string? shortcutId)
    {
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
            .SetContentTitle(title)
            .SetContentText(body)
            .SetStyle(style)
            .SetCategory(NotificationCompat.CategoryMessage)
            .SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail)
            .SetAutoCancel(true)
            .SetContentIntent(contentIntent);
        if (shortcutId is not null)
        {
            builder.SetShortcutId(shortcutId).SetLocusId(new AndroidX.Core.Content.LocusIdCompat(shortcutId));
        }

        if (threadId != 0)
        {
            // RemoteInput fills the typed text into the intent, which only works on a mutable PendingIntent.
            var replyInput = new AndroidX.Core.App.RemoteInput.Builder(NotificationActionReceiver.ReplyTextKey).SetLabel("Reply").Build();
            var replyIntent = NotificationActionReceiver.CreateIntent(context, NotificationActionReceiver.ReplyAction, threadId, address, notificationId);
            var replyPending = PendingIntent.GetBroadcast(context, notificationId * 2, replyIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
            // The semantic actions are what Android Auto looks for to read aloud, reply by voice and mark read.
            var replyAction = new NotificationCompat.Action.Builder(0, "Reply", replyPending)
                .AddRemoteInput(replyInput)
                .SetAllowGeneratedReplies(true)
                .SetSemanticAction(NotificationCompat.Action.SemanticActionReply)
                .SetShowsUserInterface(false)
                .Build();

            var readIntent = NotificationActionReceiver.CreateIntent(context, NotificationActionReceiver.MarkReadAction, threadId, address, notificationId);
            var readPending = PendingIntent.GetBroadcast(context, notificationId * 2 + 1, readIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            var readAction = new NotificationCompat.Action.Builder(0, "Mark as read", readPending)
                .SetSemanticAction(NotificationCompat.Action.SemanticActionMarkAsRead)
                .SetShowsUserInterface(false)
                .Build();
            builder.AddAction(replyAction).AddAction(readAction);
        }

        var notification = builder.Build();

        NotificationManagerCompat.From(context).Notify(notificationId, notification);
    }

    public void NotifyReminder(string chatName, ForgeLinkSms.Core.Models.MessageReminder reminder)
    {
        var context = AndroidApp.Context;
        EnsureChannels(context);

        // Kept apart from the per-conversation ids so a reminder never replaces that chat's message notification.
        var notificationId = ReminderNotificationIdBase + reminder.Id;
        var route = $"/conversations/thread?id={reminder.ThreadId}&address={Uri.EscapeDataString(reminder.Address)}"
            + $"&jumpId={reminder.MessageId}&jumpTicks={reminder.MessageTimestamp.UtcTicks}";

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        launchIntent.PutExtra("initial_route", route);
        var contentIntent = PendingIntent.GetActivity(context, notificationId, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var body = $"{chatName}: {reminder.Preview}";
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle("⏰ Reminder")
            .SetContentText(body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))
            .SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail)
            .SetAutoCancel(true)
            .SetContentIntent(contentIntent)
            .Build();

        NotificationManagerCompat.From(context).Notify(notificationId, notification);
    }

    private const int ReminderNotificationIdBase = 1_600_000_000;
    private const int SendFailedNotificationIdBase = 1_700_000_000;

    public void NotifySendFailed(long threadId, string address, string recipientName)
    {
        var context = AndroidApp.Context;
        EnsureChannels(context);

        // One per chat, apart from its message notification, so a new failure replaces the last.
        var notificationId = SendFailedNotificationIdBase + (int)(threadId % 100_000_000);
        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        launchIntent.PutExtra("initial_route", $"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
        var contentIntent = PendingIntent.GetActivity(context, notificationId, launchIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle($"Message to {recipientName} not sent")
            .SetContentText("Tap to open the chat and try again.")
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysWarning)
            .SetCategory(NotificationCompat.CategoryError)
            .SetAutoCancel(true)
            .SetContentIntent(contentIntent)
            .Build();

        NotificationManagerCompat.From(context).Notify(notificationId, notification);
    }

    public void NotifyConversation(long threadId, string address, string fallbackTitle, string fallbackBody, bool withSound)
    {
        if (threadId == 0)
        {
            NotifyIncomingMessage(fallbackTitle, fallbackBody, threadId, address, withSound);
            return;
        }
        try
        {
            var context = AndroidApp.Context;
            var services = MauiApplication.Current.Services;
            var participants = services.GetRequiredService<IThreadService>().GetParticipantsAsync(threadId).GetAwaiter().GetResult();

            var contacts = services.GetRequiredService<IContactService>();
            var people = new Dictionary<string, ContactInfo?>();
            ContactInfo? Contact(string number)
            {
                var key = PhoneNumberFormatter.ToComparableDigits(number);
                if (!people.TryGetValue(key, out var info))
                {
                    info = contacts.LookupAsync(number).GetAwaiter().GetResult();
                    people[key] = info;
                }
                return info;
            }
            string NameFor(string number) => Contact(number)?.DisplayName ?? PhoneNumberFormatter.ToDisplayFormat(number);

            var isGroup = participants.Count > 1;
            var title = isGroup ? GroupNames.Format(participants.Select(NameFor).ToList()) : NameFor(address);
            var fallback = new NotificationLine(ConversationNotification.SenderKey(address), NameFor(address), fallbackBody, DateTimeOffset.UtcNow);
            var model = ConversationNotification.Build(UnreadIncoming(context, threadId), title, isGroup, NameFor, fallback);

            var accent = WidgetUpdater.Accent(services);
            var persons = new Dictionary<string, Person>();
            Person PersonFor(NotificationLine line)
            {
                if (!persons.TryGetValue(line.SenderKey, out var person))
                {
                    var contact = Contact(line.SenderKey);
                    var icon = WidgetBitmaps.Circle(context, contact?.PhotoUri, ConversationNotification.InitialsFor(line.SenderName), accent);
                    person = new Person.Builder().SetKey(line.SenderKey).SetName(line.SenderName)
                        .SetIcon(AndroidX.Core.Graphics.Drawable.IconCompat.CreateWithBitmap(icon))
                        .Build();
                    persons[line.SenderKey] = person;
                }
                return person;
            }

            var me = new Person.Builder().SetName("You").Build();
            var style = new NotificationCompat.MessagingStyle(me);
            if (model.IsGroup)
            {
                style.SetConversationTitle(model.Title);
                style.SetGroupConversation(true);
            }
            foreach (var line in model.Lines)
            {
                style.AddMessage(new NotificationCompat.MessagingStyle.Message(new Java.Lang.String(line.Text), line.Time.ToUnixTimeMilliseconds(), PersonFor(line)));
            }

            var shortcutIcon = WidgetBitmaps.Circle(context, isGroup ? null : Contact(address)?.PhotoUri, ConversationNotification.InitialsFor(model.Title), accent);
            var shortcutId = ConversationShortcuts.Push(context, threadId, address, model.Title, persons.Values.ToList(), shortcutIcon);

            var last = model.Lines[^1];
            Post(context, threadId, address, model.IsGroup ? model.Title : last.SenderName, last.Text, withSound, style, shortcutId);
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Conversation notification for {threadId} failed, using the plain one: {e}");
            NotifyIncomingMessage(fallbackTitle, fallbackBody, threadId, address, withSound);
        }
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
