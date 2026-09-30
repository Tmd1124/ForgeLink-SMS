using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface INotificationService
{
    void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address, bool withSound = true);

    /// Tapping it opens the chat scrolled to the reminded message.
    void NotifyReminder(string chatName, MessageReminder reminder);
}
