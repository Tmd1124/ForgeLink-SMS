using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface ISmsService
{
    /// Returns up to pageSize messages for the thread, newest first, optionally only those
    /// strictly before beforeTimestamp (for fetching the next page of older history).
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, DateTimeOffset? beforeTimestamp, int pageSize);

    /// Returns up to pageSize messages for the thread, oldest first, strictly after
    /// afterTimestamp — the downward counterpart to GetMessagesAsync, used to catch a trimmed
    /// loaded window back up to the thread's true latest message.
    Task<IReadOnlyList<SmsMessage>> GetNewerMessagesAsync(long threadId, DateTimeOffset afterTimestamp, int pageSize);

    /// Messages in the thread whose text contains query (case-insensitive), newest first.
    Task<IReadOnlyList<SmsMessage>> SearchMessagesAsync(long threadId, string query, int limit);

    /// Messages in any conversation whose text contains query (case-insensitive), newest first.
    Task<IReadOnlyList<SmsMessage>> SearchAllMessagesAsync(string query, int limit);

    /// Every photo and video in the conversation (metadata only; images load separately).
    Task<IReadOnlyList<SharedMedia>> GetSharedMediaAsync(long threadId);

    Task DeleteMessageAsync(SmsMessage message);

    Task SendAsync(string address, string body);

    /// Sends a failed text again, to the same people with the same text and attachment, and removes
    /// the failed copy.
    Task ResendAsync(SmsMessage failed);

    Task SendMmsAsync(long threadId, string address, string? body, PickedAttachment attachment);

    /// Sends one MMS to everyone in a group conversation (text, attachment, or both). A threadId
    /// of 0 means "find or create the group's conversation" — used when starting a new group.
    Task SendGroupAsync(long threadId, IReadOnlyList<string> addresses, string? body, PickedAttachment? attachment);
}
