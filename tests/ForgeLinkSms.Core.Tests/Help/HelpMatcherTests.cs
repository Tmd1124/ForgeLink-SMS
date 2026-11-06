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

    [Theory]
    [InlineData("How do I pin a conversation?")]
    [InlineData("How do I back up my texts?")]
    [InlineData("How do I mute a chat?")]
    [InlineData("How do I change the theme?")]
    [InlineData("Why am I not getting notifications?")]
    public void Questions_about_using_the_app_are_recognized(string question) =>
        Assert.True(HelpMatcher.IsAboutApp(question));

    [Theory]
    [InlineData("Who won the game last night?")]
    [InlineData("What is the tallest building in the world?")]
    [InlineData("What is the weather in Atlanta tomorrow?")]
    [InlineData("Is it going to rain this weekend?")]
    [InlineData("Who is the president of France?")]
    [InlineData("")]
    public void Everyday_questions_are_not_treated_as_app_questions(string question) =>
        Assert.False(HelpMatcher.IsAboutApp(question));
}
