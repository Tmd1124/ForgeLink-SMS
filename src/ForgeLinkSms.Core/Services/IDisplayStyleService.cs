using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IDisplayStyleService
{
    ConversationDisplayStyle GetDisplayStyle();
    void SetDisplayStyle(ConversationDisplayStyle style);
}
