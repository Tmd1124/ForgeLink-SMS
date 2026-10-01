using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class WidgetContentTests
{
    private static readonly DateTime Today = new(2026, 9, 30);

    private static SmsThread Chat(long id, string name, int unread = 0, bool favorite = false, int hoursAgo = 1, string body = "hi") => new()
    {
        Id = id,
        Address = $"555000{id:0000}",
        DisplayName = name,
        LastMessageBody = body,
        LastMessageTimestamp = new DateTimeOffset(Today.AddHours(12 - hoursAgo), TimeZoneInfo.Local.GetUtcOffset(Today)),
        UnreadCount = unread,
        IsFavorite = favorite
    };

    [Fact]
    public void Totals_unread_across_the_chats_it_is_given()
    {
        var snapshot = WidgetContent.Build(new[] { Chat(1, "A", unread: 2), Chat(2, "B", unread: 3), Chat(3, "C") }, true, Today);

        Assert.Equal(5, snapshot.TotalUnread);
    }

    [Fact]
    public void Favorites_with_unread_come_first_then_the_most_recent_and_stop_at_five()
    {
        var chats = new[]
        {
            Chat(1, "Old fav", favorite: true, hoursAgo: 9),
            Chat(2, "Two unread", unread: 2, favorite: true, hoursAgo: 5),
            Chat(3, "Not fav", unread: 9),
            Chat(4, "Recent fav", favorite: true, hoursAgo: 1),
            Chat(5, "Five unread", unread: 5, favorite: true, hoursAgo: 8),
            Chat(6, "F6", favorite: true, hoursAgo: 2),
            Chat(7, "F7", favorite: true, hoursAgo: 3),
        };

        var names = WidgetContent.Build(chats, true, Today).Favorites.Select(f => f.Name);

        Assert.Equal(new[] { "Five unread", "Two unread", "Recent fav", "F6", "F7" }, names);
    }

    [Fact]
    public void Recent_unread_lists_the_three_newest_unread_chats_with_text_and_time()
    {
        var chats = new[]
        {
            Chat(1, "A", unread: 1, hoursAgo: 4, body: "four"),
            Chat(2, "B", unread: 1, hoursAgo: 1, body: "Loved “Sounds good”"),
            Chat(3, "C", unread: 1, hoursAgo: 2, body: "two"),
            Chat(4, "D", unread: 1, hoursAgo: 3, body: "three"),
            Chat(5, "E", hoursAgo: 0, body: "read"),
        };

        var rows = WidgetContent.Build(chats, true, Today).RecentUnread;

        Assert.Equal(new[] { "B", "C", "D" }, rows.Select(r => r.Name));
        Assert.Equal("❤️ to “Sounds good”", rows[0].Text);
        Assert.Equal(Today.AddHours(11).ToString("h:mm tt"), rows[0].When);
    }

    [Fact]
    public void Message_text_can_be_hidden()
    {
        var rows = WidgetContent.Build(new[] { Chat(1, "A", unread: 1, body: "secret") }, showText: false, Today).RecentUnread;

        Assert.Equal("New message", rows.Single().Text);
    }

    [Fact]
    public void Older_messages_show_a_date()
    {
        var rows = WidgetContent.Build(new[] { Chat(1, "A", unread: 1, hoursAgo: 72) }, true, Today).RecentUnread;

        Assert.Equal(Today.AddHours(-60).ToString("MMM d"), rows.Single().When);
    }

    [Fact]
    public void Initials_and_photo_are_carried_for_each_favorite()
    {
        var chat = Chat(1, "Kim Donnelly", favorite: true);

        var person = WidgetContent.Build(new[] { chat }, true, Today).Favorites.Single();

        Assert.Equal((1L, chat.Address, "Kim Donnelly", chat.Initials, (string?)null, 0), (person.ThreadId, person.Address, person.Name, person.Initials, person.PhotoUri, person.Unread));
    }

    [Fact]
    public void Nothing_unread_and_no_favorites_gives_empty_lists()
    {
        var snapshot = WidgetContent.Build(new[] { Chat(1, "A") }, true, Today);

        Assert.Equal((0, 0, 0), (snapshot.TotalUnread, snapshot.Favorites.Count, snapshot.RecentUnread.Count));
    }
}
