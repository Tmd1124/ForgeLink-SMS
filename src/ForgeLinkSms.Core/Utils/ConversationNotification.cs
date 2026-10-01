using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public sealed record NotificationLine(string SenderKey, string SenderName, string Text, DateTimeOffset Time);

public sealed record ConversationNotificationModel(string Title, bool IsGroup, IReadOnlyList<NotificationLine> Lines);

// What a conversation-style notification shows: the chat's latest unread messages from other people.
public static class ConversationNotification
{
    public const int MaxLines = 5;

    // Takes the chat's unread incoming messages themselves (not a count over recent history), so a
    // message the person already read can never be shown as new.
    public static ConversationNotificationModel Build(IReadOnlyList<SmsMessage> unreadIncoming, string title,
        bool isGroup, Func<string, string> nameFor, NotificationLine fallback)
    {
        var lines = unreadIncoming
            .Where(m => !m.IsOutgoing)
            .OrderBy(m => m.Timestamp)
            .TakeLast(MaxLines)
            .Select(m => new NotificationLine(SenderKey(m.Address), nameFor(m.Address), TextOf(m), m.Timestamp))
            .ToList();
        return new ConversationNotificationModel(title, isGroup, lines.Count > 0 ? lines : new[] { fallback });
    }

    // Short-code and named senders ("CarelonRx") have no digits, so they key on the name itself.
    public static string SenderKey(string address)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(address);
        return digits.Length > 0 ? digits : address;
    }

    private static string TextOf(SmsMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Body))
        {
            return ReactionParser.Describe(message.Body) ?? message.Body;
        }
        return message.Attachments.FirstOrDefault()?.Kind switch
        {
            AttachmentKind.Image or AttachmentKind.Gif => "📷 Photo",
            AttachmentKind.Video => "🎥 Video",
            AttachmentKind.Audio => "🎤 Voice message",
            null => string.Empty,
            _ => "📎 Attachment"
        };
    }
}
