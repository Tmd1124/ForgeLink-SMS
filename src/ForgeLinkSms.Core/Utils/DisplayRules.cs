using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class DisplayRules
{
    private const int RecentCount = 8;

    public static readonly IReadOnlyList<int> TextScales = new[] { 90, 100, 115, 130 };

    public static int TextScaleOrDefault(int stored) => TextScales.Contains(stored) ? stored : 100;

    // Unnamed numbers never get a bubble: an initials-less "+1813…" bubble is just noise.
    public static IReadOnlyList<SmsThread> BubbleMembers(IEnumerable<SmsThread> chats, DisplaySettings settings)
    {
        if (settings.BubbleEveryone)
        {
            return PondSelector.Rank(chats).ToList();
        }
        var named = chats.Where(t => !string.IsNullOrWhiteSpace(t.DisplayName)).ToList();
        var recentIds = settings.BubbleRecent
            ? named.OrderByDescending(t => t.LastMessageTimestamp).Take(RecentCount).Select(t => t.Id).ToHashSet()
            : new HashSet<long>();
        return PondSelector.Rank(named.Where(t =>
                (settings.BubbleFavorites && t.IsFavorite) ||
                (settings.BubbleUnread && t.UnreadCount > 0) ||
                (settings.BubbleFiltered && t.FilterIds.Count > 0) ||
                recentIds.Contains(t.Id)))
            .ToList();
    }

    public static string? RowColor(SmsThread thread, DisplaySettings settings, IEnumerable<Filter> filters)
    {
        if (thread.UnreadCount > 0)
        {
            return settings.UnreadColor;
        }
        return settings.ColorBy switch
        {
            ListColorMode.Person => PondSelector.ColorFor(thread.Address),
            ListColorMode.Filter => thread.FilterIds
                .Select(id => filters.FirstOrDefault(f => f.Id == id)?.ColorHex)
                .FirstOrDefault(color => !string.IsNullOrEmpty(color)),
            _ => null
        };
    }

    // Switching to Bubbles starts with everyone, as the layout always did. Everyone in
    // Bubbles + list would leave the list empty, so switching there turns it back off.
    public static DisplaySettings WithLayout(DisplaySettings current, ConversationDisplayStyle layout)
    {
        var chosen = current.Clone();
        if (layout == ConversationDisplayStyle.Bubbles && current.Layout != layout)
        {
            chosen.BubbleEveryone = true;
        }
        else if (layout == ConversationDisplayStyle.BubblesAndCards)
        {
            chosen.BubbleEveryone = false;
        }
        chosen.Layout = layout;
        return chosen;
    }

    public static DisplaySettings FavoritesAndUnreadPreset(DisplaySettings current)
    {
        var preset = current.Clone();
        preset.Layout = ConversationDisplayStyle.BubblesAndCards;
        (preset.BubbleEveryone, preset.BubbleFavorites, preset.BubbleUnread, preset.BubbleRecent, preset.BubbleFiltered) = (false, true, true, false, false);
        preset.ColorBy = ListColorMode.Person;
        preset.UnreadColor = DisplaySettings.Gray;
        return preset;
    }

    public static DisplaySettings BubblesOnlyFavoritesAndUnreadPreset(DisplaySettings current)
    {
        var preset = current.Clone();
        preset.Layout = ConversationDisplayStyle.Bubbles;
        (preset.BubbleEveryone, preset.BubbleFavorites, preset.BubbleUnread, preset.BubbleRecent, preset.BubbleFiltered) = (false, true, true, false, false);
        return preset;
    }

    public static DisplaySettings UnreadOnlyColorPreset(DisplaySettings current)
    {
        var preset = current.Clone();
        preset.ColorBy = ListColorMode.None;
        preset.UnreadColor = DisplaySettings.Gray;
        return preset;
    }

    public static DisplaySettings FavoritesWithFilterColorsPreset(DisplaySettings current)
    {
        var preset = current.Clone();
        preset.Layout = ConversationDisplayStyle.BubblesAndCards;
        (preset.BubbleEveryone, preset.BubbleFavorites, preset.BubbleUnread, preset.BubbleRecent, preset.BubbleFiltered) = (false, true, false, false, false);
        preset.ColorBy = ListColorMode.Filter;
        preset.UnreadColor = DisplaySettings.Gray;
        return preset;
    }
}
