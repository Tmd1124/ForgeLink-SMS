using ForgeLinkSms.Core.Help;

namespace ForgeLinkSms.Core.Tests.Help;

public class AssistantFixedAnswersTests
{
    [Theory]
    [InlineData("How much does it cost?", "$4.99")]
    [InlineData("is there a subscription", "one-time")]
    [InlineData("Can I get a refund", "AzureForgeAI.com")]
    [InlineData("does it read my messages", "stay on your phone")]
    [InlineData("Is my data private?", "stay on your phone")]
    public void Price_refund_and_privacy_questions_get_fixed_text(string question, string expected)
    {
        Assert.Contains(expected, AssistantFixedAnswers.Find(question));
    }

    [Theory]
    [InlineData("how do I pin a chat")]
    [InlineData("my texts will not send")]
    [InlineData("")]
    public void Other_questions_get_no_fixed_answer(string question)
    {
        Assert.Null(AssistantFixedAnswers.Find(question));
    }
}
