using Android.App;
using Android.Content;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Android reports each sent text twice: once when it leaves the phone (or fails to), and later
// when the carrier confirms delivery. Picture messages only get the first. Each report arrives
// with the message's own provider URI as the data.
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { SmsService.SentAction })]
[IntentFilter(new[] { SmsService.DeliveredAction })]
public class DeliveryStatusReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Data is not { } messageUri)
        {
            return;
        }

        // Read here: ResultCode belongs to this broadcast and isn't available once it's handed off.
        var sentOk = ResultCode == Result.Ok;
        var action = intent.Action;
        var pdu = intent.GetByteArrayExtra("pdu");
        var format = intent.GetStringExtra("format");
        var pduFile = intent.GetStringExtra(MmsSender.PduFileExtra);
        var pendingResult = GoAsync();
        Task.Run(() =>
        {
            try
            {
                Handle(context, messageUri, action, sentOk, pdu, format);
                if (pduFile is not null)
                {
                    File.Delete(pduFile);
                }
            }
            catch (Exception e)
            {
                global::Android.Util.Log.Warn("ForgeLinkSms", $"Updating send status for {messageUri} failed: {e.Message}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }

    private static void Handle(Context context, AndroidUri messageUri, string? action, bool sentOk, byte[]? pdu, string? format)
    {
        if (action is SmsService.SentAction or MmsSender.SentAction)
        {
            if (sentOk)
            {
                // Long texts report once per part; a part that already failed keeps the text failed.
                var (column, outbox, sent, _) = Columns(messageUri);
                Update(context, messageUri, column, sent, $"{column} = {outbox}");
                StatusChanged(context, messageUri);
            }
            else
            {
                MarkFailed(context, messageUri);
            }
        }
        else if (action == SmsService.DeliveredAction && pdu is not null)
        {
            var report = global::Android.Telephony.SmsMessage.CreateFromPdu(pdu, format);
            if (report is not null)
            {
                Update(context, messageUri, "status", SmsStatus.FromDeliveryReport(report.Status), null);
                StatusChanged(context, messageUri);
            }
        }
    }

    public static void MarkFailed(Context context, AndroidUri messageUri)
    {
        // Only the first failing part notifies, so a long text doesn't raise several notifications.
        var (column, _, _, failed) = Columns(messageUri);
        if (Update(context, messageUri, column, failed, $"{column} != {failed}") == 0)
        {
            return;
        }

        var threadId = StatusChanged(context, messageUri);
        if (threadId == 0)
        {
            return;
        }
        var services = MauiApplication.Current.Services;
        var participants = services.GetRequiredService<IThreadService>().GetParticipantsAsync(threadId).GetAwaiter().GetResult();
        if (participants.Count == 0)
        {
            return;
        }
        var contacts = services.GetRequiredService<IContactService>();
        var names = participants
            .Select(p => contacts.LookupAsync(p).GetAwaiter().GetResult()?.DisplayName ?? PhoneNumberFormatter.ToDisplayFormat(p))
            .ToList();
        services.GetRequiredService<INotificationService>().NotifySendFailed(threadId, participants[0], GroupNames.Format(names));
    }

    // Texts track progress in "type", picture messages in "msg_box".
    private static (string Column, int Outbox, int Sent, int Failed) Columns(AndroidUri messageUri) =>
        messageUri.Authority == "mms"
            ? ("msg_box", SmsStatus.BoxOutbox, SmsStatus.BoxSent, SmsStatus.BoxFailed)
            : ("type", SmsStatus.TypeOutbox, SmsStatus.TypeSent, SmsStatus.TypeFailed);

    private static int Update(Context context, AndroidUri messageUri, string column, int value, string? where)
    {
        using var values = new ContentValues();
        values.Put(column, value);
        return context.ContentResolver!.Update(messageUri, values, where, null);
    }

    private static long StatusChanged(Context context, AndroidUri messageUri)
    {
        using var cursor = context.ContentResolver!.Query(messageUri, new[] { "thread_id" }, null, null, null);
        if (cursor?.MoveToFirst() != true)
        {
            return 0;
        }
        var threadId = cursor.GetLong(0);
        MauiApplication.Current.Services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageStatusChanged(threadId);
        return threadId;
    }
}
