using ForgeLinkSms.Core.Help;

namespace ForgeLinkSms.Core.Tests.Help;

public class HelpMatcherTests
{
    [Fact]
    public void A_question_about_pinning_finds_the_chats_help_first()
    {
        var found = HelpMatcher.Match("How do I pin a conversation?");

        Assert.NotEmpty(found);
        Assert.Equal("Chats", found[0].Title);
    }

    [Fact]
    public void Back_up_with_a_space_still_finds_backup()
    {
        var found = HelpMatcher.Match("how do I back up my texts");

        Assert.Contains(found, e => e.Title.Contains("Backup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_matches_gibberish()
    {
        Assert.Empty(HelpMatcher.Match("zzzz qqqq xxxx"));
    }

    [Fact]
    public void At_most_the_requested_number_come_back_each_within_the_size_cap()
    {
        var found = HelpMatcher.Match("message chat settings send", 2);

        Assert.InRange(found.Count, 1, 2);
        Assert.All(found, e => Assert.True(e.Text.Length <= 900));
    }
}
