using ForgeLinkSms.Core.Help;

namespace ForgeLinkSms.Core.Tests.Assistant;

/// Layer 1: offline. Every question in the bank must reach the right help topic, because the server can only answer from what the app sends it.
public class AssistantRetrievalTests
{
    public static IEnumerable<object[]> HowTo() => QuestionBank.Of("howto");
    public static IEnumerable<object[]> Fixed() => QuestionBank.Of("fixed");
    public static IEnumerable<object[]> OffTopic() => QuestionBank.Of("offtopic");

    [Theory]
    [MemberData(nameof(HowTo))]
    public void A_how_to_question_finds_its_help_topic_among_the_three_sent(BankQuestion q)
    {
        var titles = HelpMatcher.Match(q.Q, 3).Select(e => e.Title).ToList();

        Assert.True(titles.Any(q.ExpectsTopic), $"Expected {q.Topic} but the app would send: {string.Join(", ", titles)}");
    }

    [Theory]
    [MemberData(nameof(HowTo))]
    public void A_how_to_question_stays_on_the_help_pages_and_is_never_sent_to_a_web_search(BankQuestion q)
    {
        Assert.True(HelpMatcher.IsAboutApp(q.Q), "Treated as a general question, so with Googling on it would go to the web");
    }

    [Theory]
    [MemberData(nameof(HowTo))]
    public void The_help_text_sent_holds_what_the_answer_needs(BankQuestion q)
    {
        var sent = string.Join(" ", HelpMatcher.Match(q.Q, 3).Select(e => e.Text));

        Assert.True(q.AnswerCovers(sent), $"The three help excerpts do not mention: {string.Join(", ", q.MustContain)}");
    }

    [Theory]
    [MemberData(nameof(Fixed))]
    public void Price_refund_and_privacy_questions_get_the_fixed_answer(BankQuestion q)
    {
        var answer = AssistantFixedAnswers.Find(q.Q);

        Assert.NotNull(answer);
        Assert.True(q.AnswerCovers(answer!), answer);
    }

    [Theory]
    [MemberData(nameof(OffTopic))]
    public void An_everyday_question_is_not_taken_for_a_ForgeLink_question(BankQuestion q)
    {
        Assert.False(HelpMatcher.IsAboutApp(q.Q));
        Assert.Null(AssistantFixedAnswers.Find(q.Q));
    }

    [Theory]
    [InlineData("how do I restore my messages")]
    [InlineData("do link previews send my data anywhere")]
    [InlineData("how do I export my texts")]
    public void A_how_to_question_is_not_swallowed_by_the_fixed_privacy_answer(string question)
    {
        Assert.Null(AssistantFixedAnswers.Find(question));
    }
}
