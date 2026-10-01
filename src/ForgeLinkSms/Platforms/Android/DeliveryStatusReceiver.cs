using Android.App;
using Android.Content;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Android reports each sent text twice: once when it leaves the phone (or fails to), and later
// when the carrier confirms delivery. Both arrive with the text's own provider URI as the data.
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
        var pendingResult = GoAsync();
        Task.Run(() =>
        {
            try
            {
                Handle(context, messageUri, action, sentOk, pdu, format);
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }

    private static void Handle(Context context, AndroidUri messageUri, string? action, bool sentOk, byte[]? pdu, string? format)
    {
        try
        {
            if (action == SmsService.SentAction)
            {
                if (sentOk)
                {
                    // Long texts report once per part; a part that already failed keeps the text failed.
                    Update(context, messageUri, "type", SmsStatus.TypeSent, $"type = {SmsStatus.TypeOutbox}");
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
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Updating send status for {messageUri} failed: {e.Message}");
        }
    }

    public static void MarkFailed(Context context, AndroidUri messageUri)
    {
        // Only the first failing part notifies, so a long text doesn't raise several notifications.
        if (Update(context, messageUri, "type", SmsStatus.TypeFailed, $"type != {SmsStatus.TypeFailed}") == 0)
        {
            return;
        }

        var (threadId, address) = StatusChanged(context, messageUri);
        if (threadId == 0)
        {
            return;
        }
        var services = MauiApplication.Current.Services;
        var name = services.GetRequiredService<IContactService>().LookupAsync(address).GetAwaiter().GetResult()?.DisplayName
            ?? PhoneNumberFormatter.ToDisplayFormat(address);
        services.GetRequiredService<INotificationService>().NotifySendFailed(threadId, address, name);
    }

    private static int Update(Context context, AndroidUri messageUri, string column, int value, string? where)
    {
        using var values = new ContentValues();
        values.Put(column, value);
        return context.ContentResolver!.Update(messageUri, values, where, null);
    }

    private static (long ThreadId, string Address) StatusChanged(Context context, AndroidUri messageUri)
    {
        using var cursor = context.ContentResolver!.Query(messageUri, new[] { "thread_id", "address" }, null, null, null);
        if (cursor?.MoveToFirst() != true)
        {
            return (0, string.Empty);
        }
        var threadId = cursor.GetLong(0);
        var address = cursor.GetString(1) ?? string.Empty;
        MauiApplication.Current.Services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageStatusChanged(threadId);
        return (threadId, address);
    }
}
