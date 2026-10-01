using AndroidApp = global::Android.App.Application;
using AndroidContentValues = global::Android.Content.ContentValues;
using AndroidUri = global::Android.Net.Uri;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidContext = global::Android.Content.Context;
using AndroidMimeTypeMap = global::Android.Webkit.MimeTypeMap;
using AndroidIntent = global::Android.Content.Intent;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using SmsMessage = ForgeLinkSms.Core.Models.SmsMessage;
using SmsStatus = ForgeLinkSms.Core.Utils.SmsStatus;

namespace ForgeLinkSms.Platforms.Android;

// Orchestrates an outgoing MMS: encode the PDU (MmsPduBuilder), hand it to the OS for carrier
// transport, and separately write our own copy into content://mms so it shows up in the thread
// immediately — Android does not do this automatically even for the default SMS app, exactly
// like plain SMS sending in SmsService.SendAsync above.
internal static class MmsSender
{
    public const string SentAction = "ForgeLinkSms.MMS_SENT";
    public const string PduFileExtra = "pdu_file";

    // Rebuilds the failed message from what the message store kept of it, so this works even after
    // the app was closed, then sends it as new and drops the failed copy.
    public static Task ResendAsync(SmsMessage failed) => Task.Run(() =>
    {
        var context = AndroidApp.Context;
        var recipients = MmsReader.GetRecipients(context, failed.Id);
        if (recipients.Count == 0)
        {
            return;
        }

        string? attachmentPath = null;
        string? attachmentName = null;
        if (failed.Attachments.FirstOrDefault() is { } attachment)
        {
            attachmentName = attachment.FileName;
            attachmentPath = Path.Combine(context.CacheDir!.AbsolutePath, $"resend_{failed.Id}_{attachment.FileName}");
            using var input = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!)!;
            using var output = File.Create(attachmentPath);
            input.CopyTo(output);
        }

        try
        {
            SendCore(failed.ThreadId, recipients, string.IsNullOrEmpty(failed.Body) ? null : failed.Body, attachmentPath, attachmentName);
            context.ContentResolver!.Delete(AndroidUri.Parse($"content://mms/{failed.Id}")!, null, null);
        }
        finally
        {
            if (attachmentPath is not null)
            {
                File.Delete(attachmentPath);
            }
        }
    });

    public static Task SendAsync(long threadId, IReadOnlyList<string> addresses, string? body, string? attachmentLocalPath, string? attachmentFileName) =>
        Task.Run(() => SendCore(threadId, addresses, body, attachmentLocalPath, attachmentFileName));

    private static void SendCore(long threadId, IReadOnlyList<string> addresses, string? body, string? attachmentLocalPath, string? attachmentFileName)
    {
        var context = AndroidApp.Context;
        MmsPduBuilder.Attachment? attachment = attachmentLocalPath is null
            ? null
            : new MmsPduBuilder.Attachment
            {
                ContentType = GetContentType(attachmentLocalPath),
                FileName = attachmentFileName ?? Path.GetFileName(attachmentLocalPath),
                Data = File.ReadAllBytes(attachmentLocalPath)
            };
        var transactionId = Guid.NewGuid().ToString("N");
        var date = DateTimeOffset.UtcNow;
        if (threadId == 0)
        {
            threadId = global::Android.Provider.Telephony.Threads.GetOrCreateThreadId(context, addresses.ToList());
        }

        var pdu = MmsPduBuilder.BuildSendRequest(addresses, body, attachment, transactionId, date);

        var pduFile = new Java.IO.File(context.CacheDir, $"mms_send_{transactionId}.dat");
        using (var stream = new FileStream(pduFile.AbsolutePath!, FileMode.Create))
        {
            stream.Write(pdu, 0, pdu.Length);
        }

        // SmsManager.SendMultimediaMessage reads the PDU via ContentResolver as the calling
        // (system MMS service) identity, not this app's own — a raw file:// Uri would throw
        // FileUriExposedException on API 24+, so it has to go through a FileProvider content://
        // Uri with read permission granted (grantUriPermissions="true" on the provider, declared
        // in AndroidManifest.xml).
        var authority = context.PackageName + ".fileprovider";
        var pduUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, pduFile);

        // Written to the outbox first so the send's report can move it to sent or failed.
        var messageUri = InsertOutgoingMessage(context, threadId, addresses, body, date, attachment);
        AndroidPendingIntent? sentPending = null;
        if (messageUri is not null)
        {
            // Addressed to the receiver itself, with the message's URI as data, like SMS sends.
            var sentIntent = new AndroidIntent(context, typeof(DeliveryStatusReceiver)).SetAction(SentAction).SetData(messageUri);
            sentIntent.PutExtra(PduFileExtra, pduFile.AbsolutePath);
            var requestCode = (int)(long.Parse(messageUri.LastPathSegment!) % int.MaxValue);
            sentPending = AndroidPendingIntent.GetBroadcast(context, requestCode, sentIntent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent);
        }

        try
        {
            AndroidSmsManager.Default!.SendMultimediaMessage(context, pduUri, null, null, sentPending);
        }
        catch (Exception)
        {
            if (messageUri is not null)
            {
                DeliveryStatusReceiver.MarkFailed(context, messageUri);
            }
            pduFile.Delete();
            throw;
        }
    }

    // Mirrors MmsReader's read-side schema exactly (content://mms, .../addr, .../part with the
    // same raw column names) so a message this app just sent renders identically to one it read
    // back from a real received/sent MMS.
    private static AndroidUri? InsertOutgoingMessage(AndroidContext context, long threadId, IReadOnlyList<string> addresses, string? body,
        DateTimeOffset date, MmsPduBuilder.Attachment? attachment)
    {
        var resolver = context.ContentResolver!;

        var messageValues = new AndroidContentValues();
        messageValues.Put("thread_id", threadId);
        messageValues.Put("date", date.ToUnixTimeSeconds());
        messageValues.Put("msg_box", SmsStatus.BoxOutbox);
        messageValues.Put("read", 1);
        messageValues.Put("m_type", 0x80); // MESSAGE_TYPE_SEND_REQ — matches the wire PDU's own type.
        var messageUri = resolver.Insert(AndroidUri.Parse("content://mms")!, messageValues);
        if (messageUri?.LastPathSegment is not { } idSegment || !long.TryParse(idSegment, out var msgId))
        {
            return null;
        }

        foreach (var address in addresses)
        {
            var addrValues = new AndroidContentValues();
            addrValues.Put("address", address);
            addrValues.Put("type", 151); // MmsReader.AddressTypeTo
            addrValues.Put("charset", 106); // UTF-8
            resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/addr")!, addrValues);
        }

        if (!string.IsNullOrEmpty(body))
        {
            var textValues = new AndroidContentValues();
            textValues.Put("ct", "text/plain");
            textValues.Put("text", body);
            resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/part")!, textValues);
        }

        if (attachment is null)
        {
            return messageUri;
        }
        var partValues = new AndroidContentValues();
        partValues.Put("ct", attachment.ContentType);
        partValues.Put("name", attachment.FileName);
        var partUri = resolver.Insert(AndroidUri.Parse($"content://mms/{msgId}/part")!, partValues);
        if (partUri is not null)
        {
            using var output = resolver.OpenOutputStream(partUri);
            output?.Write(attachment.Data, 0, attachment.Data.Length);
        }
        return messageUri;
    }

    private static string GetContentType(string localPath)
    {
        var extension = Path.GetExtension(localPath).TrimStart('.').ToLowerInvariant();

        // Android's MimeTypeMap doesn't reliably resolve "vcf" across API levels, and
        // AttachmentKindClassifier.FromContentType only recognizes the vCard MIME types below.
        if (extension == "vcf")
        {
            return "text/x-vcard";
        }

        return AndroidMimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream";
    }
}
