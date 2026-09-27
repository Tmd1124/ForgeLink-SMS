using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class DisplayStyleService : IDisplayStyleService
{
    private const string StyleKey = "conversation_display_style";

    public ConversationDisplayStyle GetDisplayStyle()
    {
        var stored = Preferences.Get(StyleKey, nameof(ConversationDisplayStyle.BubblesAndCards));
        return Enum.TryParse<ConversationDisplayStyle>(stored, out var style) ? style : ConversationDisplayStyle.BubblesAndCards;
    }

    public void SetDisplayStyle(ConversationDisplayStyle style) => Preferences.Set(StyleKey, style.ToString());
}
