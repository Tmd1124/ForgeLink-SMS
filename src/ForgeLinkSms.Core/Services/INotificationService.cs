using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface INotificationService
{
    void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address, bool withSound = true);

    /// The conversation form (recent unread messages, people, Priority, Android Auto); falls back to
    /// NotifyIncomingMessage with the given title and body if it can't be built.
    void NotifyConversation(long threadId, string address, string fallbackTitle, string fallbackBody, bool withSound);

    /// Tapping it opens the chat scrolled to the reminded message.
    void NotifyReminder(string chatName, MessageReminder reminder);

    /// A text didn't leave the phone; tapping it opens the chat, where it can be retried.
    void NotifySendFailed(long threadId, string address, string recipientName);
}
