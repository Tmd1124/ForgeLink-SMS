namespace ForgeLinkSms.Core.Services;

public interface INotificationService
{
    void NotifyIncomingMessage(string fromDisplayName, string body, long threadId, string address, bool withSound = true);
}
