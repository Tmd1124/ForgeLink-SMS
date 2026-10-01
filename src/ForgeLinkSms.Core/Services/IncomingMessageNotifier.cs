namespace ForgeLinkSms.Core.Services;

public class IncomingMessageNotifier : IIncomingMessageNotifier
{
    public event Action<long>? MessageReceived;

    public void NotifyMessageReceived(long threadId) => MessageReceived?.Invoke(threadId);

    public event Action<long>? MessageStatusChanged;

    public void NotifyMessageStatusChanged(long threadId) => MessageStatusChanged?.Invoke(threadId);
}
