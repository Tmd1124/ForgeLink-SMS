using ForgeLinkSms.Core.Services;
using PickedAttachment = ForgeLinkSms.Core.Models.PickedAttachment;
using SmsMessage = ForgeLinkSms.Core.Models.SmsMessage;
using SmsMessageStatus = ForgeLinkSms.Core.Models.SmsMessageStatus;
using SharedMedia = ForgeLinkSms.Core.Models.SharedMedia;
using AttachmentKindClassifier = ForgeLinkSms.Core.Utils.AttachmentKindClassifier;
using SmsStatus = ForgeLinkSms.Core.Utils.SmsStatus;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidSmsManager = global::Android.Telephony.SmsManager;
using AndroidPendingIntent = global::Android.App.PendingIntent;
using AndroidPendingIntentFlags = global::Android.App.PendingIntentFlags;
using AndroidIntent = global::Android.Content.Intent;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace ForgeLinkSms.Platforms.Android;

public class SmsService : ISmsService
{
    public const string SentAction = "ForgeLinkSms.SMS_SENT";
    public const string DeliveredAction = "ForgeLinkSms.SMS_DELIVERED";

    // Total raw (pre-base64) attachment bytes allowed to inline across one page's worth of MMS
    // messages. 20MB raw becomes well under 30MB of base64 text plus JSON structure — safely
    // under the WebView's render-batch limit (observed failing around ~160MB) even accounting
    // for the rest of that page's non-attachment content.
    private const long PageAttachmentByteBudget = 20 * 1024 * 1024;

    // The body below is synchronous, blocking ContentResolver work — reading every MMS part in
    // a thread (and base64-encoding each image/GIF attachment) can take seconds for a media-heavy
    // conversation. Blazor Hybrid page lifecycle callbacks run on the UI thread by default, so
    // without Task.Run this call freezes touch input for as long as it takes, which is exactly
    // the kind of stall that trips Android's ANR watchdog (matches ThreadService's contact-lookup
    // backgrounding, done for the same reason).
    public Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, DateTimeOffset? beforeTimestamp, int pageSize) =>
        Task.Run(() => GetMessagesCore(threadId, beforeTimestamp, pageSize, ascending: false));

    // The downward counterpart used to catch a trimmed loaded window back up to the thread's
    // true latest message: same merge/hydrate logic as GetMessagesAsync, just walking forward
    // in time from a cursor instead of backward.
    public Task<IReadOnlyList<SmsMessage>> GetNewerMessagesAsync(long threadId, DateTimeOffset afterTimestamp, int pageSize) =>
        Task.Run(() => GetMessagesCore(threadId, afterTimestamp, pageSize, ascending: true));

    public Task<IReadOnlyList<SharedMedia>> GetSharedMediaAsync(long threadId) => Task.Run<IReadOnlyList<SharedMedia>>(() =>
    {
        var context = AndroidApp.Context;
        var mmsInThread = MmsReader.QueryAll(context, threadId).ToDictionary(m => m.Id);
        var media = new List<SharedMedia>();
        using var parts = context.ContentResolver!.Query(
            global::Android.Net.Uri.Parse("content://mms/part")!,
            new[] { "_id", "mid", "ct", "name", "cl" },
            "ct LIKE 'image/%' OR ct LIKE 'video/%'",
            null,
            null);
        while (parts is not null && parts.MoveToNext())
        {
            if (!mmsInThread.TryGetValue(parts.GetLong(1), out var mms))
            {
                continue;
            }
            var partId = parts.GetLong(0);
            var fileName = parts.GetString(3) ?? parts.GetString(4) ?? $"attachment-{partId}";
            media.Add(new SharedMedia(partId, AttachmentKindClassifier.FromContentType(parts.GetString(2)), fileName, mms.Date, mms.IsOutgoing));
        }
        return media;
    });

    public Task DeleteMessageAsync(SmsMessage message) => Task.Run(() =>
    {
        var uri = message.IsMms ? $"content://mms/{message.Id}" : $"content://sms/{message.Id}";
        AndroidApp.Context.ContentResolver!.Delete(global::Android.Net.Uri.Parse(uri)!, null, null);
    });

    public Task<IReadOnlyList<SmsMessage>> SearchMessagesAsync(long threadId, string query, int limit) =>
        Task.Run(() => SearchCore(threadId, query, limit));

    public Task<IReadOnlyList<SmsMessage>> SearchAllMessagesAsync(string query, int limit) =>
        Task.Run(() => SearchCore(null, query, limit));

    public Task<IReadOnlyList<SmsMessage>> RecentMediaMessagesAsync(int limit) => Task.Run<IReadOnlyList<SmsMessage>>(() =>
    {
        var context = AndroidApp.Context;
        var withMedia = new HashSet<long>();
        using (var parts = context.ContentResolver!.Query(global::Android.Net.Uri.Parse("content://mms/part")!,
                   new[] { "mid" }, "ct LIKE 'image/%' OR ct LIKE 'video/%'", null, null))
        {
            while (parts is not null && parts.MoveToNext())
            {
                withMedia.Add(parts.GetLong(0));
            }
        }

        return MmsReader.QueryAll(context)
            .Where(m => withMedia.Contains(m.Id))
            .OrderByDescending(m => m.Date)
            .Take(limit)
            .Select(mms =>
            {
                var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: false);
                return new SmsMessage
                {
                    Id = mms.Id,
                    ThreadId = mms.ThreadId,
                    Address = MmsReader.GetAddress(context, mms.Id, mms.IsOutgoing),
                    Body = body,
                    Timestamp = mms.Date,
                    IsOutgoing = mms.IsOutgoing,
                    Status = mms.Status,
                    Attachments = attachments,
                    IsMms = true
                };
            })
            .ToList();
    });

    // SQLite's LIKE is already case-insensitive for ASCII; % and _ in the user's text are
    // escaped so they match literally.
    // threadId null searches every conversation.
    private static IReadOnlyList<SmsMessage> SearchCore(long? threadId, string query, int limit)
    {
        var context = AndroidApp.Context;
        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        var inbox = (int)global::Android.Provider.SmsMessageType.Inbox;
        var sent = (int)global::Android.Provider.SmsMessageType.Sent;
        List<SmsMessage> smsMatches;
        using (var cursor = context.ContentResolver!.Query(
            AndroidTelephony.Sms.ContentUri!,
            new[] { "_id", "thread_id", "address", "body", "date", "type", "status" },
            (threadId is null ? "" : "thread_id = ? AND ") + "(type = ? OR type = ?) AND body LIKE ? ESCAPE '\\'",
            (threadId is null ? Array.Empty<string>() : new[] { threadId.Value.ToString() }).Concat(new[] { inbox.ToString(), sent.ToString(), pattern }).ToArray(),
            "date DESC LIMIT " + limit))
        {
            smsMatches = cursor is null ? new List<SmsMessage>() : ReadSmsRows(cursor);
        }

        var mmsInThread = MmsReader.QueryAll(context, threadId).ToDictionary(m => m.Id);
        var matchingMmsIds = new HashSet<long>();
        using (var parts = context.ContentResolver!.Query(
            global::Android.Net.Uri.Parse("content://mms/part")!,
            new[] { "mid" },
            "ct = 'text/plain' AND text LIKE ? ESCAPE '\\'",
            new[] { pattern },
            null))
        {
            while (parts is not null && parts.MoveToNext())
            {
                var mid = parts.GetLong(0);
                if (mmsInThread.ContainsKey(mid))
                {
                    matchingMmsIds.Add(mid);
                }
            }
        }

        var mmsMatches = matchingMmsIds
            .Select(id => mmsInThread[id])
            .OrderByDescending(m => m.Date)
            .Take(limit)
            .Select(mms =>
            {
                var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: false);
                return new SmsMessage
                {
                    Id = mms.Id,
                    ThreadId = mms.ThreadId,
                    Address = MmsReader.GetAddress(context, mms.Id, mms.IsOutgoing),
                    Body = body,
                    Timestamp = mms.Date,
                    IsOutgoing = mms.IsOutgoing,
                    Status = mms.Status,
                    Attachments = attachments,
                    IsMms = true
                };
            });

        return smsMatches.Concat(mmsMatches).OrderByDescending(m => m.Timestamp).Take(limit).ToList();
    }

    private static IReadOnlyList<SmsMessage> GetMessagesCore(long threadId, DateTimeOffset? cursor, int pageSize, bool ascending)
    {
        var context = AndroidApp.Context;

        var smsCandidates = QuerySmsPage(context, threadId, cursor, pageSize, ascending);

        // MMS summaries (thread_id/date/read/box only, no part reads yet) are cheap regardless
        // of how many exist, so it's fine to read the full set and filter/cap here rather than
        // teaching MmsReader about paging too.
        var mmsCandidates = MmsReader.QueryAll(context, threadId)
            .Where(m => cursor is null || (ascending ? m.Date > cursor : m.Date < cursor));

        // Merge both sources by recency and keep only the closest `pageSize` overall (newest,
        // when paging backward; oldest, when catching up forward). This is what bounds how many
        // (expensive) MMS attachments get decoded per page — some conversations run to thousands
        // of MMS messages with multi-megabyte photos/videos, and decoding all of them just to
        // open the thread made the page take seconds, or in the worst case never finish.
        var merged = smsCandidates
            .Select(m => (Timestamp: m.Timestamp, Sms: (SmsMessage?)m, Mms: (MmsReader.MmsSummary?)null))
            .Concat(mmsCandidates.Select(m => (Timestamp: m.Date, Sms: (SmsMessage?)null, Mms: (MmsReader.MmsSummary?)m)));
        var page = (ascending ? merged.OrderBy(x => x.Timestamp) : merged.OrderByDescending(x => x.Timestamp))
            .Take(pageSize)
            .ToList();

        // Reading each MMS's parts (and decoding any image/GIF attachment) is the slow part of
        // opening a thread. Parallel.For fans these out across the thread pool instead of reading
        // them one at a time — this method already runs off the UI thread (see GetMessagesAsync
        // above), so it's safe to block here doing it.
        var mmsToHydrate = page.Where(x => x.Mms is not null).Select(x => x.Mms!).ToList();
        var hydratedMms = new SmsMessage[mmsToHydrate.Count];

        // The per-attachment size cap alone doesn't bound a page's total payload: a run of many
        // photos each just under that cap can still add up to a render batch the WebView's JSON
        // writer refuses to serialize (observed: a single 50-message page pushed one batch past
        // 160MB and crashed the thread view). This budget is shared across the whole page's
        // Parallel.For hydration so the combined inlined bytes for one page stay bounded — once
        // spent, remaining images in the page fall back to the file-chip UI instead of inlining.
        var attachmentBudget = new MmsReader.AttachmentBudget(PageAttachmentByteBudget);
        Parallel.For(0, mmsToHydrate.Count, i =>
        {
            var mms = mmsToHydrate[i];
            var address = MmsReader.GetAddress(context, mms.Id, mms.IsOutgoing);
            var (body, attachments) = MmsReader.GetContent(context, mms.Id, includeAttachmentData: true, attachmentBudget);

            hydratedMms[i] = new SmsMessage
            {
                Id = mms.Id,
                ThreadId = mms.ThreadId,
                Address = address,
                Body = body,
                Timestamp = mms.Date,
                IsOutgoing = mms.IsOutgoing,
                Status = mms.Status,
                Attachments = attachments,
                IsMms = true
            };
        });

        var mmsIndex = 0;
        var results = new List<SmsMessage>(page.Count);
        foreach (var item in page)
        {
            results.Add(item.Sms ?? hydratedMms[mmsIndex++]);
        }

        return results;
    }

    private static List<SmsMessage> QuerySmsPage(global::Android.Content.Context context, long threadId, DateTimeOffset? pagingCursor, int pageSize, bool ascending)
    {
        var results = new List<SmsMessage>();
        var projection = new[] { "_id", "thread_id", "address", "body", "date", "type", "status" };

        // Android also leaves draft/outbox/failed/queued placeholder rows (type 3-6) in this table;
        // one with no date ever set surfaced as a fake "message" pinned at the very start of a
        // thread. Texts still sending or that failed are shown, but only when they have a real date.
        var types = $"(type IN ({SmsStatus.TypeInbox},{SmsStatus.TypeSent}) OR (type IN ({SmsStatus.TypeOutbox},{SmsStatus.TypeFailed},{SmsStatus.TypeQueued}) AND date > 0))";
        var comparisonOp = ascending ? ">" : "<";
        var selection = pagingCursor is null
            ? $"thread_id = ? AND {types}"
            : $"thread_id = ? AND {types} AND date {comparisonOp} ?";
        var args = pagingCursor is null
            ? new[] { threadId.ToString() }
            : new[] { threadId.ToString(), pagingCursor.Value.ToUnixTimeMilliseconds().ToString() };

        // Android's SMS provider (like most SQLite-backed platform ContentProviders) accepts a
        // raw SQL LIMIT appended to sortOrder — there's no separate paging parameter on
        // ContentResolver.Query, so this is the standard way third-party SMS apps page this table.
        var sortOrder = (ascending ? "date ASC LIMIT " : "date DESC LIMIT ") + pageSize;

        using var cursor = context.ContentResolver!.Query(AndroidTelephony.Sms.ContentUri!, projection, selection, args, sortOrder);
        return cursor is null ? results : ReadSmsRows(cursor);
    }

    private static List<SmsMessage> ReadSmsRows(global::Android.Database.ICursor cursor)
    {
        var results = new List<SmsMessage>();
        var idIdx = cursor.GetColumnIndexOrThrow("_id");
        var threadIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var bodyIdx = cursor.GetColumnIndexOrThrow("body");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var typeIdx = cursor.GetColumnIndexOrThrow("type");
        var statusIdx = cursor.GetColumnIndexOrThrow("status");

        while (cursor.MoveToNext())
        {
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(dateIdx));
            var (isOutgoing, status) = SmsStatus.FromProvider(cursor.GetInt(typeIdx),
                cursor.IsNull(statusIdx) ? SmsStatus.StatusNone : cursor.GetInt(statusIdx), DateTimeOffset.UtcNow - timestamp);

            results.Add(new SmsMessage
            {
                Id = cursor.GetLong(idIdx),
                ThreadId = cursor.GetLong(threadIdx),
                Address = cursor.GetString(addressIdx) ?? string.Empty,
                Body = cursor.GetString(bodyIdx) ?? string.Empty,
                Timestamp = timestamp,
                IsOutgoing = isOutgoing,
                Status = status
            });
        }

        return results;
    }

    public Task SendMmsAsync(long threadId, string address, string? body, PickedAttachment attachment) =>
        MmsSender.SendAsync(threadId, new[] { address }, body, attachment.LocalPath, attachment.FileName);

    public Task SendGroupAsync(long threadId, IReadOnlyList<string> addresses, string? body, PickedAttachment? attachment) =>
        MmsSender.SendAsync(threadId, addresses, body, attachment?.LocalPath, attachment?.FileName);

    public Task ResendAsync(SmsMessage failed) => failed.IsMms
        ? MmsSender.ResendAsync(failed)
        : Task.Run(() =>
        {
            AndroidApp.Context.ContentResolver!.Delete(global::Android.Net.Uri.Parse($"content://sms/{failed.Id}")!, null, null);
            return SendAsync(failed.Address, failed.Body);
        });

    public Task SendAsync(string address, string body)
    {
        var context = AndroidApp.Context;
        var smsManager = AndroidSmsManager.Default!;

        // The default SMS app writes its own outgoing messages. The row starts in the outbox and
        // DeliveryStatusReceiver moves it to sent or failed when Android reports back.
        var values = new AndroidContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        values.Put("read", 1);
        values.Put("status", SmsStatus.StatusNone);
        var messageUri = context.ContentResolver!.Insert(AndroidTelephony.Sms.Outbox.ContentUri!, values)!;

        // Addressed to the receiver itself: an implicit broadcast can be dropped by Android's background
        // limits, and one carrying a data URI never matches an action-only intent filter. The message's
        // URI as the data keeps each send's PendingIntents distinct, so a report lands on its own text.
        var requestCode = (int)(long.Parse(messageUri.LastPathSegment!) % int.MaxValue);
        var sentIntent = new AndroidIntent(context, typeof(DeliveryStatusReceiver)).SetAction(SentAction).SetData(messageUri);
        var deliveredIntent = new AndroidIntent(context, typeof(DeliveryStatusReceiver)).SetAction(DeliveredAction).SetData(messageUri);
        var sentPending = AndroidPendingIntent.GetBroadcast(context, requestCode, sentIntent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;
        var deliveredPending = AndroidPendingIntent.GetBroadcast(context, requestCode, deliveredIntent, AndroidPendingIntentFlags.Immutable | AndroidPendingIntentFlags.UpdateCurrent)!;

        try
        {
            var parts = smsManager.DivideMessage(body);
            if (parts.Count > 1)
            {
                var sentIntents = new List<AndroidPendingIntent>();
                var deliveredIntents = new List<AndroidPendingIntent>();
                for (var i = 0; i < parts.Count; i++)
                {
                    sentIntents.Add(sentPending);
                    deliveredIntents.Add(deliveredPending);
                }
                smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, deliveredIntents);
            }
            else
            {
                smsManager.SendTextMessage(address, null, body, sentPending, deliveredPending);
            }
        }
        catch (Exception)
        {
            _ = Task.Run(() => DeliveryStatusReceiver.MarkFailed(context, messageUri));
            throw;
        }

        return Task.CompletedTask;
    }
}
