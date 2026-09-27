namespace ForgeLinkSms.Core.Data;

public interface IForwardedMessageRepository
{
    Task InitializeAsync();

    Task RecordAsync(long threadId, string messageKey, string forwardedTo);

    /// Message key → who it was last forwarded to, for one conversation.
    Task<IReadOnlyDictionary<string, string>> GetForThreadAsync(long threadId);
}
