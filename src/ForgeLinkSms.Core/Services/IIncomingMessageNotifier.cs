namespace ForgeLinkSms.Core.Services;

public interface IIncomingMessageNotifier
{
    event Action<long>? MessageReceived;
    void NotifyMessageReceived(long threadId);

    /// A sent text's status (sent, delivered, not sent) changed.
    event Action<long>? MessageStatusChanged;
    void NotifyMessageStatusChanged(long threadId);
}
