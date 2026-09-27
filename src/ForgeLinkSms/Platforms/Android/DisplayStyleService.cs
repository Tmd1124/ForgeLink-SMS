using System.Text.Json;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class DisplayStyleService : IDisplayStyleService
{
    private const string SettingsKey = "conversation_display_settings";
    private const string LegacyStyleKey = "conversation_display_style";

    public DisplaySettings GetDisplaySettings()
    {
        var json = Preferences.Get(SettingsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<DisplaySettings>(json) ?? new DisplaySettings();
            }
            catch (JsonException)
            {
            }
        }
        // Carry over the layout chosen before the other display settings existed.
        var settings = new DisplaySettings();
        if (Enum.TryParse<ConversationDisplayStyle>(Preferences.Get(LegacyStyleKey, string.Empty), out var layout))
        {
            settings.Layout = layout;
            settings.BubbleEveryone = layout == ConversationDisplayStyle.Bubbles;
        }
        return settings;
    }

    public void SaveDisplaySettings(DisplaySettings settings) => Preferences.Set(SettingsKey, JsonSerializer.Serialize(settings));
}
