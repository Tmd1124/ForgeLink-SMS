using ForgeLinkSms.Core.Help;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Help;

public class HelpCatalogTests
{
    // Every screen the app can show.
    public static TheoryData<string, string> Screens => new()
    {
        { "conversations", "Chats" },
        { "conversations/thread?id=4&address=555", "Chat" },
        { "conversations/media?id=4", "Photos & links" },
        { "conversations/group?id=4&address=555", "Group details" },
        { "compose", "New message" },
        { "menu", "Menu" },
        { "settings", "Settings" },
        { "settings/appearance", "Appearance" },
        { "settings/notifications", "Notifications" },
        { "settings/chat-list", "Chat list" },
        { "settings/backup", "Backup & restore" },
        { "settings/help", "Help & feedback" },
        { "settings/privacy", "Privacy" },
        { "scrolling", "Scrolling" },
        { "filters", "Filters" },
        { "scheduled", "Scheduled" },
        { "reminders", "Reminders" },
        { "archived", "Archived" },
        { "snoozed", "Snoozed" },
        { "blocked", "Blocked" },
        { "trash", "Trash" },
        { "theme", "Theme" },
        { "profile", "Your Profile" },
        { "onboarding", "Welcome" },
        { "rate", "Rate ForgeLink" }
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_screen_has_its_own_help(string route, string title)
    {
        var topic = HelpCatalog.ForRoute(route);

        Assert.Equal(title, topic.Title);
        Assert.False(string.IsNullOrWhiteSpace(topic.Summary));
    }

    [Theory]
    [InlineData(ConversationLane.Conversations, "Chats")]
    [InlineData(ConversationLane.Updates, "Updates")]
    [InlineData(ConversationLane.Screener, "Screener")]
    public void The_chat_list_help_follows_the_open_tab(ConversationLane lane, string title) =>
        Assert.Equal(title, HelpCatalog.ForRoute("conversations", lane).Title);

    [Fact]
    public void Updates_and_Screener_keep_the_explanations_that_used_to_sit_above_their_lists()
    {
        var updates = HelpCatalog.ForRoute("conversations", ConversationLane.Updates);
        var screener = HelpCatalog.ForRoute("conversations", ConversationLane.Screener);

        Assert.Contains(updates.Tips, t => t.Contains("verification codes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(screener.Icons, i => i.Text.Contains("7726"));
    }

    [Fact]
    public void An_unknown_screen_still_gets_general_help()
    {
        var topic = HelpCatalog.ForRoute("somewhere-new");

        Assert.Equal("ForgeLink SMS", topic.Title);
    }

    [Fact]
    public void Every_icon_used_has_a_drawing_and_every_line_stays_short()
    {
        foreach (var topic in HelpCatalog.All)
        {
            Assert.All(topic.Icons, item =>
            {
                Assert.Contains(item.Icon, HelpCatalog.IconNames);
                Assert.True(item.Text.Length <= 44, $"{topic.Title}: \"{item.Text}\" is too long");
            });
            Assert.All(topic.Tips, tip => Assert.True(tip.Length <= 90, $"{topic.Title}: \"{tip}\" is too long"));
            Assert.True(topic.Summary.Length <= 70, $"{topic.Title} summary is too long");
        }
    }

    [Theory]
    [InlineData(5, "★★★★★")]
    [InlineData(3, "★★★☆☆")]
    [InlineData(9, "★★★★★")]
    [InlineData(0, "★☆☆☆☆")]
    public void A_rating_becomes_an_email_to_AzureForge_with_the_stars_in_the_subject(int stars, string shown)
    {
        var email = AzureForge.RatingEmail(stars, "Love the bubbles", new AppDetails("1.0 (1)", "samsung SM-F966U", "Android 16"));

        Assert.Equal(AzureForge.Email, email.To);
        Assert.Contains(shown, email.Subject);
        Assert.Contains("Love the bubbles", email.Body);
        Assert.Contains("samsung SM-F966U", email.Body);
    }

    [Fact]
    public void A_rating_without_a_comment_still_reads_well()
    {
        var email = AzureForge.RatingEmail(4, "  ", new AppDetails("1.0 (1)", "Pixel 9", "Android 16"));

        Assert.StartsWith("I rate ForgeLink SMS 4 out of 5 stars.", email.Body);
    }
}
