using System.Threading;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;
using AndroidContext = global::Android.Content.Context;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// MMS lives in a separate, much less documented set of content:// tables than SMS: the
// message row itself ("content://mms"), its sender/recipient rows ("content://mms/{id}/addr"),
// and its text/attachment parts ("content://mms/part", filtered by "mid = <id>"). None of this
// is exposed through the strongly-typed Android.Provider.Telephony.Mms bindings in a way that
// covers addr/part, so every column here is addressed by its raw, stable provider name — the
// same names every long-standing third-party SMS/MMS app on Android relies on.
internal static class MmsReader
{
    private const int MessageBoxInbox = 1;
    private const int MessageBoxSent = 2;
    private const int AddressTypeFrom = 137;
    private const int AddressTypeTo = 151;

    // Caps how large an inline image/GIF data: URI we'll build from an MMS part. Anything
    // bigger gets shown as a plain file chip instead of decoding multi-megabyte bytes into a
    // base64 string that both this process and the WebView would have to hold in memory.
    private const long MaxInlineAttachmentBytes = 5 * 1024 * 1024;

    // A per-attachment cap alone isn't enough: a single page of 50 messages can still contain
    // a cluster of many images each just under that cap, and their combined base64 size is what
    // actually gets serialized into one Blazor render batch and shipped to the WebView. A real
    // thread with a run of camera photos produced a batch over 160MB this way, which the WebView's
    // JSON writer refuses to serialize and crashes the whole page. AttachmentBudget bounds the
    // total raw bytes inlined across one page's worth of attachments; once it's spent, remaining
    // images in that page fall back to the file-chip UI instead of blowing the batch size up
    // further.
    public sealed class AttachmentBudget
    {
        private long _remainingBytes;

        public AttachmentBudget(long totalBytes) => _remainingBytes = totalBytes;

        public bool TryReserve(long bytes)
        {
            while (true)
            {
                var current = Interlocked.Read(ref _remainingBytes);
                if (bytes > current)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref _remainingBytes, current - bytes, current) == current)
                {
                    return true;
                }
            }
        }
    }

    public sealed class MmsSummary
    {
        public required long ThreadId { get; init; }
        public required long Id { get; init; }
        public required DateTimeOffset Date { get; init; }
        public required bool IsOutgoing { get; init; }
        public required bool IsRead { get; init; }
        public required int Box { get; init; }

        public SmsMessageStatus Status => SmsStatus.FromMmsBox(Box, DateTimeOffset.UtcNow - Date).Status;
    }

    public static List<MmsSummary> QueryAll(AndroidContext context, long? threadId = null)
    {
        var results = new List<MmsSummary>();
        var mmsUri = AndroidUri.Parse("content://mms")!;
        var projection = new[] { "_id", "thread_id", "date", "read", "msg_box" };

        // Picture messages still sending or that failed are shown too, but only with a real date,
        // the same guard SmsService uses against placeholder rows.
        var boxes = $"(msg_box IN ({SmsStatus.BoxInbox},{SmsStatus.BoxSent}) OR (msg_box IN ({SmsStatus.BoxOutbox},{SmsStatus.BoxFailed}) AND date > 0))";
        var selection = threadId is null ? boxes : $"{boxes} AND thread_id = ?";
        var args = threadId is null ? null : new[] { threadId.Value.ToString() };

        using var cursor = context.ContentResolver!.Query(mmsUri, projection, selection, args, "date DESC");
        if (cursor is null)
        {
            return results;
        }

        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var threadIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var readIdx = cursor.GetColumnIndexOrThrow("read");
        var boxIdx = cursor.GetColumnIndexOrThrow("msg_box");

        while (cursor.MoveToNext())
        {
            var box = cursor.GetInt(boxIdx);
            results.Add(new MmsSummary
            {
                Id = cursor.GetLong(idIdx),
                ThreadId = cursor.GetLong(threadIdx),
                // Unlike Sms.date (milliseconds), Mms.date is whole seconds since epoch.
                Date = DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(dateIdx)),
                IsRead = cursor.GetInt(readIdx) != 0,
                IsOutgoing = box != MessageBoxInbox,
                Box = box
            });
        }

        return results;
    }

    public static List<string> GetRecipients(AndroidContext context, long mmsId)
    {
        var recipients = new List<string>();
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse($"content://mms/{mmsId}/addr")!,
            new[] { "address" }, "type = ?", new[] { AddressTypeTo.ToString() }, null);
        while (cursor?.MoveToNext() == true)
        {
            if (cursor.GetString(0) is { Length: > 0 } address && address != "insert-address-token")
            {
                recipients.Add(address);
            }
        }
        return recipients;
    }

    public static string GetAddress(AndroidContext context, long mmsId, bool isOutgoing)
    {
        var addrUri = AndroidUri.Parse($"content://mms/{mmsId}/addr")!;
        var projection = new[] { "address", "type" };
        using var cursor = context.ContentResolver!.Query(addrUri, projection, null, null, null);
        if (cursor is null)
        {
            return string.Empty;
        }

        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var typeIdx = cursor.GetColumnIndexOrThrow("type");
        var wantedType = isOutgoing ? AddressTypeTo : AddressTypeFrom;

        string? fallback = null;
        while (cursor.MoveToNext())
        {
            var address = cursor.GetString(addressIdx);
            if (string.IsNullOrEmpty(address) || address == "insert-address-token")
            {
                continue;
            }

            if (cursor.GetInt(typeIdx) == wantedType)
            {
                return address;
            }
            fallback ??= address;
        }

        return fallback ?? string.Empty;
    }

    public static (string Body, List<MessageAttachment> Attachments) GetContent(AndroidContext context, long mmsId, bool includeAttachmentData = true, AttachmentBudget? budget = null)
    {
        var partUri = AndroidUri.Parse("content://mms/part")!;
        var projection = new[] { "_id", "ct", "text", "name", "cl" };
        using var cursor = context.ContentResolver!.Query(partUri, projection, "mid = ?", new[] { mmsId.ToString() }, null);

        var bodyParts = new List<string>();
        var attachments = new List<MessageAttachment>();
        if (cursor is null)
        {
            return (string.Empty, attachments);
        }

        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var ctIdx = cursor.GetColumnIndexOrThrow("ct");
        var textIdx = cursor.GetColumnIndexOrThrow("text");
        var nameIdx = cursor.GetColumnIndexOrThrow("name");
        var clIdx = cursor.GetColumnIndexOrThrow("cl");

        while (cursor.MoveToNext())
        {
            var contentType = cursor.GetString(ctIdx) ?? string.Empty;
            var partId = cursor.GetLong(idIdx);

            if (contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var text = cursor.GetString(textIdx);
                if (string.IsNullOrEmpty(text))
                {
                    text = ReadPartAsText(context, partId);
                }
                if (!string.IsNullOrEmpty(text))
                {
                    bodyParts.Add(text);
                }
                continue;
            }

            if (contentType.Equals("application/smil", StringComparison.OrdinalIgnoreCase))
            {
                continue; // slideshow layout metadata, not user-facing content
            }

            var fileName = cursor.GetString(nameIdx) ?? cursor.GetString(clIdx) ?? $"attachment-{partId}";
            var kind = AttachmentKindClassifier.FromContentType(contentType);
            // Decoding and base64-encoding image/GIF bytes is the expensive part of reading an
            // MMS — skip it entirely when the caller only needs a preview label (kind + file
            // name), e.g. building the conversation list's snippet text for dozens of threads.
            var dataUri = !includeAttachmentData ? null
                : kind is AttachmentKind.Image or AttachmentKind.Gif ? ReadPartAsDataUri(context, partId, contentType, budget)
                : kind == AttachmentKind.Video ? ReadVideoFrameAsDataUri(partId, budget)
                : null;

            attachments.Add(new MessageAttachment
            {
                FileName = fileName,
                Kind = kind,
                DataUri = dataUri,
                PartId = partId
            });
        }

        return (string.Join("\n", bodyParts), attachments);
    }

    private static string? ReadPartAsText(AndroidContext context, long partId)
    {
        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{partId}")!);
            if (stream is null)
            {
                return null;
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // For a video, DataUri holds a still frame for the bubble, not the video itself.
    private static string? ReadVideoFrameAsDataUri(long partId, AttachmentBudget? budget)
    {
        var jpeg = MediaThumbnailService.VideoFrameJpeg(partId, 480);
        if (jpeg is null || (budget is not null && !budget.TryReserve(jpeg.Length)))
        {
            return null;
        }
        return $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}";
    }

    private static string? ReadPartAsDataUri(AndroidContext context, long partId, string contentType, AttachmentBudget? budget)
    {
        try
        {
            using var stream = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{partId}")!);
            if (stream is null)
            {
                return null;
            }
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            if (memoryStream.Length > MaxInlineAttachmentBytes)
            {
                return null;
            }
            if (budget is not null && !budget.TryReserve(memoryStream.Length))
            {
                return null;
            }
            return $"data:{contentType};base64,{Convert.ToBase64String(memoryStream.ToArray())}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
