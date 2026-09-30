using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface INotificationSettingsStore
{
    NotificationSettings Get();
    void Save(NotificationSettings settings);
}
