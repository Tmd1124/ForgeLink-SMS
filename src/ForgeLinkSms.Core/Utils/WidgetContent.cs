using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public sealed record WidgetPerson(long ThreadId, string Address, string Name, string Initials, string? PhotoUri, int Unread);

public sealed record WidgetRow(long ThreadId, string Address, string Name, string Text, string When);

public sealed record WidgetSnapshot(int TotalUnread, IReadOnlyList<WidgetPerson> Favorites, IReadOnlyList<WidgetRow> RecentUnread);

// What the home screen widget shows, from the chats the Chats tab shows (already filtered).
public static class WidgetContent
{
    public const int MaxFavorites = 5;
    public const int MaxRecent = 3;

    public static WidgetSnapshot Build(IReadOnlyList<SmsThread> chats, bool showText, DateTime todayLocal)
    {
        var favorites = chats
            .Where(c => c.IsFavorite)
            .OrderByDescending(c => c.UnreadCount)
            .ThenByDescending(c => c.LastMessageTimestamp)
            .Take(MaxFavorites)
            .Select(c => new WidgetPerson(c.Id, c.Address, c.DisplayNameOrAddress, c.Initials, c.PhotoUri, c.UnreadCount))
            .ToList();
        var recent = chats
            .Where(c => c.UnreadCount > 0)
            .OrderByDescending(c => c.LastMessageTimestamp)
            .Take(MaxRecent)
            .Select(c => new WidgetRow(c.Id, c.Address, c.DisplayNameOrAddress, showText ? c.PreviewText : "New message", When(c.LastMessageTimestamp, todayLocal)))
            .ToList();
        return new WidgetSnapshot(chats.Sum(c => c.UnreadCount), favorites, recent);
    }

    private static string When(DateTimeOffset timestamp, DateTime todayLocal)
    {
        var local = timestamp.LocalDateTime;
        return local.Date == todayLocal.Date ? local.ToString("h:mm tt") : local.ToString("MMM d");
    }
}
