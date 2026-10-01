using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Components.Help;

// Whether the help panel is open, and which chat-list tab it should explain.
public sealed class HelpState
{
    public bool IsOpen { get; private set; }

    /// Set by the chat list so its help follows the Chats / Updates / Screener tab.
    public ConversationLane Lane { get; set; } = ConversationLane.Conversations;

    public event Action? Changed;

    public void Open()
    {
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }
}
