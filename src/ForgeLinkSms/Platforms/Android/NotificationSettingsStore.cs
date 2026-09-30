using System.Text.Json;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class NotificationSettingsStore : INotificationSettingsStore
{
    private const string SettingsKey = "notification_settings";

    public NotificationSettings Get()
    {
        var json = Preferences.Get(SettingsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<NotificationSettings>(json) ?? new NotificationSettings();
            }
            catch (JsonException)
            {
            }
        }
        return new NotificationSettings();
    }

    public void Save(NotificationSettings settings) => Preferences.Set(SettingsKey, JsonSerializer.Serialize(settings));
}
