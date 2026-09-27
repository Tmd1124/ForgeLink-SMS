using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class DisplayRulesTests
{
    private static SmsThread Thread(long id, string? name = "Name", bool favorite = false, int unread = 0, int minutesAgo = 0, params long[] filterIds) => new()
    {
        Id = id,
        Address = $"55500000{id:00}",
        DisplayName = name,
        LastMessageBody = "hi",
        IsFavorite = favorite,
        UnreadCount = unread,
        LastMessageTimestamp = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        FilterIds = filterIds.ToList()
    };

    private static DisplaySettings Only(bool favorites = false, bool unread = false, bool recent = false, bool filtered = false) => new()
    {
        BubbleFavorites = favorites,
        BubbleUnread = unread,
        BubbleRecent = recent,
        BubbleFiltered = filtered
    };

    [Fact]
    public void Checked_groups_decide_who_gets_a_bubble()
    {
        var chats = new[] { Thread(1, favorite: true, minutesAgo: 50), Thread(2, unread: 3, minutesAgo: 40), Thread(3, minutesAgo: 30), Thread(4, minutesAgo: 20, filterIds: 7) };

        Assert.Equal(new long[] { 1, 2 }, DisplayRules.BubbleMembers(chats, Only(favorites: true, unread: true)).Select(t => t.Id));
        Assert.Equal(new long[] { 4 }, DisplayRules.BubbleMembers(chats, Only(filtered: true)).Select(t => t.Id));
        Assert.Empty(DisplayRules.BubbleMembers(chats, Only()));
    }

    [Fact]
    public void Recent_means_the_eight_most_recent_named_chats()
    {
        var chats = Enumerable.Range(1, 12).Select(i => Thread(i, minutesAgo: i)).Append(Thread(99, name: null, minutesAgo: 0));

        var members = DisplayRules.BubbleMembers(chats, Only(recent: true));

        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), members.Select(t => t.Id));
    }

    [Fact]
    public void Bubbles_are_ranked_favorites_then_unread_then_recent()
    {
        var chats = new[] { Thread(1, minutesAgo: 1), Thread(2, unread: 1, minutesAgo: 5), Thread(3, favorite: true, minutesAgo: 9) };

        Assert.Equal(new long[] { 3, 2, 1 }, DisplayRules.BubbleMembers(chats, Only(favorites: true, unread: true, recent: true)).Select(t => t.Id));
    }

    [Fact]
    public void Unread_rows_use_the_unread_color_whatever_the_color_mode()
    {
        var unread = Thread(1, unread: 2);

        Assert.Equal(DisplaySettings.Gray, DisplayRules.RowColor(unread, new DisplaySettings { ColorBy = ListColorMode.Person }, []));
        Assert.Equal("#ff0000", DisplayRules.RowColor(unread, new DisplaySettings { ColorBy = ListColorMode.None, UnreadColor = "#ff0000" }, []));
    }

    [Fact]
    public void No_color_for_unread_leaves_unread_rows_plain()
    {
        Assert.Null(DisplayRules.RowColor(Thread(1, unread: 2), new DisplaySettings { ColorBy = ListColorMode.Person, UnreadColor = null }, []));
    }

    [Fact]
    public void Read_rows_follow_the_color_mode()
    {
        var read = Thread(1, filterIds: [5, 6]);
        var filters = new[] { new Filter { Id = 5, ColorHex = "#16a34a" }, new Filter { Id = 6, ColorHex = "#f59e0b" } };

        Assert.Equal(PondSelector.ColorFor(read.Address), DisplayRules.RowColor(read, new DisplaySettings { ColorBy = ListColorMode.Person }, filters));
        Assert.Equal("#16a34a", DisplayRules.RowColor(read, new DisplaySettings { ColorBy = ListColorMode.Filter }, filters));
        Assert.Null(DisplayRules.RowColor(read, new DisplaySettings { ColorBy = ListColorMode.None }, filters));
        Assert.Null(DisplayRules.RowColor(Thread(2), new DisplaySettings { ColorBy = ListColorMode.Filter }, filters));
    }

    [Fact]
    public void Favorites_and_unread_preset()
    {
        var preset = DisplayRules.FavoritesAndUnreadPreset(new DisplaySettings { Layout = ConversationDisplayStyle.Cards, BubbleRecent = true, BubbleFiltered = true, ColorBy = ListColorMode.None, UnreadColor = null });

        Assert.Equal(ConversationDisplayStyle.BubblesAndCards, preset.Layout);
        Assert.True(preset.BubbleFavorites && preset.BubbleUnread);
        Assert.False(preset.BubbleRecent || preset.BubbleFiltered);
        Assert.Equal(ListColorMode.Person, preset.ColorBy);
        Assert.Equal(DisplaySettings.Gray, preset.UnreadColor);
    }

    [Fact]
    public void Unread_only_color_preset_keeps_the_layout_and_bubbles()
    {
        var current = new DisplaySettings { Layout = ConversationDisplayStyle.Cards, BubbleFiltered = true, ColorBy = ListColorMode.Person, UnreadColor = null };

        var preset = DisplayRules.UnreadOnlyColorPreset(current);

        Assert.Equal(ConversationDisplayStyle.Cards, preset.Layout);
        Assert.True(preset.BubbleFiltered);
        Assert.Equal(ListColorMode.None, preset.ColorBy);
        Assert.Equal(DisplaySettings.Gray, preset.UnreadColor);
        Assert.Equal(ListColorMode.Person, current.ColorBy);
    }

    [Fact]
    public void Favorites_with_filter_colors_preset()
    {
        var preset = DisplayRules.FavoritesWithFilterColorsPreset(new DisplaySettings { Layout = ConversationDisplayStyle.Bubbles, UnreadColor = null });

        Assert.Equal(ConversationDisplayStyle.BubblesAndCards, preset.Layout);
        Assert.True(preset.BubbleFavorites);
        Assert.False(preset.BubbleUnread || preset.BubbleRecent || preset.BubbleFiltered);
        Assert.Equal(ListColorMode.Filter, preset.ColorBy);
        Assert.Equal(DisplaySettings.Gray, preset.UnreadColor);
    }

    [Fact]
    public void Everyone_puts_every_chat_in_a_bubble_including_unnamed_numbers()
    {
        var chats = new[] { Thread(1, minutesAgo: 1), Thread(2, name: null, minutesAgo: 2) };

        Assert.Equal(new long[] { 1, 2 }, DisplayRules.BubbleMembers(chats, new DisplaySettings { BubbleEveryone = true, BubbleRecent = false }).Select(t => t.Id));
    }

    [Fact]
    public void Bubbles_only_favorites_and_unread_preset()
    {
        var preset = DisplayRules.BubblesOnlyFavoritesAndUnreadPreset(new DisplaySettings { BubbleEveryone = true, BubbleRecent = true });

        Assert.Equal(ConversationDisplayStyle.Bubbles, preset.Layout);
        Assert.True(preset.BubbleFavorites && preset.BubbleUnread);
        Assert.False(preset.BubbleEveryone || preset.BubbleRecent || preset.BubbleFiltered);
    }
}
