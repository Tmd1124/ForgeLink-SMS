namespace ForgeLinkSms.Core.Services;

/// Lets the open chat ask the chat list beside it to reload (after a send, or once it's marked read).
public class ConversationListRefresher
{
    public event Action? RefreshRequested;

    public void RequestRefresh() => RefreshRequested?.Invoke();
}
