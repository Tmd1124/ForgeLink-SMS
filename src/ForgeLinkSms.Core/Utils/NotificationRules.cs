using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class NotificationRules
{
    public static NotifyGroup GroupFor(ConversationLane lane, bool isFavorite) => lane switch
    {
        ConversationLane.Conversations => isFavorite ? NotifyGroup.Favorites : NotifyGroup.OtherChats,
        ConversationLane.Updates => NotifyGroup.Updates,
        _ => NotifyGroup.Screener
    };

    // Muted conversations are filtered out before this is asked.
    public static NotifyOutcome Decide(NotifyGroup group, string body, NotificationSettings settings, TimeOnly localTime)
    {
        if (!settings.Enabled)
        {
            return NotifyOutcome.None;
        }

        var setting = settings.For(group);
        var outcome = !setting.Notify ? NotifyOutcome.None : setting.Sound ? NotifyOutcome.Sound : NotifyOutcome.Silent;
        if (settings.AlwaysAlertForCodes && OneTimeCodeDetector.Extract(body) is not null)
        {
            outcome = NotifyOutcome.Sound;
        }

        var favoriteRings = group == NotifyGroup.Favorites && settings.FavoritesRingDuringQuietHours;
        if (outcome == NotifyOutcome.Sound && !favoriteRings && IsQuietTime(settings, localTime))
        {
            return NotifyOutcome.Silent;
        }
        return outcome;
    }

    // An overnight range (22:00 → 07:00) wraps past midnight; start == end means no quiet time.
    public static bool IsQuietTime(NotificationSettings settings, TimeOnly localTime)
    {
        if (!settings.QuietHoursEnabled || settings.QuietStart == settings.QuietEnd)
        {
            return false;
        }
        return settings.QuietStart < settings.QuietEnd
            ? localTime >= settings.QuietStart && localTime < settings.QuietEnd
            : localTime >= settings.QuietStart || localTime < settings.QuietEnd;
    }

    public static NotificationSettings Everything(NotificationSettings current) => WithAll(current, new(true, true));

    public static NotificationSettings EverythingSilent(NotificationSettings current) => WithAll(current, new(true, false));

    public static NotificationSettings FavoritesOnly(NotificationSettings current)
    {
        var preset = WithAll(current, new(false, false));
        preset.Favorites = new(true, true);
        return preset;
    }

    private static NotificationSettings WithAll(NotificationSettings current, GroupNotifySetting setting)
    {
        var preset = current.Clone();
        foreach (var group in Enum.GetValues<NotifyGroup>())
        {
            preset.Set(group, setting);
        }
        return preset;
    }
}
