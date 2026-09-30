using Android.Content;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Reads the SMS/MMS provider directly by raw column name (see MmsReader for why). Only the inbox
// and sent boxes are backed up — drafts, outbox and failed sends aren't real history.
internal sealed class AndroidBackupSource(Context context, IServiceProvider services) : IBackupSource
{
    private IReadOnlyDictionary<long, IReadOnlyList<string>> _participants = new Dictionary<long, IReadOnlyList<string>>();

    public async Task<AppData> ReadAppDataAsync()
    {
        var (data, participants, _) = await AppDataSnapshot.ReadAsync(services);
        _participants = participants;
        return data;
    }

    public int CountMessages() =>
        Count("content://sms", "type IN (1,2)") + Count("content://mms", "msg_box IN (1,2)");

    public IEnumerable<SourceMessage> ReadMessages() => ReadSms().Concat(ReadMms());

    private IEnumerable<SourceMessage> ReadSms()
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://sms")!,
            new[] { "thread_id", "address", "body", "date", "date_sent", "type", "read", "status" }, "type IN (1,2)", null, "date ASC");
        if (cursor is null)
        {
            yield break;
        }
        while (cursor.MoveToNext())
        {
            var threadId = cursor.GetLong(0);
            var address = cursor.GetString(1) ?? string.Empty;
            var message = new BackupMessage(
                IsMms: false,
                Addresses: ParticipantsOf(threadId, address),
                From: address,
                TimestampMs: cursor.GetLong(3),
                DateSentMs: cursor.GetLong(4),
                Outgoing: cursor.GetInt(5) == 2,
                Read: cursor.GetInt(6) == 1,
                Status: cursor.GetInt(7),
                Body: cursor.GetString(2),
                Subject: null,
                Attachments: Array.Empty<BackupAttachment>());
            yield return new SourceMessage(message, Array.Empty<SourceAttachment>());
        }
    }

    private IEnumerable<SourceMessage> ReadMms()
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms")!,
            new[] { "_id", "thread_id", "date", "date_sent", "msg_box", "read", "sub" }, "msg_box IN (1,2)", null, "date ASC");
        if (cursor is null)
        {
            yield break;
        }
        while (cursor.MoveToNext())
        {
            var id = cursor.GetLong(0);
            var threadId = cursor.GetLong(1);
            var outgoing = cursor.GetInt(4) == 2;
            var (from, recipients) = ReadMmsAddresses(id);
            var (body, attachments) = ReadMmsParts(id);
            var message = new BackupMessage(
                IsMms: true,
                Addresses: ParticipantsOf(threadId, from ?? recipients.FirstOrDefault() ?? string.Empty, recipients),
                From: outgoing ? null : from,
                TimestampMs: cursor.GetLong(2) * 1000,
                DateSentMs: cursor.GetLong(3) * 1000,
                Outgoing: outgoing,
                Read: cursor.GetInt(5) == 1,
                Status: 0,
                Body: body,
                Subject: cursor.GetString(6),
                Attachments: Array.Empty<BackupAttachment>());
            yield return new SourceMessage(message, attachments);
        }
    }

    private IReadOnlyList<string> ParticipantsOf(long threadId, string fallback, IReadOnlyList<string>? more = null) =>
        _participants.TryGetValue(threadId, out var p) && p.Count > 0
            ? p
            : (more is { Count: > 0 } ? more.Append(fallback).Where(a => a.Length > 0).Distinct().ToList() : new[] { fallback });

    private (string? From, IReadOnlyList<string> Recipients) ReadMmsAddresses(long mmsId)
    {
        const int PduFrom = 137;
        string? from = null;
        var recipients = new List<string>();
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse($"content://mms/{mmsId}/addr")!, new[] { "address", "type" }, null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var address = cursor.GetString(0);
            if (string.IsNullOrEmpty(address) || address == "insert-address-token")
            {
                continue;
            }
            if (cursor.GetInt(1) == PduFrom)
            {
                from = address;
            }
            else
            {
                recipients.Add(address);
            }
        }
        return (from, recipients);
    }

    private (string? Body, IReadOnlyList<SourceAttachment> Attachments) ReadMmsParts(long mmsId)
    {
        var text = new List<string>();
        var attachments = new List<SourceAttachment>();
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms/part")!,
            new[] { "_id", "ct", "text", "name", "cl" }, "mid = ?", new[] { mmsId.ToString() }, "_id ASC");
        while (cursor is not null && cursor.MoveToNext())
        {
            var partId = cursor.GetLong(0);
            var contentType = cursor.GetString(1) ?? "application/octet-stream";
            if (contentType == "application/smil")
            {
                continue;
            }
            if (contentType == "text/plain")
            {
                var partText = cursor.GetString(2);
                if (!string.IsNullOrEmpty(partText))
                {
                    text.Add(partText);
                }
                continue;
            }
            var name = cursor.GetString(3) ?? cursor.GetString(4);
            var partUri = AndroidUri.Parse($"content://mms/part/{partId}")!;
            attachments.Add(new SourceAttachment(contentType, name,
                () => context.ContentResolver!.OpenInputStream(partUri) ?? throw new IOException($"Could not read attachment {partId}.")));
        }
        return (text.Count == 0 ? null : string.Join("\n", text), attachments);
    }

    private int Count(string uri, string selection)
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse(uri)!, new[] { "_id" }, selection, null, null);
        return cursor?.Count ?? 0;
    }
}
