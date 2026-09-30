namespace ForgeLinkSms.Core.Models;

public enum NotifyGroup
{
    Favorites,
    OtherChats,
    Updates,
    Screener
}

public enum NotifyOutcome
{
    None,
    Silent,
    Sound
}

public readonly record struct GroupNotifySetting(bool Notify, bool Sound);

public class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    public GroupNotifySetting Favorites { get; set; } = new(true, true);
    public GroupNotifySetting OtherChats { get; set; } = new(true, true);
    public GroupNotifySetting Updates { get; set; } = new(false, false);
    public GroupNotifySetting Screener { get; set; } = new(false, false);

    public bool AlwaysAlertForCodes { get; set; } = true;

    public bool QuietHoursEnabled { get; set; }
    public TimeOnly QuietStart { get; set; } = new(22, 0);
    public TimeOnly QuietEnd { get; set; } = new(7, 0);
    public bool FavoritesRingDuringQuietHours { get; set; }

    public GroupNotifySetting For(NotifyGroup group) => group switch
    {
        NotifyGroup.Favorites => Favorites,
        NotifyGroup.OtherChats => OtherChats,
        NotifyGroup.Updates => Updates,
        _ => Screener
    };

    public void Set(NotifyGroup group, GroupNotifySetting setting)
    {
        switch (group)
        {
            case NotifyGroup.Favorites: Favorites = setting; break;
            case NotifyGroup.OtherChats: OtherChats = setting; break;
            case NotifyGroup.Updates: Updates = setting; break;
            default: Screener = setting; break;
        }
    }

    public NotificationSettings Clone() => (NotificationSettings)MemberwiseClone();
}
