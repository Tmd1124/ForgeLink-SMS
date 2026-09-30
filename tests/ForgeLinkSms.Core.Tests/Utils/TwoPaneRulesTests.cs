using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class TwoPaneRulesTests
{
    [Theory]
    [InlineData(599, "conversations", false)]
    [InlineData(600, "conversations", true)]
    [InlineData(832, "conversations/thread?id=4&address=555", true)]
    [InlineData(832, "/conversations/thread?id=4", true)]
    [InlineData(412, "conversations/thread?id=4", false)]
    [InlineData(832, "settings", false)]
    [InlineData(832, "conversations/media?id=4", false)]
    [InlineData(832, "compose", false)]
    [InlineData(832, "", false)]
    public void Two_panes_show_only_for_chats_on_a_wide_screen(double width, string uri, bool expected)
    {
        Assert.Equal(expected, TwoPaneRules.ShowsTwoPanes(width, uri));
    }

    [Fact]
    public void The_open_chat_is_read_from_the_route()
    {
        Assert.Equal(4, TwoPaneRules.OpenThreadId("conversations/thread?id=4&address=555"));
        Assert.Null(TwoPaneRules.OpenThreadId("conversations"));
        Assert.Null(TwoPaneRules.OpenThreadId("conversations/thread?address=555"));
        Assert.True(TwoPaneRules.IsChat("conversations/thread?id=4"));
        Assert.False(TwoPaneRules.IsChat("conversations"));
    }
}
