using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

// What TalkBack reads for a chat row, bubble or message: the facts a sighted person gets from
// colors, badges and icons, as one sentence.
public static class AccessibleLabels
{
    public static string ForThread(SmsThread thread, DateTime todayLocal)
    {
        var states = new List<string> { thread.DisplayNameOrAddress };
        if (thread.UnreadCount > 0)
        {
            states.Add($"{thread.UnreadCount} unread");
        }
        if (thread.IsPinned)
        {
            states.Add("pinned");
        }
        if (thread.IsFavorite)
        {
            states.Add("favorite");
        }
        if (thread.IsMuted)
        {
            states.Add("muted");
        }

        var content = string.IsNullOrWhiteSpace(thread.DraftText)
            ? $"Last message: {thread.LastMessageBody}"
            : $"Draft: {thread.DraftText}";
        var local = thread.LastMessageTimestamp.LocalDateTime;
        var when = local.Date == todayLocal.Date ? local.ToString("h:mm tt") : local.ToString("MMM d");
        return $"{string.Join(", ", states)}. {content}. {when}";
    }

    public static string ForBubble(SmsThread thread) =>
        thread.UnreadCount > 0 ? $"{thread.DisplayNameOrAddress}, {thread.UnreadCount} unread" : thread.DisplayNameOrAddress;

    // Read just before a message's own text, links and attachments, which TalkBack reads as they are.
    public static string MessageLeadIn(SmsMessage message, string senderName, string? reminderText = null)
    {
        var who = message.IsOutgoing ? "You" : senderName;
        var label = $"{who}, {message.Timestamp.LocalDateTime:h:mm tt}";
        var attachments = AttachmentCounts(message.Attachments).ToList();
        if (attachments.Count > 0)
        {
            label += $". {string.Join(", ", attachments)}";
        }
        var status = message.StatusDisplay.TrimStart('✓', '⚠', ' ');
        if (status.Length > 0)
        {
            label += $". {status}";
        }
        if (message.Reactions.Count > 0)
        {
            label += $". Reactions: {string.Join(" ", message.Reactions)}";
        }
        if (!string.IsNullOrEmpty(reminderText))
        {
            label += $". Reminder {reminderText}";
        }
        return label;
    }

    private static IEnumerable<string> AttachmentCounts(IReadOnlyList<MessageAttachment> attachments)
    {
        foreach (var group in attachments.GroupBy(a => a.Kind switch
                 {
                     AttachmentKind.Image or AttachmentKind.Gif => "photo",
                     AttachmentKind.Video => "video",
                     AttachmentKind.Audio => "voice message",
                     AttachmentKind.Contact => "contact",
                     _ => "file"
                 }))
        {
            var count = group.Count();
            yield return $"{count} {group.Key}{(count == 1 ? "" : "s")}";
        }
    }
}
